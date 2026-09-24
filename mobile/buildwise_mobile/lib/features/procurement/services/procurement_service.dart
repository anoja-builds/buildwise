import 'dart:convert';
import 'package:http/http.dart' as http;

class ProcurementService {
  static const String baseUrl = 'http://localhost:5078/api/procurement';

  Future<List<dynamic>> getPurchaseOrders() async {
    final response = await http.get(Uri.parse('$baseUrl/purchase-orders'));
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load purchase orders');
    }
  }
}
