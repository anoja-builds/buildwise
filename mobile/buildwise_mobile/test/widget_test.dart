import 'package:flutter_test/flutter_test.dart';
import 'package:buildwise_mobile/main.dart';

void main() {
  testWidgets('BuildWise common UI loads', (WidgetTester tester) async {
    await tester.pumpWidget(const BuildWiseApp());

    expect(find.text('BuildWise'), findsOneWidget);
    expect(find.text('Good morning, Jordan'), findsOneWidget);
  });
}
