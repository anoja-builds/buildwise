import 'package:flutter/material.dart';
import '../services/procurement_service.dart';

class PoListScreen extends StatefulWidget {
  const PoListScreen({super.key});

  @override
  State<PoListScreen> createState() => _PoListScreenState();
}

class _PoListScreenState extends State<PoListScreen> {
  final _service = ProcurementService();
  late Future<List<dynamic>> _futurePos;

  @override
  void initState() {
    super.initState();
    _futurePos = _service.getPurchaseOrders();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Issued Purchase Orders'),
      ),
      body: FutureBuilder<List<dynamic>>(
        future: _futurePos,
        builder: (context, snapshot) {
          if (snapshot.connectionState == ConnectionState.waiting) {
            return const Center(child: CircularProgressIndicator());
          } else if (snapshot.hasError) {
            return Center(child: Text('Error: ${snapshot.error}'));
          } else if (!snapshot.hasData || snapshot.data!.isEmpty) {
            return const Center(child: Text('No purchase orders found.'));
          }

          final pos = snapshot.data!;
          return ListView.builder(
            itemCount: pos.length,
            padding: const EdgeInsets.all(12),
            itemBuilder: (context, index) {
              final po = pos[index];
              return Card(
                elevation: 2,
                margin: const EdgeInsets.only(bottom: 12),
                child: ListTile(
                  leading: const CircleAvatar(
                    backgroundColor: Colors.tealAccent,
                    child: Icon(Icons.shopping_cart, color: Colors.teal),
                  ),
                  title: Text(
                    'PO #${po['id'].toString().padLeft(4, '0')} - ${po['supplierName'] ?? 'Supplier'}',
                    style: const TextStyle(fontWeight: FontWeight.bold),
                  ),
                  subtitle: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const SizedBox(height: 4),
                      Text('Project: ${po['projectName'] ?? 'Site'}'),
                      Text('Status: ${po['status']}', style: const TextStyle(fontWeight: FontWeight.bold, color: Colors.teal)),
                    ],
                  ),
                  trailing: Text(
                    'LKR ${(po['totalAmount'] as num).toStringAsFixed(0)}',
                    style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 13),
                  ),
                ),
              );
            },
          );
        },
      ),
    );
  }
}
