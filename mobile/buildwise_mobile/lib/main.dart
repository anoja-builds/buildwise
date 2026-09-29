import 'package:flutter/material.dart';

import 'core/theme/app_theme.dart';
import 'features/auth/screens/login_screen.dart';
import 'features/auth/services/auth_service.dart';
import 'features/deliveries/screens/delivery_list_screen.dart';
import 'features/material_requests/screens/material_request_list_screen.dart';
import 'features/procurement/screens/po_list_screen.dart';
import 'features/quality/screens/pending_inspections_screen.dart';

void main() => runApp(const BuildWiseApp());

class BuildWiseApp extends StatelessWidget {
  const BuildWiseApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'BuildWise Mobile',
    debugShowCheckedModeBanner: false,
    theme: AppTheme.light,
    home: const AuthGate(),
  );
}

/// Shows the sign-in screen until a JWT is present in secure storage, then
/// hands off to the main app shell.
class AuthGate extends StatefulWidget {
  const AuthGate({super.key});

  @override
  State<AuthGate> createState() => _AuthGateState();
}

class _AuthGateState extends State<AuthGate> {
  final _authService = AuthService();
  late Future<Map<String, dynamic>?> _sessionFuture;

  Future<Map<String, dynamic>?> _readSession() async {
    if (!await _authService.isSignedIn()) return null;
    return await _authService.currentUser() ?? <String, dynamic>{};
  }

  @override
  void initState() {
    super.initState();
    _sessionFuture = _readSession();
  }

  void _handleSignedIn() {
    setState(() {
      _sessionFuture = _readSession();
    });
  }

  void _handleSignedOut() {
    setState(() {
      _sessionFuture = Future.value(null);
    });
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<Map<String, dynamic>?>(
    future: _sessionFuture,
    builder: (context, snapshot) {
      if (snapshot.connectionState != ConnectionState.done) {
        return const Scaffold(body: Center(child: CircularProgressIndicator()));
      }
      if (snapshot.data != null) {
        final value = snapshot.data!['roles'];
        final roles = value is List
            ? value.whereType<String>().toList()
            : <String>[];
        return MainAppShell(roles: roles, onSignOut: _handleSignedOut);
      }
      return LoginScreen(
        onSignedIn: _handleSignedIn,
        authService: _authService,
      );
    },
  );
}

class MainAppShell extends StatefulWidget {
  const MainAppShell({super.key, required this.onSignOut, required this.roles});

  final List<String> roles;

  final VoidCallback onSignOut;

  @override
  State<MainAppShell> createState() => _MainAppShellState();
}

class _MainAppShellState extends State<MainAppShell> {
  int selectedIndex = 0;
  final _authService = AuthService();

  bool get _canReceive => widget.roles.any(
    (role) => const ['SiteEngineer', 'Administrator'].contains(role),
  );

  bool get _isProcurementOnly =>
      widget.roles.any(
        (role) => const ['ProcurementOfficer', 'ProcurementManager'].contains(role),
      ) &&
      !widget.roles.any(
        (role) => const ['SiteEngineer', 'QualityInspector', 'Administrator'].contains(role),
      );

  List<({String label, IconData icon, Widget screen})> get _destinations {
    bool hasAny(List<String> allowed) => widget.roles.any(allowed.contains);
    return [
      if (hasAny(const ['SiteEngineer', 'Administrator']))
        (
          label: 'Requests',
          icon: Icons.assignment_outlined,
          screen: MaterialRequestListScreen(
            canCreate: _canReceive,
            canViewProcurementStatus: _canReceive,
          ),
        ),
      if (hasAny(const ['Administrator']))
        (
          label: 'Orders',
          icon: Icons.shopping_bag_outlined,
          screen: const PoListScreen(),
        ),
      if (hasAny(const ['SiteEngineer', 'QualityInspector', 'Administrator']))
        (
          label: 'Deliveries',
          icon: Icons.local_shipping_outlined,
          screen: DeliveryListScreen(canReceive: _canReceive),
        ),
      if (hasAny(const ['QualityInspector', 'Administrator']))
        (
          label: 'Quality',
          icon: Icons.fact_check_outlined,
          screen: const PendingInspectionsScreen(),
        ),
    ];
  }

  Future<void> _signOut() async {
    await _authService.logout();
    widget.onSignOut();
  }

  @override
  Widget build(BuildContext context) {
    final destinations = _destinations;
    final index = selectedIndex < destinations.length ? selectedIndex : 0;
    return Scaffold(
      appBar: AppBar(
        title: const Text('BuildWise'),
        actions: [
          IconButton(
            onPressed: () {},
            tooltip: 'Notifications',
            icon: const Icon(Icons.notifications_none),
          ),
          IconButton(
            onPressed: _signOut,
            tooltip: 'Sign out',
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: destinations.isEmpty
          ? Center(
              child: Padding(
                padding: const EdgeInsets.all(24.0),
                child: Text(
                  _isProcurementOnly
                      ? 'Procurement management workflows are available in the BuildWise Web Portal.'
                      : 'Your account has no assigned workspace.',
                  textAlign: TextAlign.center,
                ),
              ),
            )
          : destinations[index].screen,
      bottomNavigationBar: destinations.length < 2
          ? null
          : NavigationBar(
              selectedIndex: index,
              onDestinationSelected: (index) =>
                  setState(() => selectedIndex = index),
              destinations: destinations
                  .map(
                    (destination) => NavigationDestination(
                      icon: Icon(destination.icon),
                      label: destination.label,
                    ),
                  )
                  .toList(),
            ),
    );
  }
}
