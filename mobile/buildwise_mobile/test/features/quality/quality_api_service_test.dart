import 'dart:async';
import 'dart:convert';

import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:buildwise_mobile/features/auth/services/auth_service.dart';
import 'package:buildwise_mobile/features/quality/services/quality_api_service.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const storage = MethodChannel('plugins.it_nomads.com/flutter_secure_storage');
  late Map<String, String> stored;
  setUp(() {
    stored = {'buildwise.jwt': 'test-jwt'};
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storage, (call) async {
          final args = call.arguments as Map;
          final key = args['key'] as String;
          switch (call.method) {
            case 'read':
              return stored[key];
            case 'write':
              stored[key] = args['value'] as String;
              return null;
            case 'delete':
              stored.remove(key);
              return null;
          }
          return null;
        });
  });
  tearDown(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storage, null),
  );

  QualityApiService service(
    Future<http.Response> Function(http.Request) handler,
  ) {
    final client = MockClient(handler);
    addTearDown(client.close);
    return QualityApiService(apiClient: ApiClient(client: client));
  }

  final inspection = {
    'id': 12,
    'deliveryId': 7,
    'inspectorUserId': 42,
    'inspectionDate': '2026-09-23T00:00:00Z',
    'status': 'UnderInspection',
    'overallDecision': null,
    'notes': null,
    'createdAt': '2026-09-23T00:00:00Z',
    'updatedAt': '2026-09-23T00:00:00Z',
    'items': [],
  };
  final workflow = {
    'workflowId': 20,
    'inspectionId': 12,
    'deliveryId': 7,
    'objective': 'Review inspection',
    'status': 'Completed',
    'approvalStatus': 'Pending',
    'finalOutcome': 'Advisory only',
    'createdAt': '2026-09-23T00:00:00Z',
    'updatedAt': '2026-09-23T00:00:00Z',
    'startedAt': null,
    'completedAt': null,
    'steps': [],
  };

  test('pending deliveries use shared URL and stored JWT and parse quantities', () async {
    final api = service((request) async {
      expect(
        request.url.toString(),
        '${apiBaseUrl.replaceFirst(RegExp(r'/+$'), '')}/inspections/pending-deliveries',
      );
      expect(request.headers['Authorization'], 'Bearer test-jwt');
      expect(request.method, 'GET');
      return http.Response(
        jsonEncode([
          {
            'deliveryId': 7,
            'deliveryReference': null,
            'status': 'Received',
            'items': [
              {
                'deliveryItemId': 1,
                'purchaseOrderItemId': 2,
                'receivedQuantity': 10,
              },
              {
                'deliveryItemId': 3,
                'purchaseOrderItemId': 4,
                'receivedQuantity': 2.25,
              },
            ],
          },
        ]),
        200,
      );
    });
    final result = await api.getPendingDeliveries();
    expect(result.single.status, 'Received');
    expect(result.single.deliveryReference, isNull);
    expect(result.single.items.map((i) => i.receivedQuantity), [10.0, 2.25]);
  });

  test('empty array remains a successful result', () async {
    expect(
      await service((_) async => http.Response('[]', 200))
          .getPendingDeliveries(),
      isEmpty,
    );
  });

  for (final notes in [null, 'Inspect carefully']) {
    test(
      'start request sends only deliveryId and optional notes: $notes',
      () async {
        final api = service((request) async {
          expect(request.method, 'POST');
          expect(request.url.path, '/api/inspections');
          expect(request.headers['Authorization'], 'Bearer test-jwt');
          expect(jsonDecode(request.body), {'deliveryId': 7, 'notes': ?notes});
          return http.Response(jsonEncode(inspection), 201);
        });
        expect(
          (await api.startInspection(
            deliveryId: 7,
            notes: notes,
          ))['inspectorUserId'],
          42,
        );
      },
    );
  }

  test('inspection read uses shared JWT', () async {
    final api = service((request) async {
      expect(request.url.path, '/api/inspections/12');
      expect(request.headers['Authorization'], 'Bearer test-jwt');
      return http.Response(jsonEncode(inspection), 200);
    });
    expect((await api.getInspection(12))['id'], 12);
  });

  test(
    'agent analysis and workflow retrieval use API JWT without caller identity',
    () async {
      final api = service((request) async {
        expect(request.headers['Authorization'], 'Bearer test-jwt');
        if (request.method == 'POST') {
          expect(
            request.url.path,
            '/api/quality-risk-agent/inspections/12/analyse',
          );
          expect(request.body, isEmpty);
        } else {
          expect(request.url.path, '/api/quality-risk-agent/workflows/20');
        }
        return http.Response(jsonEncode(workflow), 200);
      });
      expect((await api.analyseInspection(12))['workflowId'], 20);
      expect((await api.getQualityWorkflow(20))['status'], 'Completed');
    },
  );

  for (final code in [401, 403]) {
    test(
      'HTTP $code has explicit access error for all quality operations',
      () async {
        final api = service((_) async => http.Response('', code));
        for (final operation in <Future<dynamic> Function()>[
          api.getPendingDeliveries,
          () => api.startInspection(deliveryId: 7),
          () => api.getInspection(12),
          () => api.analyseInspection(12),
          () => api.getQualityWorkflow(20),
        ]) {
          await expectLater(
            operation(),
            throwsA(
              isA<QualityApiException>()
                  .having((e) => e.statusCode, 'status', code)
                  .having(
                    (e) => e.message,
                    'message',
                    contains(code == 401 ? 'sign in again' : 'permission'),
                  ),
            ),
          );
        }
      },
    );
  }

  test('JWT is read on each request and logout removes it', () async {
    final seen = <String?>[];
    final api = service((r) async {
      seen.add(r.headers['Authorization']);
      return http.Response('[]', 200);
    });
    await api.getPendingDeliveries();
    stored['buildwise.jwt'] = 'new-token';
    await api.getPendingDeliveries();
    final authClient = ApiClient();
    addTearDown(authClient.close);
    await AuthService(apiClient: authClient).logout();
    await api.getPendingDeliveries();
    expect(seen, ['Bearer test-jwt', 'Bearer new-token', null]);
  });

  test('shared login session is used by quality service', () async {
    stored.clear();
    final transport = MockClient((request) async {
      if (request.url.path == '/api/auth/login') {
        return http.Response('{"token":"login-token","user":{"id":42}}', 200);
      }
      expect(request.headers['Authorization'], 'Bearer login-token');
      return http.Response('[]', 200);
    });
    addTearDown(transport.close);
    final shared = ApiClient(client: transport);
    await AuthService(apiClient: shared)
        .login('inspector@example.test', 'test-only');
    expect(
      await QualityApiService(apiClient: shared).getPendingDeliveries(),
      isEmpty,
    );
  });

  for (final body in [
    'invalid json',
    '{}',
    '[null]',
    '[{"deliveryId":1,"status":true,"items":[]}]',
    '[{"deliveryId":1,"status":4,"items":[{"deliveryItemId":2,"purchaseOrderItemId":3,"receivedQuantity":"10"}]}]',
  ]) {
    test('malformed pending response is an error: $body', () async {
      await expectLater(
        service((_) async => http.Response(body, 200)).getPendingDeliveries(),
        throwsA(
          isA<QualityApiException>().having(
            (e) => e.message,
            'message',
            contains('invalid response'),
          ),
        ),
      );
    });
  }

  test('invalid creation and workflow responses are rejected', () async {
    await expectLater(
      service((_) async => http.Response('{}', 201))
          .startInspection(deliveryId: 7),
      throwsA(isA<QualityApiException>()),
    );
    await expectLater(
      service((_) async => http.Response('[]', 200)).getQualityWorkflow(1),
      throwsA(isA<QualityApiException>()),
    );
  });

  test(
    'ProblemDetails and failed-workflow response retain error information',
    () async {
      await expectLater(
        service(
          (_) async => http.Response('{"detail":"Delivery not ready."}', 409),
        ).startInspection(deliveryId: 7),
        throwsA(
          isA<QualityApiException>().having(
            (e) => e.message,
            'message',
            'Delivery not ready.',
          ),
        ),
      );
      await expectLater(
        service(
          (_) async => http.Response(
            '{"workflowId":20,"status":"Failed","finalOutcome":"Analysis failed."}',
            502,
          ),
        ).analyseInspection(12),
        throwsA(
          isA<QualityApiException>().having(
            (e) => e.message,
            'message',
            'Analysis failed.',
          ),
        ),
      );
    },
  );

  test('non-JSON errors do not expose raw response', () async {
    await expectLater(
      service((_) async => http.Response('<html>proxy</html>', 502))
          .getPendingDeliveries(),
      throwsA(
        isA<QualityApiException>().having(
          (e) => e.message,
          'message',
          'Quality request failed (HTTP 502).',
        ),
      ),
    );
  });

  test('network and timeout failures have readable messages', () async {
    await expectLater(
      service((_) async => throw http.ClientException('network'))
          .getPendingDeliveries(),
      throwsA(
        isA<QualityApiException>().having(
          (e) => e.message,
          'message',
          contains('Unable to connect'),
        ),
      ),
    );
    await expectLater(
      service((_) async => throw TimeoutException('timeout'))
          .getPendingDeliveries(),
      throwsA(
        isA<QualityApiException>().having(
          (e) => e.message,
          'message',
          contains('timed out'),
        ),
      ),
    );
  });
}
