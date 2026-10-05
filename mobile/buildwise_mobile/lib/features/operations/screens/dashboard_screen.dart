import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';

/// Role-aware dashboard, mirroring the web app's DashboardPage.
///
/// The same three metric keys are chosen per role from the backend's
/// `/dashboard` response, so mobile and web show the same headline numbers.
class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key, this.roles = const [], this.service});

  final List<String> roles;
  final OperationsService? service;

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  final _service = OperationsService();
  Map<String, dynamic>? _dashboard;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await (widget.service ?? _service).getDashboard();
      if (mounted) setState(() => _dashboard = data);
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  /// Headline metrics per role, copied from the web page so both clients agree.
  List<String> _metricKeys(String role) => switch (role) {
        'SiteEngineer' => const ['activeRequests', 'awaitingProcurement', 'approvedPendingDelivery'],
        'SiteOfficer' => const ['deliveriesExpectedToday', 'deliveriesReconciled', 'discrepanciesLogged'],
        'ProcurementOfficer' => const ['awaitingProcurement', 'activeRfqs', 'quotationsToday'],
        'ProcurementManager' => const ['proposalsAwaitingApproval', 'confirmedPurchaseOrders', 'monthlySpend'],
        'QualityInspector' => const ['deliveriesAwaitingQuality', 'qualityPassRate', 'activeNcrs'],
        'SiteManager' => const ['proposalsAwaitingApproval', 'confirmedPurchaseOrders', 'activeNcrs'],
        'Administrator' => const ['activeRequests', 'activeNcrs', 'proposalsAwaitingApproval'],
        _ => const ['activeRequests', 'awaitingProcurement', 'proposalsAwaitingApproval'],
      };

  static String _title(String role) => switch (role) {
        'SiteEngineer' => 'Site Engineer Dashboard',
        'SiteOfficer' => 'Site Officer Dashboard',
        'ProcurementOfficer' => 'Procurement Officer Dashboard',
        'ProcurementManager' => 'Procurement Manager Dashboard',
        'QualityInspector' => 'Quality Inspector Dashboard',
        'SiteManager' => 'Site Manager Dashboard',
        'Administrator' => 'Administrator Dashboard',
        _ => 'BuildWise Dashboard',
      };

  static String _description(String role) => switch (role) {
        'SiteEngineer' => 'Track your site material demand and request approval progress.',
        'SiteOfficer' => "Reconcile today's arrivals and record delivery evidence at the site.",
        'ProcurementOfficer' => 'Move approved requests through RFQ, quotation and agent evaluation.',
        'ProcurementManager' =>
          'Review recommendations at the human approval boundary and oversee purchasing.',
        'QualityInspector' =>
          'Inspect received materials and manage corrective actions for open NCRs.',
        'SiteManager' => 'Monitor procurement approvals, site delivery and operational exceptions.',
        'Administrator' =>
          'Monitor system-wide operational activity and access the administration workspace.',
        _ => 'Your role-aware operations overview.',
      };

  String get _primaryRole =>
      (_dashboard?['primaryRole'] as String?) ??
      (widget.roles.isNotEmpty ? widget.roles.first : 'TeamMember');

  @override
  Widget build(BuildContext context) {
    final role = _primaryRole;
    final metrics = (_dashboard?['metrics'] as List<dynamic>?) ?? const [];
    final tasks = (_dashboard?['tasks'] as List<dynamic>?) ?? const [];
    final alerts = (_dashboard?['alerts'] as List<dynamic>?) ?? const [];
    final activity = (_dashboard?['activity'] as List<dynamic>?) ?? const [];
    final metricMap = <String, Map<String, dynamic>>{
      for (final metric in metrics.cast<Map<String, dynamic>>())
        metric['key'].toString(): metric,
    };

    if (_loading) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }
    if (_error != null) {
      return Scaffold(body: buildwise.ErrorWidget(message: _error!, onRetry: _load));
    }

    final children = <Widget>[
      Text(_description(role), style: Theme.of(context).textTheme.bodyMedium),
      const SizedBox(height: 16),
      // A metric key the backend did not return still renders (as zero) so the
      // layout stays stable and a missing key is visible rather than silent.
      ..._metricKeys(role).map((key) {
        final metric = metricMap[key];
        final suffix = metric?['suffix'];
        return Padding(
          padding: const EdgeInsets.only(bottom: 10),
          child: AppCard(
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(child: Text(metric?['label']?.toString() ?? key)),
                Text(
                  '${_formatNumber(metric?['value'])}'
                  '${suffix != null && suffix != '' ? ' $suffix' : ''}',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
              ],
            ),
          ),
        );
      }),
      const SizedBox(height: 10),
      Text('Quick actions', style: Theme.of(context).textTheme.titleMedium),
      const SizedBox(height: 8),
    ];

    if (tasks.isEmpty) {
      children.add(const AppCard(child: Text('No actions available for this role.')));
    } else {
      for (final task in tasks.cast<Map<String, dynamic>>()) {
        children.add(_InfoCard(
          title: task['title']?.toString() ?? '',
          detail: task['description']?.toString() ?? '',
        ));
      }
    }

    children
      ..add(const SizedBox(height: 12))
      ..add(Text('Attention required', style: Theme.of(context).textTheme.titleMedium))
      ..add(const SizedBox(height: 8));

    if (alerts.isEmpty) {
      children.add(const AppCard(child: Text('No active alerts for this role.')));
    } else {
      for (final alert in alerts.cast<Map<String, dynamic>>()) {
        children.add(_InfoCard(
          title: alert['title']?.toString() ?? '',
          detail: alert['detail']?.toString() ?? '',
        ));
      }
    }

    children
      ..add(const SizedBox(height: 12))
      ..add(Text('Recent activity', style: Theme.of(context).textTheme.titleMedium))
      ..add(const SizedBox(height: 8));

    if (activity.isEmpty) {
      children.add(const AppCard(child: Text('No recent activity recorded.')));
    } else {
      for (final item in activity.cast<Map<String, dynamic>>()) {
        children.add(_InfoCard(
          title: item['title']?.toString() ?? '',
          detail: item['detail']?.toString() ?? '',
          footer: item['createdAt']?.toString(),
        ));
      }
    }

    return Scaffold(
      appBar: AppBar(
        title: Text(_title(role)),
        actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
      ),
      body: RefreshIndicator(
        onRefresh: _load,
        child: ListView(padding: const EdgeInsets.all(16), children: children),
      ),
    );
  }


  /// Thousands-separated integer, matching the web's `toLocaleString()`.
  static String _formatNumber(Object? value) {
    final number = (value as num?)?.toDouble() ?? 0;
    final isWhole = number == number.roundToDouble();
    final digits = isWhole ? number.toInt().toString() : number.toString();
    final parts = digits.split('.');
    final buffer = StringBuffer();
    for (var i = 0; i < parts[0].length; i++) {
      if (i > 0 && (parts[0].length - i) % 3 == 0) buffer.write(',');
      buffer.write(parts[0][i]);
    }
    return parts.length > 1 ? '$buffer.${parts[1]}' : buffer.toString();
  }
}

/// A title/detail block, used for quick actions, alerts and activity.
class _InfoCard extends StatelessWidget {
  const _InfoCard({required this.title, required this.detail, this.footer});

  final String title;
  final String detail;
  final String? footer;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: AppCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: const TextStyle(fontWeight: FontWeight.w600)),
              const SizedBox(height: 4),
              Text(detail, style: Theme.of(context).textTheme.bodySmall),
              if (footer != null) ...[
                const SizedBox(height: 4),
                Text(footer!, style: Theme.of(context).textTheme.bodySmall),
              ],
            ],
          ),
        ),
      );
}

