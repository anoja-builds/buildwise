import 'dart:async';

import 'package:flutter/material.dart';

import 'features/operations/screens/administration_screen.dart';
import 'features/operations/screens/agent_workflows_screen.dart';
import 'features/operations/screens/dashboard_screen.dart';
import 'features/operations/screens/delivery_receiving_screen.dart';
import 'features/operations/screens/material_requests_screen.dart';
import 'features/operations/screens/quality_inspection_screen.dart';
import 'core/auth/buildwise_roles.dart';
import 'core/theme/app_colors.dart';
import 'core/theme/app_theme.dart';
import 'core/widgets/widgets.dart' hide ErrorWidget;
import 'core/widgets/error_widget.dart' as buildwise;
import 'features/auth/screens/login_screen.dart';
import 'features/auth/services/auth_service.dart';
import 'features/operations/services/operations_service.dart';
import 'features/procurement/screens/procurement_home_screen.dart';
import 'features/procurement/screens/quotation_comparison_screen.dart';
import 'features/procurement/screens/rfq_screen.dart';
import 'features/procurement/services/notification_service.dart';
import 'features/supplier/screens/supplier_portal_screen.dart';

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
  final _operationsService = OperationsService();
  Timer? _notificationTimer;
  int _unreadNotifications = 0;
  final Set<int> _notifiedNotificationIds = <int>{};
  late Future<void> _sessionFuture;

  @override
  void initState() {
    super.initState();
    _sessionFuture = _loadSession();
    _notificationTimer = Timer.periodic(const Duration(seconds: 30), (_) => _pollNotifications(showDeviceNotification: true));
  }

  @override
  void dispose() {
    _notificationTimer?.cancel();
    super.dispose();
  }

  Future<void> _loadSession() async {
    final user = await _authService.currentUser();
    if (mounted) {
      setState(() {
        _roles = ((user?['roles'] as List<dynamic>?) ?? const []).cast<String>();
      });
      await _pollNotifications(showDeviceNotification: false);
    }
  }

  Future<void> _pollNotifications({required bool showDeviceNotification}) async {
    // The notifications API is internal-staff only, so a Supplier login must
    // not poll it — otherwise it would log a 403 every 30 seconds.
    if (BuildWiseRoles.isSupplier(_roles)) return;
    try {
      final rows = await _operationsService.listNotifications(unreadOnly: true);
      if (!mounted) return;
      setState(() => _unreadNotifications = rows.length);
      if (!showDeviceNotification) return;
      for (final row in rows.take(3)) {
        final id = (row['id'] as num?)?.toInt() ?? 0;
        if (id == 0 || !_notifiedNotificationIds.add(id)) continue;
        final title = row['title']?.toString() ?? 'BuildWise update';
        final body = row['body']?.toString() ?? 'Your workflow has a new update.';
        if (row['type'] == 'QualityInspectionCompleted') {
          await NotificationService.instance.showQualityUpdate(id: id, title: title, body: body);
        } else {
          await NotificationService.instance.showProcurementUpdate(id: id, title: title, body: body);
        }
      }
    } catch (_) {
      // Notification polling is best-effort; protected API operations remain authoritative.
    }
  }

  Future<void> _openNotifications() async {
    if (BuildWiseRoles.isSupplier(_roles)) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Notifications are not available on the supplier portal.')),
      );
      return;
    }
    try {
      final rows = await _operationsService.listNotifications();
      if (!mounted) return;
      await showModalBottomSheet<void>(
        context: context,
        showDragHandle: true,
        builder: (context) => SafeArea(
          child: ListView(
            shrinkWrap: true,
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
            children: [
              Text('Notifications', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 12),
              if (rows.isEmpty) const Text('No notifications yet.'),
              ...rows.map((row) => ListTile(
                leading: Icon(row['isRead'] == true ? Icons.notifications_none : Icons.notifications_active, color: row['isRead'] == true ? AppColors.textMuted : AppColors.primary),
                title: Text('${row['title']}'),
                subtitle: Text('${row['body']}'),
                onTap: () async {
                  final id = (row['id'] as num?)?.toInt();
                  if (id != null && row['isRead'] != true) await _operationsService.markNotificationRead(id);
                  if (context.mounted) Navigator.pop(context);
                },
              )),
            ],
          ),
        ),
      );
      await _pollNotifications(showDeviceNotification: false);
    } catch (error) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.toString().replaceFirst('Exception: ', ''))));
    }
  }

  bool _has(Set<String> roles) => roles.any(_roles.contains);

  /// Role sets live in `core/auth/buildwise_roles.dart` so this shell and the
  /// backend policy grants cannot disagree about who sees which screen.
  List<_MobileDestination> get _destinations {
    final destinations = <_MobileDestination>[
      _MobileDestination(
        label: 'Home',
        icon: Icons.home_outlined,
        selectedIcon: Icons.home,
        builder: (_) => DashboardScreen(roles: _roles),
      ),
    ];

    // A Supplier account gets a dedicated portal and nothing else — it must
    // never see internal operations screens.
    if (BuildWiseRoles.isSupplier(_roles)) {
      for (final section in const ['Rfqs', 'Quotations', 'PurchaseOrders']) {
        destinations.add(_MobileDestination(
          label: switch (section) {
            'Quotations' => 'Quotes',
            'PurchaseOrders' => 'Orders',
            _ => 'RFQs',
          },
          icon: switch (section) {
            'Quotations' => Icons.request_quote_outlined,
            'PurchaseOrders' => Icons.receipt_long_outlined,
            _ => Icons.mail_outline,
          },
          selectedIcon: switch (section) {
            'Quotations' => Icons.request_quote,
            'PurchaseOrders' => Icons.receipt_long,
            _ => Icons.mail,
          },
          builder: (_) => SupplierPortalScreen(section: section),
        ));
      }
      return destinations;
    }

    if (!_has(BuildWiseRoles.internalStaff)) return destinations;

    // Site roles and the receiving officer get the request + receiving screens.
    // ReceivingOfficer was previously a seeded role with no screens at all.
    if (_has({...BuildWiseRoles.siteOperations, BuildWiseRoles.receivingOfficer, BuildWiseRoles.administrator})) {
      destinations.addAll([
        _MobileDestination(
          label: 'Requests',
          icon: Icons.assignment_outlined,
          selectedIcon: Icons.assignment,
          builder: (_) => MaterialRequestsScreen(
            readOnly: _has({BuildWiseRoles.administrator}),
          ),
        ),
        _MobileDestination(
          label: 'Receiving',
          icon: Icons.local_shipping_outlined,
          selectedIcon: Icons.local_shipping,
          builder: (_) => DeliveryReceivingScreen(
            readOnly: _has({BuildWiseRoles.administrator}),
          ),
        ),
      ]);
    }

    if (_has({
      ...BuildWiseRoles.siteOperations,
      BuildWiseRoles.procurementOfficer,
      BuildWiseRoles.procurementManager,
      BuildWiseRoles.siteManager,
      BuildWiseRoles.administrator,
    })) {
      destinations.add(_MobileDestination(
        label: 'Procurement',
        icon: Icons.route_outlined,
        selectedIcon: Icons.route,
        builder: (_) => ProcurementHomeScreen(
          siteScoped: BuildWiseRoles.isSite(_roles),
        ),
      ));
      // RFQ issuing and quotation comparison are commercial surfaces, so they
      // are gated to the same roles as Procurement — a site user sees only the
      // redacted procurement status view.
      destinations.addAll([
        _MobileDestination(
          label: 'RFQs',
          icon: Icons.mail_outline,
          selectedIcon: Icons.mail,
          builder: (_) => const RfqScreen(),
        ),
        _MobileDestination(
          label: 'Quotes',
          icon: Icons.compare_arrows_outlined,
          selectedIcon: Icons.compare_arrows,
          builder: (_) => const QuotationComparisonScreen(),
        ),
      ]);
    }

    if (_has({BuildWiseRoles.qualityInspector, BuildWiseRoles.administrator})) {
      destinations.add(_MobileDestination(
        label: 'Quality',
        icon: Icons.fact_check_outlined,
        selectedIcon: Icons.fact_check,
        builder: (_) => QualityInspectionScreen(
          readOnly: _has({BuildWiseRoles.administrator}),
        ),
      ));
    }

    // Agent workflow history: the cross-component view of which agents ran.
    // Visible to the procurement and management roles that can start a run.
    if (_has({
      BuildWiseRoles.procurementOfficer,
      BuildWiseRoles.procurementManager,
      BuildWiseRoles.siteManager,
      BuildWiseRoles.projectManager,
      BuildWiseRoles.administrator,
    })) {
      destinations.add(_MobileDestination(
        label: 'Agents',
        icon: Icons.hub_outlined,
        selectedIcon: Icons.hub,
        builder: (_) => const AgentWorkflowsScreen(),
      ));
    }

    // Administration: user register, audit trail and per-service health.
    if (_has({BuildWiseRoles.administrator})) {
      destinations.add(_MobileDestination(
        label: 'Admin',
        icon: Icons.admin_panel_settings_outlined,
        selectedIcon: Icons.admin_panel_settings,
        builder: (_) => const AdministrationScreen(),
      ));
    }
    return destinations;
  }

  void openRoute(String route) {
    if (BuildWiseRoles.isSupplier(_roles)) {
      final supplierIndex = switch (route) {
        '/supplier/quotations' => 2,
        '/supplier/purchase-orders' => 3,
        _ => 1,
      };
      final destinations = _destinations;
      if (supplierIndex < destinations.length) {
        setState(() => selectedIndex = supplierIndex);
      }
      return;
    }

    final label = switch (route) {
      '/material-requests' => 'Requests',
      '/deliveries' => 'Receiving',
      '/procurement' || '/rfqs' || '/quotations' || '/purchase-orders' || '/agent-workflows' => 'Procurement',
      '/quality-inspections' || '/suppliers' => 'Quality',
      _ => 'Home',
    };
    final index = _destinations.indexWhere((destination) => destination.label == label);
    if (index >= 0) setState(() => selectedIndex = index);
  }

  void openDestination(int index) {
    if (index < 0 || index >= _destinations.length) return;
    setState(() => selectedIndex = index);
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
            Badge(
              isLabelVisible: _unreadNotifications > 0,
              label: Text('$_unreadNotifications'),
              child: IconButton(onPressed: _openNotifications, tooltip: 'Notifications', icon: const Icon(Icons.notifications_none)),
            ),
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

class _RoleHomeScreen extends StatefulWidget {
  const _RoleHomeScreen({required this.roles});
  final List<String> roles;

  @override
  State<_RoleHomeScreen> createState() => _RoleHomeScreenState();
}

class _RoleHomeScreenState extends State<_RoleHomeScreen> {
  final _service = OperationsService();
  Map<String, dynamic>? _dashboard;
  bool _loading = true;
  String? _error;

  String get _primaryRole => (_dashboard?['primaryRole'] as String?) ?? (widget.roles.isNotEmpty ? widget.roles.first : 'TeamMember');

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final data = await _service.getDashboard();
      if (mounted) setState(() => _dashboard = data);
    } catch (error) {
      if (mounted) setState(() => _error = error.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null) return Padding(padding: const EdgeInsets.all(16), child: buildwise.ErrorWidget(message: _error!, onRetry: _load));
    final data = _dashboard ?? const <String, dynamic>{};
    final metrics = ((data['metrics'] as List<dynamic>?) ?? const []).cast<Map<String, dynamic>>();
    final tasks = ((data['tasks'] as List<dynamic>?) ?? const []).cast<Map<String, dynamic>>();
    final alerts = ((data['alerts'] as List<dynamic>?) ?? const []).cast<Map<String, dynamic>>();
    final copy = {
      'SiteEngineer': ('Site Engineer Dashboard', 'Track your site material demand and request progress.'),
      'SiteOfficer': ('Site Officer Dashboard', 'Reconcile arrivals and record receiving evidence.'),
      'ReceivingOfficer': ('Receiving Officer Dashboard', 'Record arrivals and evidence against confirmed orders.'),
      'QualityInspector': ('Quality Inspector Dashboard', 'Inspect materials and manage active NCRs.'),
      'ProcurementOfficer': ('Procurement Officer Dashboard', 'Move requests through RFQ and supplier evaluation.'),
      'ProcurementManager': ('Procurement Manager Dashboard', 'Review recommendations and purchasing decisions.'),
      'SiteManager': ('Site Manager Dashboard', 'Monitor procurement and delivery exceptions.'),
      'Administrator': ('Administrator Dashboard', 'Monitor system-wide operational activity.'),
      'Supplier': ('Supplier Portal', 'Respond to RFQ invitations and track your quotations and orders.'),
    }[_primaryRole] ?? ('BuildWise Dashboard', 'Your role-aware operations overview.');
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(copy.$1, style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 6),
          Text(copy.$2, style: const TextStyle(color: AppColors.textMuted)),
          const SizedBox(height: 18),
          GridView.count(
            crossAxisCount: 2,
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            crossAxisSpacing: 12,
            mainAxisSpacing: 12,
            childAspectRatio: 1.45,
            children: metrics.map((metric) => AppCard(child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisAlignment: MainAxisAlignment.center, children: [Text('${metric['value']}${metric['suffix'] == null ? '' : ' ${metric['suffix']}'}', style: const TextStyle(fontSize: 25, fontWeight: FontWeight.w800)), const SizedBox(height: 6), Text('${metric['label']}', style: const TextStyle(color: AppColors.textMuted))]))).toList(),
          ),
          const SizedBox(height: 22),
          const SectionHeader(title: 'Quick actions'),
          ...tasks.map((task) => Padding(padding: const EdgeInsets.only(bottom: 10), child: AppCard(onTap: () => _openTask(task['route'] as String? ?? ''), child: Row(children: [const Icon(Icons.arrow_forward, color: AppColors.primary), const SizedBox(width: 12), Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Text('${task['title']}', style: const TextStyle(fontWeight: FontWeight.w700)), const SizedBox(height: 4), Text('${task['description']}', style: const TextStyle(color: AppColors.textMuted))])), const Icon(Icons.chevron_right, color: AppColors.textMuted)])))),
          if (alerts.isNotEmpty) ...[
            const SizedBox(height: 14),
            const SectionHeader(title: 'Attention required'),
            ...alerts.map((alert) => Padding(padding: const EdgeInsets.only(bottom: 10), child: AppCard(onTap: () => _openTask(alert['route'] as String? ?? ''), child: ListTile(contentPadding: EdgeInsets.zero, leading: Icon(Icons.warning_amber, color: alert['severity'] == 'Danger' ? AppColors.danger : AppColors.warning), title: Text('${alert['title']}'), subtitle: Text('${alert['detail']}'))))),
          ],
        ],
      ),
    );
  }

  void _openTask(String route) {
    context.findAncestorStateOfType<_MainAppShellState>()?.openRoute(route);
  }
}
