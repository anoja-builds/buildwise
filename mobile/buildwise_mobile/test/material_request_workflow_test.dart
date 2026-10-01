import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:buildwise_mobile/features/material_requests/services/material_request_service.dart';
import 'package:buildwise_mobile/features/material_requests/screens/create_material_request_screen.dart';
import 'package:buildwise_mobile/features/material_requests/screens/material_request_list_screen.dart';

void main() {
  const storage = MethodChannel('plugins.it_nomads.com/flutter_secure_storage');
  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          storage,
          (call) async => call.method == 'read' ? 'site-jwt' : null,
        );
  });
  tearDown(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storage, null),
  );
  final options = {
    'projects': [
      {'id': 37, 'name': 'Live project'},
    ],
    'materials': [
      {'id': 81, 'name': 'Live material', 'unit': 'kg'},
    ],
  };

  testWidgets('loads real choices and submits their IDs and unit with JWT', (
    tester,
  ) async {
    Map<String, dynamic>? submitted;
    final client = MockClient((request) async {
      expect(request.headers['Authorization'], 'Bearer site-jwt');
      if (request.url.path.endsWith('/options')) {
        return http.Response(jsonEncode(options), 200);
      }
      submitted = jsonDecode(request.body) as Map<String, dynamic>;
      return http.Response('{}', 201);
    });
    addTearDown(client.close);
    await tester.pumpWidget(
      MaterialApp(
        home: CreateMaterialRequestScreen(
          service: MaterialRequestService(apiClient: ApiClient(client: client)),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('project-select')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Live project').last);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('material-select')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Live material (kg)').last);
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextField, 'Required Quantity (kg)'),
      '12.5',
    );
    await tester.enterText(
      find.widgetWithText(TextField, 'Purpose / Work Reason'),
      'Site demand',
    );
    final submit = find.text('SUBMIT MATERIAL REQUEST');
    await tester.ensureVisible(submit);
    await tester.tap(submit);
    await tester.pumpAndSettle();
    expect(submitted!['projectId'], 37);
    expect(submitted!.containsKey('requestedByUserId'), isFalse);
    expect(submitted!['items'][0]['materialId'], 81);
    expect(submitted!['items'][0]['unit'], 'kg');
    expect(submitted!['items'][0]['quantity'], 12.5);
  });

  testWidgets('lookup failure supports retry and disables submission', (
    tester,
  ) async {
    var fail = true;
    final client = MockClient(
      (request) async => fail
          ? http.Response('Unavailable', 503)
          : http.Response(jsonEncode(options), 200),
    );
    addTearDown(client.close);
    await tester.pumpWidget(
      MaterialApp(
        home: CreateMaterialRequestScreen(
          service: MaterialRequestService(apiClient: ApiClient(client: client)),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(
      tester.widget<ElevatedButton>(find.byType(ElevatedButton)).onPressed,
      isNull,
    );
    fail = false;
    await tester.tap(find.text('Retry loading options'));
    await tester.pumpAndSettle();
    expect(find.text('Retry loading options'), findsNothing);
    expect(find.text('Select project'), findsOneWidget);
  });

  testWidgets('saves multiple selected materials as a server draft', (
    tester,
  ) async {
    Map<String, dynamic>? submitted;
    final client = MockClient((request) async {
      if (request.method == 'GET') {
        return http.Response(
          jsonEncode({
            ...options,
            'materials': [
              {'id': 81, 'name': 'Live material', 'unit': 'kg'},
              {'id': 82, 'name': 'Second material', 'unit': 'bags'},
            ],
          }),
          200,
        );
      }
      submitted = jsonDecode(request.body) as Map<String, dynamic>;
      return http.Response('{}', 201);
    });
    addTearDown(client.close);
    await tester.pumpWidget(
      MaterialApp(
        home: CreateMaterialRequestScreen(
          service: MaterialRequestService(apiClient: ApiClient(client: client)),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('project-select')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Live project').last);
    await tester.pumpAndSettle();
    Future<void> select(String material, String unit, String quantity) async {
      await tester.ensureVisible(find.byKey(const Key('material-select')));
      await tester.tap(find.byKey(const Key('material-select')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('$material ($unit)').last);
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextField, 'Required Quantity ($unit)'),
        quantity,
      );
    }

    await select('Live material', 'kg', '12.5');
    await tester.enterText(
      find.widgetWithText(TextField, 'Purpose / Work Reason'),
      'Site demand',
    );
    await tester.ensureVisible(find.text('Add Another Material Item'));
    await tester.tap(find.text('Add Another Material Item'));
    await tester.pumpAndSettle();
    await select('Second material', 'bags', '10');
    await tester.ensureVisible(find.text('Save Draft'));
    await tester.tap(find.text('Save Draft'));
    await tester.pumpAndSettle();
    expect(submitted!['submitImmediately'], false);
    expect((submitted!['items'] as List).map((i) => i['materialId']), [81, 82]);
    expect(submitted!.containsKey('requestedByUserId'), false);
  });

  testWidgets(
    'site user sees manager-updated status after refresh and resume',
    (tester) async {
      var status = 'PendingApproval';
      final client = MockClient(
        (request) async => http.Response(
          jsonEncode([
            {
              'id': 42,
              'projectName': 'Live project',
              'reason': 'Site demand',
              'status': status,
            },
          ]),
          200,
        ),
      );
      addTearDown(client.close);
      await tester.pumpWidget(
        MaterialApp(
          home: MaterialRequestListScreen(
            service: MaterialRequestService(
              apiClient: ApiClient(client: client),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Pending Approval'), findsNWidgets(2));
      status = 'Approved';
      await tester.tap(find.byTooltip('Refresh request status'));
      await tester.pumpAndSettle();
      expect(find.text('Approved'), findsNWidgets(2));
      status = 'Rejected';
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.hidden);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.hidden);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await tester.pumpAndSettle();
      expect(find.text('Rejected'), findsNWidgets(2));
    },
  );
}
