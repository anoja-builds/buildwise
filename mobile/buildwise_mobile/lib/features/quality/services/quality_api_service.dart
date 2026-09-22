import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../../../core/api/api_config.dart';
import '../models/pending_inspection_delivery.dart';

class QualityApiException implements Exception {
  const QualityApiException(this.message, {this.statusCode});

  final String message;
  final int? statusCode;

  @override
  String toString() => message;
}

class QualityApiService {
  QualityApiService({
    String baseUrl = ApiConfig.baseUrl,
    http.Client? client,
    this.timeout = const Duration(seconds: 20),
  }) : _baseUrl = ApiConfig.parseBaseUrl(baseUrl),
       _client = client ?? http.Client(),
       _ownsClient = client == null;

  final Uri _baseUrl;
  final http.Client _client;
  final bool _ownsClient;
  final Duration timeout;

  Future<List<PendingInspectionDelivery>> getPendingDeliveries() async {
    try {
      final response = await _client
          .get(
            _baseUrl.resolve('inspections/pending-deliveries'),
            headers: {'Accept': 'application/json'},
          )
          .timeout(timeout);

      if (response.statusCode != 200) {
        throw QualityApiException(
          _errorMessage(response),
          statusCode: response.statusCode,
        );
      }

      final json = jsonDecode(utf8.decode(response.bodyBytes));
      if (json is! List<dynamic>) {
        throw const FormatException('Expected a delivery array.');
      }
      return List.unmodifiable(
        json.map((delivery) {
          if (delivery is! Map<String, dynamic>) {
            throw const FormatException('Invalid delivery.');
          }
          return PendingInspectionDelivery.fromJson(delivery);
        }),
      );
    } on TimeoutException {
      throw const QualityApiException(
        'The request timed out. Please try again.',
      );
    } on http.ClientException {
      throw const QualityApiException(
        'Unable to connect to the BuildWise API.',
      );
    } on FormatException {
      throw const QualityApiException(
        'The BuildWise API returned an invalid response.',
      );
    }
  }

  String _errorMessage(http.Response response) {
    // ASP.NET controllers return ProblemDetails. Never display raw HTML or
    // unexpected response bodies (for example, a proxy error page).
    try {
      final problem = jsonDecode(utf8.decode(response.bodyBytes));
      if (problem is Map<String, dynamic>) {
        for (final field in ['detail', 'title']) {
          final message = problem[field];
          if (message is String && message.trim().isNotEmpty) return message;
        }
      }
    } on FormatException {
      // Non-JSON errors use the safe fallback below.
    }
    return 'Unable to load pending deliveries (HTTP ${response.statusCode}).';
  }

  /// The owner of an injected client remains responsible for closing it.
  void close() {
    if (_ownsClient) _client.close();
  }
}
