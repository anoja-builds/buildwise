import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:buildwise_mobile/features/operations/screens/material_requests_screen.dart';
import 'package:buildwise_mobile/features/operations/services/operations_service.dart';

class MockOperationsService extends OperationsService {
  List<Map<String, dynamic>> mockRequests = [
    {
      'id': 74,
      'projectName': 'Riverside Apartments — Block C',
      'itemCount': 1,
      // Mirrors the summary DTO: the list labels each row with the material.
      'materialNames': ['Cement (50kg bag)'],
      'status': 'Approved',
    },
    {
      'id': 75,
      'projectName': 'Riverside Apartments — Block C',
      'itemCount': 1,
      'materialNames': ['Concrete Blocks'],
      'status': 'PendingApproval',
    },
  ];

  @override
  Future<List<Map<String, dynamic>>> listMyRequests() async => mockRequests;

  @override
  Future<List<Map<String, dynamic>>> listRequests({String? status}) async =>
      mockRequests;

  /// The create-request form now resolves the project and material from the
  /// API instead of submitting hard-coded ids, so the mock serves the same
  /// reference data the real `/projects` and `/materials` endpoints return.
  @override
  Future<List<Map<String, dynamic>>> listProjects() async => [
    {'id': 1, 'name': 'Riverside Apartments — Block C'},
    {'id': 2, 'name': 'Kandy Heights — Tower A'},
  ];

  @override
  Future<List<Map<String, dynamic>>> listMaterials() async => [
    {'id': 1, 'name': 'Cement (50kg bag)', 'unit': 'bags'},
    {'id': 2, 'name': 'Concrete Blocks', 'unit': 'units'},
  ];

  /// Captures the last submitted request so a test can assert on what the form
  /// actually sent, rather than only on what the screen displayed.
  Map<String, dynamic>? lastSubmitted;

  @override
  Future<Map<String, dynamic>> createRequest({
    required int projectId,
    required String requiredDate,
    required int materialId,
    required double quantity,
    required String reason,
    String priority = 'Normal',
    String? siteNotes,
    String? description,
    String? unit,
    String? itemRequiredDate,
    String? requestDate,
    String? projectName,
    String? materialName,
  }) async {
    lastSubmitted = {
      'projectId': projectId,
      'projectName': projectName,
      'materialName': materialName,
      'requiredDate': requiredDate,
      'materialId': materialId,
      'quantity': quantity,
      'reason': reason,
      'unit': unit,
      'requestDate': requestDate,
      'siteNotes': siteNotes,
      'description': description,
    };
    final newReq = {
      'id': 76,
      'projectName': 'Riverside Apartments — Block C',
      'itemCount': 1,
      'status': 'Draft',
    };
    mockRequests.add(newReq);
    return newReq;
  }
}

