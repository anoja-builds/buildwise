import 'dart:convert';

import 'package:http/http.dart' as http;

class ApiException implements Exception {
  const ApiException(this.message, {this.statusCode});
  final String message;
  final int? statusCode;
  @override
  String toString() => message;

  static void check(
    http.Response response, {
    List<int> accepted = const [200],
  }) {
    if (accepted.contains(response.statusCode)) return;
    if (response.statusCode == 401) {
      throw const ApiException(
        'Your session has expired. Please sign in again.',
        statusCode: 401,
      );
    }
    if (response.statusCode == 403) {
      throw const ApiException(
        'You do not have permission to access this content.',
        statusCode: 403,
      );
    }
    var message =
        'Unable to complete this request (HTTP ${response.statusCode}).';
    try {
      final body = jsonDecode(utf8.decode(response.bodyBytes));
      if (body is Map<String, dynamic>) {
        final errors = body['errors'];
        if (errors is Map) {
          message = errors.values.expand((e) => e is List ? e : [e]).join('\n');
        } else {
          message =
              (body['error'] ?? body['detail'] ?? body['title'] ?? message)
                  .toString();
        }
      } else if (body is String && response.statusCode < 500) {
        message = body;
      }
    } on FormatException {
      if (response.statusCode < 500 &&
          !response.body.contains('<') &&
          response.body.length < 500) {
        message = response.body.isEmpty ? message : response.body;
      }
    }
    throw ApiException(message, statusCode: response.statusCode);
  }
}
