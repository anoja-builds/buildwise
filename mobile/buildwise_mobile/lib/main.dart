import 'package:flutter/material.dart';

import 'common/screens/screens.dart';
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
  late Future<bool> _signedInFuture;

  @override
  void initState() {
    super.initState();
    _signedInFuture = _authService.isSignedIn();
  }

  void _handleSignedIn() {
    setState(() {
      _signedInFuture = Future.value(true);
    });
  }

  void _handleSignedOut() {
    setState(() {
      _signedInFuture = Future.value(false);
    });
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<bool>(
    future: _signedInFuture,
    builder: (context, snapshot) {
      if (snapshot.connectionState != ConnectionState.done) {
        return const Scaffold(body: Center(child: CircularProgressIndicator()));
      }
      if (snapshot.data == true) {
        return MainAppShell(onSignOut: _handleSignedOut);
      }
      return LoginScreen(onSignedIn: _handleSignedIn, authService: _authService);
    },
  );
}

class MainAppShell extends StatefulWidget {
  const MainAppShell({super.key, required this.onSignOut});
  final VoidCallback onSignOut;

  @override
  State<MainAppShell> createState() => _MainAppShellState();
}

class _MainAppShellState extends State<MainAppShell> {
  int selectedIndex = 0;
  final _authService = AuthService();

  static const screens = [
    MobileHomeBaseScreen(),
    MaterialRequestListScreen(),
    PoListScreen(),
    DeliveryListScreen(),
    PendingInspectionsScreen(),
  ];

  Future<void> _signOut() async {
    await _authService.logout();
    widget.onSignOut();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
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
    body: IndexedStack(index: selectedIndex, children: screens),
    bottomNavigationBar: NavigationBar(
      selectedIndex: selectedIndex,
      onDestinationSelected: (index) => setState(() => selectedIndex = index),
      destinations: const [
        NavigationDestination(
          icon: Icon(Icons.home_outlined),
          selectedIcon: Icon(Icons.home),
          label: 'Home',
        ),
        NavigationDestination(
          icon: Icon(Icons.assignment_outlined),
          selectedIcon: Icon(Icons.assignment),
          label: 'Requests',
        ),
        NavigationDestination(
          icon: Icon(Icons.shopping_bag_outlined),
          selectedIcon: Icon(Icons.shopping_bag),
          label: 'Orders',
        ),
        NavigationDestination(
          icon: Icon(Icons.local_shipping_outlined),
          selectedIcon: Icon(Icons.local_shipping),
          label: 'Deliveries',
        ),
        NavigationDestination(
          icon: Icon(Icons.fact_check_outlined),
          selectedIcon: Icon(Icons.fact_check),
          label: 'Quality',
        ),
      ],
    ),
  );
}

