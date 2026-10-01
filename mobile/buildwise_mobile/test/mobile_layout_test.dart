import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:buildwise_mobile/core/theme/app_theme.dart';
import 'package:buildwise_mobile/features/auth/screens/login_screen.dart';
import 'package:buildwise_mobile/features/material_requests/screens/material_request_list_screen.dart';
import 'package:buildwise_mobile/features/material_requests/screens/create_material_request_screen.dart';
import 'package:buildwise_mobile/features/material_requests/services/material_request_service.dart';
import 'package:buildwise_mobile/features/deliveries/screens/delivery_list_screen.dart';
import 'package:buildwise_mobile/features/deliveries/screens/receive_delivery_screen.dart';
import 'package:buildwise_mobile/features/deliveries/services/delivery_service.dart';
import 'package:buildwise_mobile/features/quality/screens/pending_inspections_screen.dart';
import 'package:buildwise_mobile/features/quality/services/quality_api_service.dart';
import 'package:buildwise_mobile/features/procurement/screens/procurement_home_screen.dart';
import 'package:buildwise_mobile/features/procurement/services/procurement_service.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const export = bool.fromEnvironment('EXPORT_VISUAL_REVIEW');
  setUpAll(() async {
    final font = FontLoader('Geist')
      ..addFont(rootBundle.load('assets/fonts/Geist.ttf'));
    await font.load();
    final icons = FontLoader('MaterialIcons')
      ..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));
    await icons.load();
  });
  setUp(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
          (_) async => null,
        ),
  );
  tearDown(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
          null,
        ),
  );
  final delivery = {
    'id': 321,
    'deliveryReference': 'DEL-0321',
    'purchaseOrderId': 203,
    'supplierName': 'Test supplier',
    'projectName': 'Test site',
    'status': 'Scheduled',
    'expectedDate': '2026-10-03',
    'items': [
      {
        'id': 456,
        'purchaseOrderItemId': 77,
        'materialName': 'Test construction cement',
        'materialUnit': 'bags',
        'orderedQuantity': 250,
        'outstandingQuantity': 250,
      },
    ],
  };
  for (final width in [360.0, 390.0, 412.0]) {
    for (final name in [
      'login',
      'requests',
      'create-request',
      'deliveries',
      'receive',
      'quality',
      'procurement',
    ]) {
      testWidgets('$name renders without overflow at $width', (tester) async {
        tester.view.physicalSize = Size(width, 844);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final client = MockClient((request) async {
          final path = request.url.path;
          final Object data;
          if (path.endsWith('/options')) {
            data = {
              'projects': [
                {'id': 62, 'name': 'Test site'},
              ],
              'materials': [
                {'id': 63, 'name': 'Test cement', 'unit': 'bags'},
              ],
            };
          } else if (path.endsWith('/materialrequests')) {
            data = [
              {
                'id': 731,
                'projectName': 'Test site',
                'reason': 'Foundation work',
                'status': 'Approved',
                'quotationCount': 0,
                'itemsCount': 1,
                'requiredDate': '2026-10-05',
              },
            ];
          } else if (path.endsWith('/purchase-orders')) {
            data = {'items': [], 'total': 0};
          } else if (path.endsWith('/procurement-workflow')) {
            return http.Response('', 204);
          } else if (path.endsWith('/pending-deliveries')) {
            data = [
              {
                'deliveryId': 321,
                'deliveryReference': 'DEL-0321',
                'status': 'Received',
                'items': [
                  {
                    'deliveryItemId': 456,
                    'purchaseOrderItemId': 77,
                    'receivedQuantity': 250,
                    'damagedQuantity': 0,
                  },
                ],
              },
            ];
          } else {
            data = [delivery];
          }
          return http.Response(jsonEncode(data), 200);
        });
        addTearDown(client.close);
        final api = ApiClient(client: client);
        final requests = MaterialRequestService(apiClient: api);
        final deliveries = DeliveryService(apiClient: api);
        final screen = switch (name) {
          'login' => LoginScreen(onSignedIn: () {}),
          'requests' => MaterialRequestListScreen(service: requests),
          'create-request' => CreateMaterialRequestScreen(service: requests),
          'deliveries' => DeliveryListScreen(service: deliveries),
          'receive' => ReceiveDeliveryScreen(
            delivery: delivery,
            service: deliveries,
          ),
          'quality' => PendingInspectionsScreen(
            service: QualityApiService(apiClient: api),
          ),
          _ => ProcurementHomeScreen(
            service: ProcurementService(apiClient: api),
          ),
        };
        final key = GlobalKey();
        await tester.pumpWidget(
          RepaintBoundary(
            key: key,
            child: MaterialApp(
              debugShowCheckedModeBanner: false,
              theme: AppTheme.light,
              home: screen,
            ),
          ),
        );
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull);
        if (export && width == 390) {
          await tester.runAsync(() async {
            final boundary =
                key.currentContext!.findRenderObject()!
                    as RenderRepaintBoundary;
            final image = await boundary.toImage();
            final bytes = await image.toByteData(
              format: ui.ImageByteFormat.png,
            );
            final folder = Directory('build/visual-review')
              ..createSync(recursive: true);
            File('${folder.path}/$name.png')
                .writeAsBytesSync(bytes!.buffer.asUint8List());
            image.dispose();
          });
        }
      });
    }
  }
}
