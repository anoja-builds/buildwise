import 'dart:async';
import 'dart:convert';

import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:buildwise_mobile/core/theme/app_theme.dart';
import 'package:buildwise_mobile/features/quality/models/inspection_record.dart';
import 'package:buildwise_mobile/features/quality/screens/inspection_record_screen.dart';
import 'package:buildwise_mobile/features/quality/services/quality_api_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

Map<String, dynamic> record({bool completed = false}) => {
  'id': 12,
  'deliveryId': 7,
  'inspectorUserId': 42,
  'deliveryReference': 'DEL-007',
  'status': completed ? 'Completed' : 'UnderInspection',
  'overallDecision': completed ? 'PartiallyAccepted' : null,
  'notes': 'Initial notes',
  'deliveryItems': [
    {
      'deliveryItemId': 9,
      'purchaseOrderItemId': 2,
      'receivedQuantity': 240,
      'damagedQuantity': 5,
    },
  ],
  'items': completed
      ? [
          {
            'id': 13,
            'deliveryItemId': 9,
            'condition': 'Five damaged bags',
            'acceptedQuantity': 235,
            'rejectedQuantity': 5,
            'remarks': 'Observed on site',
          },
        ]
      : [],
};
http.Response response({bool completed = false}) =>
    http.Response(jsonEncode(record(completed: completed)), 200);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const storage = MethodChannel('plugins.it_nomads.com/flutter_secure_storage');
  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          storage,
          (call) async => call.method == 'read' ? 'test-jwt' : null,
        );
  });
  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storage, null);
  });

  QualityApiService service(
    Future<http.Response> Function(http.Request) handler,
  ) {
    final client = MockClient(handler);
    addTearDown(client.close);
    return QualityApiService(apiClient: ApiClient(client: client));
  }

  Future<void> open(
    WidgetTester tester,
    Future<http.Response> Function(http.Request) handler,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: InspectionRecordScreen(
          inspectionId: 12,
          service: service(handler),
        ),
      ),
    );
  }

  Finder field(String label) => find.widgetWithText(TextField, label);
  Future<void> fill(WidgetTester tester, {String accepted = '235'}) async {
    await tester.enterText(field('Condition'), 'Five damaged bags');
    await tester.enterText(field('Accepted quantity'), accepted);
    await tester.enterText(field('Rejected quantity'), '5');
    await tester.enterText(field('Remarks'), 'Observed on site');
    await tester.ensureVisible(find.byType(DropdownButtonFormField<String>));
    await tester.tap(find.byType(DropdownButtonFormField<String>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Partially Accepted').last);
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Complete Inspection'));
  }

  test(
    'complete request uses shared JWT and exact DTO without inspector identity',
    () async {
      final api = service((request) async {
        expect(request.url.path, endsWith('/inspections/12/complete'));
        expect(request.method, 'POST');
        expect(request.headers['Authorization'], 'Bearer test-jwt');
        expect(jsonDecode(request.body), {
          'overallDecision': 'PartiallyAccepted',
          'notes': 'Reviewed',
          'items': [
            {
              'deliveryItemId': 9,
              'condition': 'Damaged',
              'acceptedQuantity': 235.0,
              'rejectedQuantity': 5.0,
              'remarks': 'Observed',
            },
          ],
        });
        return response(completed: true);
      });
      final result = await api.completeInspection(
        12,
        overallDecision: 'PartiallyAccepted',
        notes: 'Reviewed',
        items: [
          const InspectionItemSubmission(
            deliveryItemId: 9,
            acceptedQuantity: 235,
            rejectedQuantity: 5,
            condition: 'Damaged',
            remarks: 'Observed',
          ),
        ],
      );
      expect(result.completed, isTrue);
      expect(result.items.single.damaged, 5);
    },
  );

  testWidgets('loads delivery quantities without choosing inspection results', (
    tester,
  ) async {
    final pending = Completer<http.Response>();
    await open(tester, (_) => pending.future);
    expect(find.byType(LinearProgressIndicator), findsOneWidget);
    pending.complete(response());
    await tester.pumpAndSettle();
    expect(find.text('DEL-007'), findsOneWidget);
    expect(find.text('Received: 240.00'), findsOneWidget);
    expect(find.text('Damaged: 5.00'), findsOneWidget);
    expect(
      tester.widget<TextField>(field('Accepted quantity')).controller!.text,
      isEmpty,
    );
    expect(
      tester.widget<TextField>(field('Rejected quantity')).controller!.text,
      isEmpty,
    );
  });

  testWidgets(
    'successful completion prevents duplicate taps and displays saved results',
    (tester) async {
      final pending = Completer<http.Response>();
      var posts = 0;
      await open(tester, (request) async {
        if (request.method == 'GET') return response();
        posts++;
        expect(jsonDecode(request.body)['items'][0]['acceptedQuantity'], 235);
        return pending.future;
      });
      await tester.pumpAndSettle();
      await fill(tester);
      await tester.tap(find.text('Complete Inspection'));
      await tester.pump();
      await tester.tap(find.text('Saving...'));
      await tester.pump();
      expect(posts, 1);
      expect(
        tester.widget<TextField>(field('Accepted quantity')).readOnly,
        isTrue,
      );
      pending.complete(response(completed: true));
      await tester.pumpAndSettle();
      expect(find.text('Complete Inspection'), findsNothing);
      expect(find.text('Overall decision: Partially Accepted'), findsOneWidget);
      await tester.ensureVisible(
        find.text('Inspection completed successfully.'),
      );
      expect(find.text('Inspection completed successfully.'), findsOneWidget);
    },
  );

  for (final value in ['-1', '1.001', 'NaN', '']) {
    testWidgets('invalid quantity $value is not submitted', (tester) async {
      var posts = 0;
      await open(tester, (request) async {
        if (request.method == 'POST') posts++;
        return response();
      });
      await tester.pumpAndSettle();
      await fill(tester, accepted: value);
      await tester.tap(find.text('Complete Inspection'));
      await tester.pumpAndSettle();
      expect(posts, 0);
      await tester.ensureVisible(
        find.textContaining('enter nonnegative quantities'),
      );
      expect(
        find.textContaining('enter nonnegative quantities'),
        findsOneWidget,
      );
    });
  }

  for (final entry in {
    400: 'Total exceeds received quantity',
    401: 'sign in again',
    403: 'permission',
    409: 'Inspection has already been completed.',
  }.entries) {
    testWidgets(
      'completion HTTP ${entry.key} preserves entered values and shows error',
      (tester) async {
        await open(
          tester,
          (request) async => request.method == 'GET'
              ? response()
              : http.Response(jsonEncode({'detail': entry.value}), entry.key),
        );
        await tester.pumpAndSettle();
        await fill(tester);
        await tester.tap(find.text('Complete Inspection'));
        await tester.pumpAndSettle();
        await tester.ensureVisible(find.textContaining(entry.value));
        expect(find.textContaining(entry.value), findsOneWidget);
        expect(
          tester.widget<TextField>(field('Accepted quantity')).controller!.text,
          '235',
        );
      },
    );
  }

  testWidgets(
    'lost completion response can be reconciled without another POST',
    (tester) async {
      var posts = 0;
      await open(tester, (request) async {
        if (request.method == 'POST') {
          posts++;
          throw http.ClientException('offline');
        }
        return response(completed: posts > 0);
      });
      await tester.pumpAndSettle();
      await fill(tester);
      await tester.tap(find.text('Complete Inspection'));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('Check saved status'));
      await tester.tap(find.text('Check saved status'));
      await tester.pumpAndSettle();
      expect(find.text('Inspection completed successfully.'), findsOneWidget);
      expect(posts, 1);
      expect(find.text('Complete Inspection'), findsNothing);
    },
  );

  testWidgets('invalid detail response shows retry and recovers', (
    tester,
  ) async {
    var calls = 0;
    await open(
      tester,
      (_) async => ++calls == 1 ? http.Response('{"id":12}', 200) : response(),
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('invalid response'), findsOneWidget);
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();
    expect(find.text('DEL-007'), findsOneWidget);
  });

  testWidgets('opening completed inspection is read-only', (tester) async {
    await open(tester, (_) async => response(completed: true));
    await tester.pumpAndSettle();
    expect(
      tester.widget<TextField>(field('Accepted quantity')).readOnly,
      isTrue,
    );
    expect(find.text('Complete Inspection'), findsNothing);
  });
}
