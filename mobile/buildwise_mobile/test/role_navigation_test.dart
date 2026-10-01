import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:buildwise_mobile/main.dart';
import 'package:buildwise_mobile/features/deliveries/screens/delivery_list_screen.dart';
import 'package:buildwise_mobile/features/procurement/screens/procurement_home_screen.dart';

const storage = MethodChannel('plugins.it_nomads.com/flutter_secure_storage');
void main() {
  final cases = <String, List<String>>{
    'SiteEngineer': ['Requests', 'Deliveries'],
    'QualityInspector': ['Deliveries', 'Quality'],
    'ProcurementOfficer': ['Procurement', 'Suppliers', 'Orders'],
    'ProcurementManager': ['Procurement', 'Suppliers', 'Orders'],
    'Administrator': ['Requests', 'Procurement', 'Suppliers', 'Orders', 'More'],
    'ProjectManager': [],
    'ReceivingOfficer': [],
    'Unknown': [],
  };
  tearDown(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storage, null),
  );
  for (final entry in cases.entries) {
    testWidgets('${entry.key} restores only authorized destinations', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(360, 844);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(
            storage,
            (call) async => call.method == 'read'
                ? call.arguments['key'] == 'buildwise.jwt'
                      ? 'test-token'
                      : jsonEncode({
                          'roles': [entry.key],
                        })
                : null,
          );
      await tester.pumpWidget(const BuildWiseApp());
      await tester.pumpAndSettle();
      expect(
        tester
            .widgetList<NavigationDestination>(
              find.byType(NavigationDestination),
            )
            .map((d) => d.label)
            .toList(),
        entry.value,
      );
      if (entry.key.startsWith('Procurement')) {
        expect(
          tester
              .widget<ProcurementHomeScreen>(find.byType(ProcurementHomeScreen))
              .manager,
          entry.key == 'ProcurementManager',
        );
        expect(find.text('Review in Web Portal'), findsNothing);
      }
      if (entry.key == 'Administrator') {
        await tester.tap(find.text('More'));
        await tester.pumpAndSettle();
        expect(find.text('Quality'), findsOneWidget);
        await tester.tap(find.text('Deliveries'));
        await tester.pumpAndSettle();
      } else if (entry.value.contains('Deliveries')) {
        await tester.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('Deliveries'),
          ),
        );
        await tester.pumpAndSettle();
      }
      if (entry.value.contains('Deliveries') || entry.key == 'Administrator') {
        expect(
          tester
              .widget<DeliveryListScreen>(find.byType(DeliveryListScreen))
              .canReceive,
          ['SiteEngineer', 'Administrator'].contains(entry.key),
        );
      }
      if (entry.value.isEmpty) {
        expect(
          find.text('Your account has no assigned workspace.'),
          findsOneWidget,
        );
      }
      await tester.tap(find.byTooltip('Sign out'));
      await tester.pumpAndSettle();
      expect(find.text('Sign in'), findsOneWidget);
    });
  }
  testWidgets('multiple roles union authorized modules', (tester) async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storage, (_) async => null);
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
