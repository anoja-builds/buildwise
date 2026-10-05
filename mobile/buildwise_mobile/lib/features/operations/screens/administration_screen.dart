import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';

/// Administrator-only screen: user register, audit trail and service health.
///
/// The health section is the quickest way to confirm during a demo that all four
/// AI agents are actually reachable, rather than only that the API is up.
class AdministrationScreen extends StatefulWidget {
  const AdministrationScreen({super.key, this.service});

  final OperationsService? service;

  @override
  State<AdministrationScreen> createState() => _AdministrationScreenState();
}

class _AdministrationScreenState extends State<AdministrationScreen> with SingleTickerProviderStateMixin {
  final _service = OperationsService();
  late final TabController _tabs = TabController(length: 3, vsync: this);

  List<Map<String, dynamic>> _users = const [];
  List<Map<String, dynamic>> _auditLogs = const [];
  Map<String, dynamic>? _health;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _tabs.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    final service = widget.service ?? _service;
    try {
      // Each section is independent: a failure in one (for example audit logs
      // being restricted) must not blank the whole screen.
      final results = await Future.wait([
        service.listUsers().catchError((_) => const <dynamic>[]),
        service.listAuditLogs().catchError((_) => const <dynamic>[]),
        service.getSystemHealth().catchError((_) => <String, dynamic>{}),
      ]);
      if (!mounted) return;
      setState(() {
        _users = (results[0] as List<dynamic>).cast<Map<String, dynamic>>();
        _auditLogs = (results[1] as List<dynamic>).cast<Map<String, dynamic>>();
        _health = results[2] as Map<String, dynamic>;
      });
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Administration'),
      actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
      bottom: TabBar(
        controller: _tabs,
        tabs: const [
          Tab(text: 'Users'),
          Tab(text: 'Audit'),
          Tab(text: 'Health'),
        ],
      ),
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
            ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
            : TabBarView(
                controller: _tabs,
                children: [_usersTab(), _auditTab(), _healthTab()],
              ),
  );


  Widget _usersTab() {
    if (_users.isEmpty) {
      return const EmptyStateWidget(title: 'No users', message: 'The register is empty or restricted.');
    }
    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemCount: _users.length,
      separatorBuilder: (_, _) => const SizedBox(height: 10),
      itemBuilder: (_, index) {
        final user = _users[index];
        final roles = (user['roles'] as List<dynamic>? ?? const []).map((r) => r.toString()).toList();
        return AppCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Expanded(
                    child: Text(
                      user['fullName']?.toString() ?? user['email']?.toString() ?? 'User',
                      style: const TextStyle(fontWeight: FontWeight.bold),
                    ),
                  ),
                  StatusChip(
                    label: user['isActive'] == true ? 'Active' : 'Inactive',
                    tone: user['isActive'] == true ? StatusTone.success : StatusTone.neutral,
                  ),
                ],
              ),
              const SizedBox(height: 4),
              Text(user['email']?.toString() ?? '', style: Theme.of(context).textTheme.bodySmall),
              const SizedBox(height: 8),
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [for (final role in roles) StatusChip(label: role)],
              ),
            ],
          ),
        );
      },
    );
  }

  Widget _auditTab() {
    if (_auditLogs.isEmpty) {
      return const EmptyStateWidget(title: 'No audit entries', message: 'Nothing has been recorded yet.');
    }
    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemCount: _auditLogs.length,
      separatorBuilder: (_, _) => const SizedBox(height: 8),
      itemBuilder: (_, index) {
        final log = _auditLogs[index];
        return AppCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                log['action']?.toString() ?? 'Action',
                style: const TextStyle(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 4),
              Text(
                [
                  if (log['entityType'] != null) log['entityType'].toString(),
                  if (log['entityId'] != null) '#${log['entityId']}',
                  if (log['httpMethod'] != null) log['httpMethod'].toString(),
                  if (log['statusCode'] != null) 'HTTP ${log['statusCode']}',
                  if (log['userId'] != null) 'by user ${log['userId']}',
                ].join(' · '),
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        );
      },
    );
  }

  Widget _healthTab() {
    final health = _health ?? const <String, dynamic>{};
    // `services` is an object of name -> reachable, e.g.
    // {"quotation-agent": true, "quality-agent": true}, not a list of rows.
    final services = (health['services'] as Map<String, dynamic>?) ?? const {};
    final database = health['database'];
    final overall = health['status']?.toString() ?? 'Unknown';
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        AppCard(
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text('Overall', style: Theme.of(context).textTheme.titleMedium),
              StatusChip(
                label: overall,
                tone: overall.toLowerCase() == 'healthy'
                    ? StatusTone.success
                    : overall.toLowerCase() == 'degraded'
                        ? StatusTone.warning
                        : StatusTone.danger,
              ),
            ],
          ),
        ),
        const SizedBox(height: 10),
        AppCard(
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              const Text('Database'),
              StatusChip(
                label: database == true ? 'Reachable' : 'Unreachable',
                tone: database == true ? StatusTone.success : StatusTone.danger,
              ),
            ],
          ),
        ),
        const SizedBox(height: 16),
        Text('AI agents', style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 8),
        if (services.isEmpty)
          const AppCard(child: Text('No agent health reported.'))
        else
          ...services.entries.map((entry) {
            final ok = entry.value == true;
            return Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: AppCard(
                child: Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text(entry.key, style: const TextStyle(fontWeight: FontWeight.w600)),
                    StatusChip(
                      label: ok ? 'Healthy' : 'Unreachable',
                      tone: ok ? StatusTone.success : StatusTone.danger,
                    ),
                  ],
                ),
              ),
            );
          }),
        const SizedBox(height: 12),
        Text(
          'Checked at ${health['checkedAtUtc'] ?? '—'}',
          style: Theme.of(context).textTheme.bodySmall,
        ),
      ],
    );
  }
}
