import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:buildwise_mobile/main.dart';
import 'package:buildwise_mobile/features/deliveries/screens/delivery_list_screen.dart';
import 'package:buildwise_mobile/features/material_requests/screens/material_request_list_screen.dart';

const storage = MethodChannel('plugins.it_nomads.com/flutter_secure_storage');

void main() {
  final cases = <String, List<String>>{
    'SiteEngineer': ['Requests', 'Deliveries'],
    'QualityInspector': ['Deliveries', 'Quality'],
    'ProcurementOfficer': [],
    'ProcurementManager': [],
    'Administrator': ['Requests', 'Orders', 'Deliveries', 'Quality'],
    'ProjectManager': [],
    'ReceivingOfficer': [],
    'Unknown': [],
  };

  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storage, (call) async => null);
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storage, null);
  });

  for (final entry in cases.entries) {
    testWidgets(
      '${entry.key} restores only permitted tabs from the API session',
      (tester) async {
        TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
            .setMockMethodCallHandler(storage, (call) async {
              if (call.method == 'read') {
                return call.arguments['key'] == 'buildwise.jwt'
                    ? 'test-token'
                    : jsonEncode({
                        'roles': [entry.key],
                      });
              }
              return null;
            });
        await tester.pumpWidget(const BuildWiseApp());
        await tester.pumpAndSettle();
        final labels = tester
            .widgetList<NavigationDestination>(
              find.byType(NavigationDestination),
            )
            .map((destination) => destination.label)
            .toList();
        expect(labels, entry.value);
        if (entry.value.isEmpty) {
          if (['ProcurementOfficer', 'ProcurementManager'].contains(entry.key)) {
            expect(
              find.text(
                'Procurement management workflows are available in the BuildWise Web Portal.',
              ),
              findsOneWidget,
            );
          } else {
            expect(
              find.text('Your account has no assigned workspace.'),
              findsOneWidget,
            );
          }
        } else {
          if (entry.value.first == 'Requests') {
            final requests = tester.widget<MaterialRequestListScreen>(
              find.byType(MaterialRequestListScreen),
            );
            expect(
              requests.canCreate,
              ['SiteEngineer', 'Administrator'].contains(entry.key),
            );
            expect(requests.canViewProcurementStatus, requests.canCreate);
          }
          await tester.tap(find.text('Deliveries'));
          await tester.pumpAndSettle();
          final deliveries = tester.widget<DeliveryListScreen>(
            find.byType(DeliveryListScreen),
          );
          expect(
            deliveries.canReceive,
            ['SiteEngineer', 'Administrator'].contains(entry.key),
          );
        }
        // Unselected modules are not mounted and do not issue background API calls.
        expect(find.byType(MaterialRequestListScreen), findsNothing);
        await tester.tap(find.byTooltip('Sign out'));
        await tester.pumpAndSettle();
        expect(find.text('Sign in'), findsWidgets);
      },
    );
  }

  testWidgets('multiple roles union permitted modules', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: MainAppShell(
          roles: const ['SiteEngineer', 'QualityInspector'],
          onSignOut: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(
      tester
          .widgetList<NavigationDestination>(find.byType(NavigationDestination))
          .map((d) => d.label)
          .toList(),
      ['Requests', 'Deliveries', 'Quality'],
    );
  });
}
