// Opt-in real HTTP verification against the isolated ProcurementIntegrationTests host.
// Only the device secure-storage plugin is replaced; all API calls are real.
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/io_client.dart';
import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:buildwise_mobile/features/material_requests/services/material_request_service.dart';
import 'package:buildwise_mobile/features/material_requests/screens/create_material_request_screen.dart';
import 'package:buildwise_mobile/features/procurement/services/procurement_status_service.dart';
import 'package:buildwise_mobile/features/procurement/screens/material_request_procurement_view.dart';

class _RealHttpOverrides extends HttpOverrides {}

void main() {
  const stage = String.fromEnvironment('BUILDWISE_LIVE_STAGE');
  const directory = String.fromEnvironment('BUILDWISE_LIVE_DIR');
  TestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('live Flutter material request and procurement status: $stage', (
    tester,
  ) async {
    final values = <String, String>{};
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
          (call) async {
            final key = call.arguments['key'] as String;
            if (call.method == 'write') {
              values[key] = call.arguments['value'] as String;
            }
            return call.method == 'read' ? values[key] : null;
          },
        );
    final http = HttpOverrides.runWithHttpOverrides(
      () => IOClient(HttpClient()),
      _RealHttpOverrides(),
    );
    addTearDown(http.close);
    final api = ApiClient(client: http);
    await tester.runAsync(() async {
      final response = await api.post(
        '/auth/login',
        body: {
          'email': 'site.engineer@buildwise.demo',
          'password': 'Passw0rd!',
        },
      );
      expect(response.statusCode, 200);
      final session = jsonDecode(response.body) as Map<String, dynamic>;
      await api.saveSession(
        session['token'] as String,
        session['user'] as Map<String, dynamic>,
      );
    });
    Future<void> settleNetwork() async {
      await tester.runAsync(() async {
        await tester.pump();
        await Future<void>.delayed(const Duration(milliseconds: 700));
      });
      await tester.pumpAndSettle();
    }

    final service = MaterialRequestService(apiClient: api);
    if (stage == 'create') {
      Map<String, dynamic>? options;
      await tester.runAsync(() async {
        options = await service.getOptions();
      });
      final project =
          (options!['projects'] as List).first as Map<String, dynamic>;
      final material =
          (options!['materials'] as List).first as Map<String, dynamic>;
      final reason =
          'Flutter live phase 4 ${DateTime.now().microsecondsSinceEpoch}';
      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) =>
                        CreateMaterialRequestScreen(service: service),
                  ),
                ),
                child: const Text('Create'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('Create'));
      await settleNetwork();
      expect(find.textContaining('Failed to load'), findsNothing);
      expect(
        tester
            .widget<DropdownButton<int>>(
              find.byKey(const Key('project-select')),
            )
            .items,
        isNotEmpty,
      );
      await tester.tap(find.byKey(const Key('project-select')));
      await tester.pumpAndSettle();
      await tester.tap(find.text(project['name'] as String).last);
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('material-select')));
      await tester.pumpAndSettle();
      await tester.tap(
        find.text('${material['name']} (${material['unit']})').last,
      );
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(
          TextField,
          'Required Quantity (${material['unit']})',
        ),
        '250',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Purpose / Work Reason'),
        reason,
      );
      await tester.ensureVisible(find.text('SUBMIT MATERIAL REQUEST'));
      await tester.tap(find.text('SUBMIT MATERIAL REQUEST'));
      await settleNetwork();
      await tester.runAsync(() async {
        final requests = await service.getRequests();
        final created = requests.singleWhere(
          (r) => r['reason'] == reason,
        ) as Map<String, dynamic>;
        expect(created['status'], 'PendingApproval');
        await File('$directory/request.json').writeAsString(
          jsonEncode({'requestId': created['id'], 'reason': reason}),
        );
      });
    } else {
      final request = jsonDecode(
        File('$directory/request.json').readAsStringSync(),
      ) as Map<String, dynamic>;
      final result = jsonDecode(
        File('$directory/result.json').readAsStringSync(),
      ) as Map<String, dynamic>;
      await tester.runAsync(() async {
        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(
              body: MaterialRequestProcurementView(
                materialRequestId: request['requestId'] as int,
                service: ProcurementStatusService(apiClient: api),
              ),
            ),
          ),
        );
        await Future<void>.delayed(const Duration(milliseconds: 700));
      });
      await settleNetwork();
      expect(
        find.text('Purchase Order Created (PO #${result['purchaseOrderId']})'),
        findsOneWidget,
      );
      expect(find.text('Order status: Confirmed'), findsOneWidget);
      expect(find.textContaining('Supplier A'), findsNothing);
      expect(find.textContaining('525,000'), findsNothing);
      await tester.runAsync(() async {
        await tester.tap(find.text('Refresh status'));
        await Future<void>.delayed(const Duration(milliseconds: 700));
      });
      await settleNetwork();
      expect(find.text('Order status: Confirmed'), findsOneWidget);
      File('$directory/flutter-verified.json').writeAsStringSync(
        jsonEncode({
          'requestId': request['requestId'],
          'purchaseOrderId': result['purchaseOrderId'],
          'status': 'Confirmed',
        }),
      );
    }
    http.close();
    await tester.pumpAndSettle();
  }, skip: stage.isEmpty || directory.isEmpty);
}
