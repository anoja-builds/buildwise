import 'package:flutter/material.dart';
import '../services/delivery_service.dart';
import 'receive_delivery_screen.dart';

class DeliveryListScreen extends StatefulWidget {
  const DeliveryListScreen({super.key});

  @override
  State<DeliveryListScreen> createState() => _DeliveryListScreenState();
}

class _DeliveryListScreenState extends State<DeliveryListScreen> {
  final DeliveryService _service = DeliveryService();
  late Future<List<dynamic>> _deliveries;

  @override
  void initState() {
    super.initState();
    _deliveries = _service.getExpectedDeliveries();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Incoming Deliveries')),
      body: FutureBuilder<List<dynamic>>(
        future: _deliveries,
        builder: (context, snapshot) {
          if (snapshot.connectionState == ConnectionState.waiting) {
            return const Center(child: CircularProgressIndicator());
          } else if (snapshot.hasError) {
            return Center(child: Text('Error: ${snapshot.error}'));
          } else if (!snapshot.hasData || snapshot.data!.isEmpty) {
            return const Center(child: Text('No pending deliveries.'));
          }

          final deliveries = snapshot.data!;
          return ListView.builder(
            itemCount: deliveries.length,
            itemBuilder: (context, index) {
              final delivery = deliveries[index];
              return Card(
                margin: const EdgeInsets.all(8),
                child: ListTile(
                  title: Text(delivery['deliveryReference'] ?? 'No Ref'),
                  subtitle: Text('${delivery['supplierName']} - PO #${delivery['purchaseOrderId']}'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () {
                    Navigator.push(
                      context,
                      MaterialPageRoute(
                        builder: (context) => ReceiveDeliveryScreen(delivery: delivery),
                      ),
                    ).then((_) => setState(() {
                      _deliveries = _service.getExpectedDeliveries();
                    }));
                  },
                ),
              );
            },
          );
        },
      ),
    );
  }
}
