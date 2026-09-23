import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../../../core/api/api_client.dart';
import '../models/pending_inspection_delivery.dart';

class QualityApiException implements Exception {
  const QualityApiException(this.message, {this.statusCode});
  final String message;
  final int? statusCode;
  @override
  String toString() => message;
}

class QualityApiService {
  QualityApiService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient(),
      _ownsClient = apiClient == null;
  final ApiClient _apiClient;
  final bool _ownsClient;

  Future<List<PendingInspectionDelivery>> getPendingDeliveries() => _request(
    () => _apiClient.get('/inspections/pending-deliveries'),
    200,
    (json) {
      if (json is! List<dynamic>) throw const FormatException();
      return List.unmodifiable(
        json.map((delivery) {
          if (delivery is! Map<String, dynamic>) throw const FormatException();
          return PendingInspectionDelivery.fromJson(delivery);
        }),
      );
    },
  );

  /// StartInspectionDto has only deliveryId and optional notes. The backend
  /// derives the inspector from the shared client's JWT, never from the body.
  Future<Map<String, dynamic>> startInspection({
    required int deliveryId,
    String? notes,
  }) => _request(
    () => _apiClient.post(
      '/inspections',
      body: {'deliveryId': deliveryId, 'notes': ?notes},
    ),
    201,
    _inspection,
  );

  Future<Map<String, dynamic>> getInspection(int id) =>
      _request(() => _apiClient.get('/inspections/$id'), 200, _inspection);

  Future<Map<String, dynamic>> analyseInspection(int inspectionId) => _request(
    () => _apiClient.post(
      '/quality-risk-agent/inspections/$inspectionId/analyse',
      timeout: const Duration(seconds: 120),
    ),
    200,
    _workflow,
  );

  Future<Map<String, dynamic>> getQualityWorkflow(int workflowId) => _request(
    () => _apiClient.get('/quality-risk-agent/workflows/$workflowId'),
    200,
    _workflow,
  );

  Map<String, dynamic> _inspection(dynamic json) {
    if (json is! Map<String, dynamic> ||
        json['id'] is! int ||
        json['deliveryId'] is! int ||
        json['inspectorUserId'] is! int ||
        (json['status'] is! String && json['status'] is! int) ||
        json['items'] is! List) {
      throw const FormatException();
    }
    return json;
  }

  Map<String, dynamic> _workflow(dynamic json) {
    if (json is! Map<String, dynamic> ||
        json['workflowId'] is! int ||
        json['inspectionId'] is! int ||
        json['status'] is! String ||
        json['steps'] is! List) {
      throw const FormatException();
    }
    return json;
  }

  Future<T> _request<T>(
    Future<http.Response> Function() send,
    int successStatus,
    T Function(dynamic) parse,
  ) async {
    try {
      final response = await send();
      if (response.statusCode != successStatus) {
        throw QualityApiException(
          _errorMessage(response),
          statusCode: response.statusCode,
        );
      }
      return parse(jsonDecode(utf8.decode(response.bodyBytes)));
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
    if (response.statusCode == 401) {
      return 'Your session has expired or you are not signed in. Please sign in again.';
    }
    if (response.statusCode == 403) {
      return 'You do not have permission to access Quality Inspection. A Quality Inspector or Administrator account is required.';
    }
    try {
      final problem = jsonDecode(utf8.decode(response.bodyBytes));
      if (problem is Map<String, dynamic>) {
        // Analysis failures return a workflow response with HTTP 502.
        for (final field in ['detail', 'title', 'finalOutcome']) {
          final message = problem[field];
          if (message is String && message.trim().isNotEmpty) return message;
        }
      }
    } on FormatException {
      // Never display raw proxy/server response bodies.
    }
    return 'Quality request failed (HTTP ${response.statusCode}).';
  }

  void close() {
    if (_ownsClient) _apiClient.close();
  }
}
