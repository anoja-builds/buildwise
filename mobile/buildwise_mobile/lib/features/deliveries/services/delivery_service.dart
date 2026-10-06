import 'dart:convert';
import 'package:http/http.dart' as http;

class DeliveryService {
  static const String baseUrl = 'http://localhost:5078/api/deliveries';

  Future<List<dynamic>> getExpectedDeliveries() async {
    final response = await http.get(Uri.parse('$baseUrl/expected'));
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load expected deliveries');
    }
  }

  Future<void> receiveDelivery(int id, Map<String, dynamic> data) async {
    final response = await http.post(
      Uri.parse('$baseUrl/$id/receive'),
      headers: {'Content-Type': 'application/json'},
      body: json.encode(data),
    );
    if (response.statusCode != 200) {
      throw Exception('Failed to reconcile delivery');
    }
  }

  Future<void> uploadEvidence(int id, String imageUrl) async {
    final response = await http.post(
      Uri.parse('$baseUrl/$id/evidence'),
      headers: {'Content-Type': 'application/json'},
      body: json.encode({'imageUrl': imageUrl}),
    );
    if (response.statusCode != 200) {
      throw Exception('Failed to upload evidence');
    }
  }
}
