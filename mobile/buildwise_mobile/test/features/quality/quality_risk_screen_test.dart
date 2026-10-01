import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:buildwise_mobile/features/quality/services/quality_api_service.dart';
import 'package:buildwise_mobile/features/quality/screens/quality_risk_screen.dart';

void main() {
  setUp(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
          (_) async => 'test-jwt',
        ),
  );
  tearDown(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
          null,
        ),
  );
  test('quality overview reads real non-conformance records', () async {
    final client = MockClient((request) async {
      expect(request.url.path, '/api/non-conformances');
      expect(request.headers['Authorization'], 'Bearer test-jwt');
      return http.Response(
        jsonEncode([
          {'id': 812, 'status': 'Open', 'issueDescription': 'Recorded issue'},
        ]),
        200,
      );
    });
    addTearDown(client.close);
    final records = await QualityApiService(
      apiClient: ApiClient(client: client),
    ).getNonConformances();
    expect(records.single['id'], 812);
    expect(records.single['issueDescription'], 'Recorded issue');
  });
  for (final status in ['Completed', 'Failed']) {
    testWidgets(
      '$status quality workflow only shows validated public recommendation',
      (tester) async {
        var posts = 0;
        final client = MockClient((request) async {
          posts++;
          expect(
            request.url.path,
            '/api/quality-risk-agent/inspections/789/analyse',
          );
          expect(request.headers['Authorization'], 'Bearer test-jwt');
          return http.Response(
            jsonEncode({
              'workflowId': 890,
              'inspectionId': 789,
              'status': status,
              'steps': [
                {
                  'structuredResult': {
                    'trace': 'DO NOT DISPLAY INTERNAL TRACE',
                    'recommendation': {
                      'riskLevel': 'High',
                      'evidenceSummary': 'Recorded damaged material',
                      'rationaleSummary': 'Public assessment',
                      'ncrRecommended': true,
                      'riskFlags': [],
                      'itemRecommendations': [],
                    },
                  },
                },
              ],
            }),
            200,
          );
        });
        addTearDown(client.close);
        await tester.pumpWidget(
          MaterialApp(
            home: QualityRiskScreen(
              inspectionId: 789,
              service: QualityApiService(apiClient: ApiClient(client: client)),
            ),
          ),
        );
        await tester.tap(find.text('Run Quality Analysis'));
        await tester.pumpAndSettle();
        expect(posts, 1);
        expect(
          find.text('Public assessment'),
          status == 'Completed' ? findsOneWidget : findsNothing,
        );
        expect(find.textContaining('DO NOT DISPLAY'), findsNothing);
        expect(find.text('Create NCR'), findsNothing);
        expect(
          find.textContaining('AI does not create an NCR'),
          findsOneWidget,
        );
      },
    );
  }
}
