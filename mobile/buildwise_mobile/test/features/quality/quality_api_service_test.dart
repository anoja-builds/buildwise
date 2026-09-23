import 'dart:async';
import 'dart:convert';

import 'package:buildwise_mobile/core/api/api_config.dart';
import 'package:buildwise_mobile/features/quality/services/quality_api_service.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  QualityApiService serviceWith(
    MockClient client, {
    Duration timeout = const Duration(seconds: 20),
  }) {
    addTearDown(client.close);
    return QualityApiService(
      baseUrl: 'https://buildwise.example/api',
      client: client,
      timeout: timeout,
    );
  }

  test(
    'GET uses API path and maps backend numeric status and quantities',
    () async {
      final service = serviceWith(
        MockClient((request) async {
          expect(request.method, 'GET');
          expect(
            request.url.toString(),
            'https://buildwise.example/api/inspections/pending-deliveries',
          );
          expect(request.headers['Accept'], 'application/json');
          return http.Response(
            jsonEncode([
              {
                'deliveryId': 7,
                'deliveryReference': 'Réception-7',
                'status': 4,
                'items': [
                  {
                    'deliveryItemId': 9,
                    'purchaseOrderItemId': 2,
                    'receivedQuantity': 10,
                  },
                  {
                    'deliveryItemId': 10,
                    'purchaseOrderItemId': 3,
                    'receivedQuantity': 2.25,
                  },
                ],
              },
              {
                'deliveryId': 8,
                'deliveryReference': null,
                'status': 5,
                'items': [],
              },
            ]),
            200,
            headers: {'content-type': 'application/json; charset=utf-8'},
          );
        }),
      );
      final result = await service.getPendingDeliveries();
      expect(result.first.deliveryId, 7);
      expect(result.first.deliveryReference, 'Réception-7');
      expect(result.first.status, 4);
      expect(result.first.items.first.deliveryItemId, 9);
      expect(result.first.items.first.purchaseOrderItemId, 2);
      expect(result.first.items.map((item) => item.receivedQuantity), [
        10.0,
        2.25,
      ]);
      expect(result.last.deliveryReference, isNull);
    },
  );

  test('empty array is a successful empty result', () async {
    final service = serviceWith(
      MockClient((_) async => http.Response('[]', 200)),
    );
    expect(await service.getPendingDeliveries(), isEmpty);
  });

  test('ProblemDetails keeps the HTTP status and backend message', () async {
    final service = serviceWith(
      MockClient(
        (_) async => http.Response(
          '{"detail":"Unable to process the quality inspection request."}',
          500,
        ),
      ),
    );
    await expectLater(
      service.getPendingDeliveries(),
      throwsA(
        isA<QualityApiException>()
            .having((error) => error.statusCode, 'status', 500)
            .having(
              (error) => error.message,
              'message',
              'Unable to process the quality inspection request.',
            ),
      ),
    );
  });

  test('non-JSON HTTP error does not expose its raw body', () async {
    final service = serviceWith(
      MockClient((_) async => http.Response('<html>proxy</html>', 502)),
    );
    await expectLater(
      service.getPendingDeliveries(),
      throwsA(
        isA<QualityApiException>().having(
          (error) => error.message,
          'message',
          'Unable to load pending deliveries (HTTP 502).',
        ),
      ),
    );
  });

  for (final body in [
    'not json',
    '{}',
    '[null]',
    '[{"deliveryId":1,"status":true,"items":[]}]',
    '[{"deliveryId":1,"status":4,"items":[{"deliveryItemId":2,"purchaseOrderItemId":3,"receivedQuantity":"10"}]}]',
  ]) {
    test('rejects malformed API response: $body', () async {
      final service = serviceWith(
        MockClient((_) async => http.Response(body, 200)),
      );
      await expectLater(
        service.getPendingDeliveries(),
        throwsA(
          isA<QualityApiException>().having(
            (error) => error.message,
            'message',
            'The BuildWise API returned an invalid response.',
          ),
        ),
      );
    });
  }

  test('connection failure becomes a readable error', () async {
    final service = serviceWith(
      MockClient((_) async => throw http.ClientException('network')),
    );
    await expectLater(
      service.getPendingDeliveries(),
      throwsA(
        isA<QualityApiException>().having(
          (error) => error.message,
          'message',
          'Unable to connect to the BuildWise API.',
        ),
      ),
    );
  });

  test('slow request times out', () async {
    final pending = Completer<http.Response>();
    final service = serviceWith(
      MockClient((_) => pending.future),
      timeout: const Duration(milliseconds: 1),
    );
    await expectLater(
      service.getPendingDeliveries(),
      throwsA(
        isA<QualityApiException>().having(
          (error) => error.message,
          'message',
          'The request timed out. Please try again.',
        ),
      ),
    );
    pending.complete(http.Response('[]', 200));
  });

  test('base URL rejects absent or invalid configuration', () {
    for (final value in [
      '',
      'localhost:5000',
      'ftp://host/api/',
      'https://host/api/?key=value',
    ]) {
      expect(() => ApiConfig.parseBaseUrl(value), throwsArgumentError);
    }
    expect(
      ApiConfig.parseBaseUrl('https://host/api/').toString(),
      'https://host/api/',
    );
  });
}
