import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:buildwise_mobile/features/auth/screens/login_screen.dart';
import 'package:buildwise_mobile/main.dart';

/// `flutter_secure_storage` has no platform implementation registered under
/// `flutter test`, so without a stub `AuthGate`'s `isSignedIn()` probe never
/// resolves and the loading spinner (an infinite animation) makes
/// `pumpAndSettle` time out. Answer the plugin's method channel directly.
const _secureStorageChannel = MethodChannel(
  'plugins.it_nomads.com/flutter_secure_storage',
);

void main() {
  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(_secureStorageChannel, (call) async {
          switch (call.method) {
            case 'read':
              return null; // no JWT stored -> signed out
            case 'readAll':
              return <String, String>{};
            case 'containsKey':
              return false;
            case 'isProtectedDataAvailable':
              return true;
            default:
              return null; // write / delete / deleteAll
          }
        });
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(_secureStorageChannel, null);
  });

  testWidgets('BuildWise shows the sign-in screen when signed out', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(const BuildWiseApp());
    await tester.pumpAndSettle();

    // The auth probe resolved to "signed out": spinner gone, login shown.
    expect(find.byType(CircularProgressIndicator), findsNothing);
    expect(find.byType(LoginScreen), findsOneWidget);
    expect(find.text('BuildWise'), findsOneWidget);
    // Found twice: the sign-in card heading and the submit button label.
    expect(find.text('Sign in'), findsNWidgets(2));
    expect(find.text('Quick demo login'), findsOneWidget);
  });
}