void main() {
  group('MaterialRequestsScreen Widget Tests', () {
    testWidgets('Renders material requests list with status chips', (
      tester,
    ) async {
      final mockService = MockOperationsService();

      await tester.pumpWidget(
        MaterialApp(
          // canApprove is off by default because the agent/approval actions sit
          // behind the API's approval policies; this test asserts the row
          // actions, so it opts into that capability.
          home: MaterialRequestsScreen(
            service: mockService,
            canApprove: true,
            autoRefresh: false,
          ),
        ),
      );

      await tester.pumpAndSettle();

      expect(find.text('Material Requests'), findsOneWidget);
      // Rows lead with the material name, not "Request #74 · 1 item(s)". The id
      // moved to its own line below it.
      expect(find.text('Cement (50kg bag)'), findsOneWidget);
      expect(find.text('Concrete Blocks'), findsOneWidget);
      expect(find.text('Request #74'), findsOneWidget);
      expect(find.text('Approved'), findsOneWidget);
      expect(find.text('Request #75'), findsOneWidget);
      expect(find.text('PendingApproval'), findsOneWidget);
      expect(find.text('Run AI Analysis'), findsNWidgets(2));
    });

    /// Opens the create sheet and resolves the reference-data dropdowns.
    Future<void> openCreateSheet(
      WidgetTester tester,
      MockOperationsService s,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          home: MaterialRequestsScreen(service: s, autoRefresh: false),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Create Request'));
      await tester.pumpAndSettle();
    }

    /// Picks an option from one of the create sheet's reference-data dropdowns.
    Future<void> pickTyped(
      WidgetTester tester,
      Key field,
      String fragment,
    ) async {
      await tester.enterText(find.byKey(field), fragment);
      await tester.pumpAndSettle();
      await tester.tap(find.text(fragment).last);
      await tester.pumpAndSettle();
    }

    Future<void> pickDropdown(
      WidgetTester tester,
      String label,
      String option,
    ) async {
      final field = Key(
        label == 'Project *' ? 'project-field' : 'material-field',
      );
      await tester.ensureVisible(find.byKey(field));
      await tester.pumpAndSettle();
      await pickTyped(tester, field, option);
    }

    /// Scrolls the sheet until [finder] is visible, then taps it. The submit
    /// button sits below the fold on a test-sized viewport, and a tap on an
    /// off-screen widget silently does nothing.
    ///
    /// [label] is matched against the button's own text rather than a bare
    /// `find.text`, because the list screen behind the sheet also renders a
    /// "Create Request" action and a `scrollUntilVisible` over several matches
    /// throws "Too many elements".
    Future<void> submitRequest(WidgetTester tester) async {
      // `scrollUntilVisible` requires exactly one match, and the sheet plus the
      // list screen behind it render several buttons. `ensureVisible` has no such
      // requirement, so scroll the sheet itself and then tap the button.
      final button = find.widgetWithText(FilledButton, 'Submit Request');
      await tester.ensureVisible(button.first);
      await tester.pumpAndSettle();
      await tester.ensureVisible(button.first);
      await tester.pumpAndSettle();
      await tester.tap(button.first);
      await tester.pumpAndSettle();
    }

    testWidgets('Create sheet resolves project and material from typed text', (
      tester,
    ) async {
      final mockService = MockOperationsService();
      await openCreateSheet(tester, mockService);

      expect(find.text('Create Material Request'), findsOneWidget);
      expect(find.byKey(const Key('project-field')), findsOneWidget);
      expect(find.byKey(const Key('material-field')), findsOneWidget);
      expect(find.text('Quantity *'), findsOneWidget);
      expect(find.text('Justification *'), findsOneWidget);
      expect(find.text('Submit Request'), findsOneWidget);
    });

    testWidgets('typing filters the suggestions to the text entered', (
      tester,
    ) async {
      final mockService = MockOperationsService();
      await openCreateSheet(tester, mockService);

      await tester.enterText(find.byKey(const Key('project-field')), 'kandy');
      await tester.pumpAndSettle();

      expect(find.text('Kandy Heights — Tower A'), findsOneWidget);
      // The other project does not match the typed text, so it is not offered.
      expect(find.text('Riverside Apartments — Block C'), findsNothing);
    });

    testWidgets('a project name that is not in the list is submitted as text', (
      tester,
    ) async {
      final mockService = MockOperationsService();
      await openCreateSheet(tester, mockService);

      // Fill the reference fields the way the other tests do, then replace the
      // project with a site BuildWise has never seen.
      await pickDropdown(tester, 'Project *', 'Kandy Heights — Tower A');
      await pickDropdown(tester, 'Material *', 'Concrete Blocks');
      await tester.enterText(
        find.widgetWithText(TextField, 'Quantity *'),
        '500',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Justification *'),
        'For masonry work',
      );

      // A new site is typed in as plain text. The form does not refuse it: the
      await tester.enterText(
        find.widgetWithText(TextField, 'Site Notes'),
        'Unload at the site entrance',
      );

      // A new site is typed in as plain text. The form does not refuse it: the
      // name goes out and the API resolves or creates the project.
      await tester.enterText(
        find.byKey(const Key('project-field')),
        'Galle Face Promenade',
      );
      await tester.pumpAndSettle();
      expect(find.textContaining('new project name'), findsOneWidget);

      await submitRequest(tester);

      expect(
        mockService.lastSubmitted,
        isNotNull,
        reason:
            'text on screen: '
            '${find.byType(Text).evaluate().map((e) => (e.widget as Text).data).whereType<String>().toList()}',
      );
      expect(mockService.lastSubmitted!['projectName'], 'Galle Face Promenade');
      // No matching project means no id is invented for the form to send.
      expect(mockService.lastSubmitted!['projectId'], 0);
    });

    testWidgets('text matching no record says so instead of offering one', (
      tester,
    ) async {
      final mockService = MockOperationsService();
      await openCreateSheet(tester, mockService);

      await tester.enterText(
        find.byKey(const Key('material-field')),
        'unobtainium',
      );
      await tester.pumpAndSettle();

      expect(find.textContaining('No match'), findsOneWidget);
    });

    testWidgets('Submitting with no project or material selected is refused', (
      tester,
    ) async {
      final mockService = MockOperationsService();
      await openCreateSheet(tester, mockService);

      await tester.enterText(
        find.widgetWithText(TextField, 'Quantity *'),
        '500',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Justification *'),
        'For masonry work',
      );
      await submitRequest(tester);

      // Nothing was sent, because the request is incomplete. The project is typed in
      // as text, so the question is whether anything was typed at all.
      expect(mockService.lastSubmitted, isNull);
      expect(find.text('Enter a project name.'), findsOneWidget);
    });
    testWidgets(
      'Typed alphanumeric material resolves and edited text clears its selection',
      (tester) async {
        final service = MockOperationsService();
        await openCreateSheet(tester, service);
        await pickDropdown(tester, 'Project *', 'Kandy Heights — Tower A');
        await tester.enterText(
          find.byKey(const Key('material-field')),
          'cement (50kg bag)',
        );
        await tester.enterText(
          find.widgetWithText(TextField, 'Quantity *'),
          '10',
        );
        await tester.enterText(
          find.widgetWithText(TextField, 'Justification *'),
          'Concrete work',
        );
        await tester.enterText(
          find.widgetWithText(TextField, 'Site Notes'),
          'Store inside',
        );
        await submitRequest(tester);
        expect(service.lastSubmitted?['materialId'], 1);

        service.lastSubmitted = null;
        await tester.tap(find.text('Create Request'));
        await tester.pumpAndSettle();
        await pickDropdown(tester, 'Project *', 'Kandy Heights — Tower A');
        await pickDropdown(tester, 'Material *', 'Cement (50kg bag)');
        await tester.enterText(
          find.byKey(const Key('material-field')),
          'Steel 12mm unknown',
        );
        await tester.enterText(
          find.widgetWithText(TextField, 'Quantity *'),
          '10',
        );
        await tester.enterText(
          find.widgetWithText(TextField, 'Justification *'),
          'Concrete work',
        );
        await tester.enterText(
          find.widgetWithText(TextField, 'Site Notes'),
          'Store inside',
        );
        await submitRequest(tester);
        expect(service.lastSubmitted?['materialId'], 0);
        expect(service.lastSubmitted?['materialName'], 'Steel 12mm unknown');
      },
    );

    testWidgets('A zero quantity is refused before any request is sent', (
      tester,
    ) async {
      final mockService = MockOperationsService();
      await openCreateSheet(tester, mockService);

      await pickDropdown(tester, 'Project *', 'Kandy Heights — Tower A');
      await pickDropdown(tester, 'Material *', 'Cement (50kg bag)');

      await tester.enterText(find.widgetWithText(TextField, 'Quantity *'), '0');
      await tester.enterText(
        find.widgetWithText(TextField, 'Justification *'),
        'For masonry work',
      );
      await submitRequest(tester);

      expect(mockService.lastSubmitted, isNull);
      expect(
        find.text('Quantity must be a positive number greater than 0.'),
        findsOneWidget,
      );
    });

    testWidgets('A missing justification is refused', (tester) async {
      final mockService = MockOperationsService();
      await openCreateSheet(tester, mockService);

      await pickDropdown(tester, 'Project *', 'Riverside Apartments — Block C');
      await pickDropdown(tester, 'Material *', 'Cement (50kg bag)');

      await tester.enterText(
        find.widgetWithText(TextField, 'Quantity *'),
        '500',
      );
      await submitRequest(tester);

      expect(mockService.lastSubmitted, isNull);
      expect(
        find.text('Enter a justification for this request.'),
        findsOneWidget,
      );
    });

    testWidgets('Countable quantities and site notes match web validation', (
      tester,
    ) async {
      final service = MockOperationsService();
      await openCreateSheet(tester, service);
      await pickDropdown(tester, 'Project *', 'Kandy Heights — Tower A');
      await pickDropdown(tester, 'Material *', 'Concrete Blocks');
      final quantity = find.widgetWithText(TextField, 'Quantity *');
      await tester.enterText(quantity, '10.5');
      await tester.enterText(
        find.widgetWithText(TextField, 'Justification *'),
        'Masonry',
      );
      await submitRequest(tester);
      expect(service.lastSubmitted, isNull);
      expect(
        find.textContaining('Decimal quantities are not allowed'),
        findsOneWidget,
      );
      await tester.ensureVisible(quantity);
      await tester.pumpAndSettle();
      await tester.enterText(quantity, '10');
      await submitRequest(tester);
      expect(service.lastSubmitted, isNull);
      expect(find.text('Enter site notes for this request.'), findsOneWidget);
    });

    testWidgets('A complete request submits the chosen project and material', (
      tester,
    ) async {
      final mockService = MockOperationsService();
      await openCreateSheet(tester, mockService);

      // Choose the *second* project and material, proving the form sends the
      // engineer's selection rather than a hard-coded first row.
      await pickDropdown(tester, 'Project *', 'Kandy Heights — Tower A');
      await pickDropdown(tester, 'Material *', 'Concrete Blocks');

      await tester.enterText(
        find.widgetWithText(TextField, 'Quantity *'),
        '500',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Justification *'),
        'Required for masonry',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Site Notes'),
        'Unload at the north gate',
      );
      await tester.enterText(
        find.widgetWithText(TextField, 'Material Specification / Description'),
        'Class A blocks',
      );
      await submitRequest(tester);

      expect(mockService.lastSubmitted, isNotNull);
      expect(mockService.lastSubmitted!['projectId'], 2);
      expect(mockService.lastSubmitted!['materialId'], 2);
      expect(mockService.lastSubmitted!['quantity'], 500.0);
      expect(mockService.lastSubmitted!['unit'], 'units');
      expect(
        mockService.lastSubmitted!['siteNotes'],
        'Unload at the north gate',
      );
      expect(mockService.lastSubmitted!['description'], 'Class A blocks');
      expect(mockService.lastSubmitted!['requestDate'], isNotNull);
    });
  });
}
