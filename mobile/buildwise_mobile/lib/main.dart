import 'package:flutter/material.dart';

import 'features/operations/screens/delivery_receiving_screen.dart';
import 'features/operations/screens/material_requests_screen.dart';
import 'features/operations/screens/quality_inspection_screen.dart';
import 'core/theme/app_theme.dart';
import 'features/auth/screens/login_screen.dart';
import 'features/auth/services/auth_service.dart';
import 'features/procurement/screens/procurement_home_screen.dart';

void main() => runApp(const BuildWiseApp());

class BuildWiseApp extends StatelessWidget {
  const BuildWiseApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'BuildWise',
    debugShowCheckedModeBanner: false,
    theme: AppTheme.light,
    home: const AuthGate(),
  );
}

/// Shows the sign-in screen until a JWT is present in secure storage, then
/// hands off to the main app shell (spec §8: "protected screens").
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

  void _handleSignedIn() => setState(() => _signedInFuture = Future.value(true));
  void _handleSignedOut() => setState(() => _signedInFuture = Future.value(false));

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
  List<String> _roles = const [];
  final _authService = AuthService();
  late Future<void> _sessionFuture;

  @override
  void initState() {
    super.initState();
    _sessionFuture = _loadSession();
  }

  Future<void> _loadSession() async {
    final user = await _authService.currentUser();
    if (mounted) {
      setState(() {
        _roles = ((user?['roles'] as List<dynamic>?) ?? const []).cast<String>();
      });
    }
  }

  bool _has(Set<String> roles) => roles.any(_roles.contains);

  List<_MobileDestination> get _destinations {
    final destinations = <_MobileDestination>[
      _MobileDestination(
        label: 'Home',
        icon: Icons.home_outlined,
        selectedIcon: Icons.home,
        builder: (_) => _RoleHomeScreen(roles: _roles),
      ),
    ];
    if (_has({'SiteEngineer', 'SiteOfficer', 'Administrator'})) {
      destinations.addAll([
        _MobileDestination(
          label: 'Requests',
          icon: Icons.assignment_outlined,
          selectedIcon: Icons.assignment,
          builder: (_) => MaterialRequestsScreen(
            readOnly: _has({'Administrator'}),
          ),
        ),
        _MobileDestination(
          label: 'Receiving',
          icon: Icons.local_shipping_outlined,
          selectedIcon: Icons.local_shipping,
          builder: (_) => DeliveryReceivingScreen(
            readOnly: _has({'Administrator'}),
          ),
        ),
      ]);
    }
    if (_has({'SiteEngineer', 'SiteOfficer', 'ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'})) {
      destinations.add(_MobileDestination(
        label: 'Procurement',
        icon: Icons.route_outlined,
        selectedIcon: Icons.route,
        builder: (_) => ProcurementHomeScreen(
          siteScoped: _has({'SiteEngineer', 'SiteOfficer'}),
        ),
      ));
    }
    if (_has({'QualityInspector', 'Administrator'})) {
      destinations.add(_MobileDestination(
        label: 'Quality',
        icon: Icons.fact_check_outlined,
        selectedIcon: Icons.fact_check,
        builder: (_) => QualityInspectionScreen(
          readOnly: _has({'Administrator'}),
        ),
      ));
    }
    return destinations;
  }

  Future<void> _signOut() async {
    await _authService.logout();
    widget.onSignOut();
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<void>(
    future: _sessionFuture,
    builder: (context, snapshot) {
      if (snapshot.connectionState != ConnectionState.done) {
        return const Scaffold(body: Center(child: CircularProgressIndicator()));
      }
      final destinations = _destinations;
      final safeIndex = selectedIndex.clamp(0, destinations.length - 1);
      return Scaffold(
        appBar: AppBar(
          title: const Text('BuildWise'),
          actions: [
            IconButton(onPressed: () {}, tooltip: 'Notifications', icon: const Icon(Icons.notifications_none)),
            IconButton(onPressed: _signOut, tooltip: 'Sign out', icon: const Icon(Icons.logout)),
          ],
        ),
        body: IndexedStack(index: safeIndex, children: [for (final item in destinations) item.builder(context)]),
        bottomNavigationBar: NavigationBar(
          selectedIndex: safeIndex,
          onDestinationSelected: (index) => setState(() => selectedIndex = index),
          destinations: [
            for (final item in destinations)
              NavigationDestination(icon: Icon(item.icon), selectedIcon: Icon(item.selectedIcon), label: item.label),
          ],
        ),
      );
    },
  );
}

class _MobileDestination {
  const _MobileDestination({required this.label, required this.icon, required this.selectedIcon, required this.builder});
  final String label;
  final IconData icon;
  final IconData selectedIcon;
  final WidgetBuilder builder;
}

class _RoleHomeScreen extends StatelessWidget {
  const _RoleHomeScreen({required this.roles});
  final List<String> roles;

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(16),
    children: [
      Text('BuildWise Operations', style: Theme.of(context).textTheme.headlineSmall),
      const SizedBox(height: 8),
      Text('Signed in as ${roles.isEmpty ? 'Team member' : roles.join(', ')}', style: const TextStyle(color: Colors.black54)),
      const SizedBox(height: 20),
      Card(
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text('Role-based workspace', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
              const SizedBox(height: 8),
              const Text('Only destinations assigned to your JWT roles appear here. API authorization remains mandatory for every operation.'),
            ],
          ),
        ),
      ),
    ],
  );
}