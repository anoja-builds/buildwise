import 'package:flutter/material.dart';

import 'core/theme/app_theme.dart';
import 'core/api/api_client.dart';
import 'core/theme/app_colors.dart';
import 'core/widgets/widgets.dart' as ui;
import 'features/auth/screens/login_screen.dart';
import 'features/auth/services/auth_service.dart';
import 'features/deliveries/screens/delivery_list_screen.dart';
import 'features/material_requests/screens/material_request_list_screen.dart';
import 'features/procurement/screens/po_list_screen.dart';
import 'features/procurement/screens/procurement_home_screen.dart';
import 'features/procurement/screens/supplier_screens.dart';
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
  bool _expiring = false;

  Future<void> _expireSession() async {
    if (_expiring) return;
    _expiring = true;
    await _authService.logout();
    if (mounted) {
      Navigator.of(context).popUntil((route) => route.isFirst);
      _handleSignedOut();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Session expired. Please sign in again.')),
      );
    }
    _expiring = false;
  }

  Future<Map<String, dynamic>?> _readSession() async {
    if (!await _authService.isSignedIn()) return null;
    return await _authService.currentUser() ?? <String, dynamic>{};
  }

  @override
  void initState() {
    super.initState();
    _sessionFuture = _readSession();
    ApiClient.sessionExpired.addListener(_expireSession);
  }

  @override
  void dispose() {
    ApiClient.sessionExpired.removeListener(_expireSession);
    super.dispose();
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
      if (hasAny(const [
        'ProcurementOfficer',
        'ProcurementManager',
        'Administrator',
      ]))
        (
          label: 'Procurement',
          icon: Icons.work_outline,
          screen: ProcurementHomeScreen(
            manager: hasAny(const ['ProcurementManager', 'Administrator']),
          ),
        ),
      if (hasAny(const [
        'ProcurementOfficer',
        'ProcurementManager',
        'Administrator',
      ]))
        (
          label: 'Suppliers',
          icon: Icons.business_outlined,
          screen: SuppliersScreen(
            manager: hasAny(const ['ProcurementManager', 'Administrator']),
          ),
        ),
      if (hasAny(const [
        'ProcurementOfficer',
        'ProcurementManager',
        'Administrator',
      ]))
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
    final overflow = destinations.length > 5;
    final visible = overflow ? destinations.take(4).toList() : destinations;
    return ui.WorkspaceSession(
      onSignOut: _signOut,
      child: Scaffold(
        appBar: destinations.isEmpty
            ? ui.WorkspaceAppBar(title: const Text('BuildWise'))
            : null,
        body: destinations.isEmpty
            ? Center(
                child: Padding(
                  padding: const EdgeInsets.all(24.0),
                  child: Text(
                    'Your account has no assigned workspace.',
                    textAlign: TextAlign.center,
                  ),
                ),
              )
            : destinations[index].screen,
        bottomNavigationBar: destinations.length < 2
            ? null
            : NavigationBar(
                backgroundColor: AppColors.primaryDark,
                indicatorColor: Colors.transparent,
                height: 64,
                labelTextStyle: WidgetStateProperty.resolveWith(
                  (states) => TextStyle(
                    fontSize: 11,
                    color: states.contains(WidgetState.selected)
                        ? AppColors.accent
                        : const Color(0xFF9CA3AF),
                  ),
                ),
                selectedIndex: overflow && index >= 4 ? 4 : index,
                onDestinationSelected: (selected) async {
                  if (overflow && selected == 4) {
                    final next = await showModalBottomSheet<int>(
                      context: context,
                      builder: (context) => SafeArea(
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            for (var i = 4; i < destinations.length; i++)
                              ListTile(
                                title: Text(destinations[i].label),
                                leading: Icon(destinations[i].icon),
                                onTap: () => Navigator.pop(context, i),
                              ),
                          ],
                        ),
                      ),
                    );
                    if (next != null && mounted) {
                      setState(() => selectedIndex = next);
                    }
                  } else {
                    setState(() => selectedIndex = selected);
                  }
                },
                destinations: [
                  ...visible.map(
                    (destination) => NavigationDestination(
                      icon: const Icon(
                        Icons.circle,
                        size: 20,
                        color: Color(0xFF9CA3AF),
                      ),
                      selectedIcon: const Icon(
                        Icons.circle,
                        size: 20,
                        color: AppColors.accent,
                      ),
                      label: destination.label,
                    ),
                  ),
                  if (overflow)
                    const NavigationDestination(
                      icon: Icon(Icons.more_horiz, color: Color(0xFF9CA3AF)),
                      selectedIcon: Icon(
                        Icons.more_horiz,
                        color: AppColors.accent,
                      ),
                      label: 'More',
                    ),
                ],
              ),
      ),
    );
  }
}
