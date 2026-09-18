import 'package:flutter_test/flutter_test.dart';
import 'package:buildwise_mobile/main.dart';

void main() {
  testWidgets('BuildWise shows the sign-in screen when signed out', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(const BuildWiseApp());
    // AuthGate's isSignedIn() check resolves asynchronously (and errors out
    // to "not signed in" in a widget test with no real secure-storage
    // platform channel available) — settle before asserting.
    await tester.pumpAndSettle();

    expect(find.text('BuildWise'), findsOneWidget);
    expect(find.text('Sign in'), findsOneWidget);
    expect(find.text('Quick demo login'), findsOneWidget);
  });
}
