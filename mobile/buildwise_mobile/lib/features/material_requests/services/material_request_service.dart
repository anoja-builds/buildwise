import 'dart:convert';
import 'package:http/http.dart' as http;

class MaterialRequestService {
  static const String baseUrl = 'http://localhost:5078/api/materialrequests';

  Future<List<dynamic>> getRequests() async {
    final response = await http.get(Uri.parse(baseUrl));
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load material requests');
    }
  }

  Future<void> createRequest(Map<String, dynamic> data) async {
    final response = await http.post(
      Uri.parse(baseUrl),
      headers: {'Content-Type': 'application/json'},
      body: json.encode(data),
    );
    if (response.statusCode != 201 && response.statusCode != 200) {
      throw Exception('Failed to create material request');
    }
  }
}
