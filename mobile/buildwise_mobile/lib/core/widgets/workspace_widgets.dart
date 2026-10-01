import 'dart:async';

import 'package:flutter/material.dart' hide ErrorWidget;
import 'package:http/http.dart' as http;

import '../api/api_exception.dart';
import '../theme/app_colors.dart';
import 'widgets.dart';

class WorkspaceSession extends InheritedWidget {
  const WorkspaceSession({
    super.key,
    required this.onSignOut,
    required super.child,
  });
  final VoidCallback onSignOut;
  @override
  bool updateShouldNotify(WorkspaceSession oldWidget) =>
      oldWidget.onSignOut != onSignOut;
}

class WorkspaceAppBar extends StatelessWidget implements PreferredSizeWidget {
  const WorkspaceAppBar({
    super.key,
    required this.title,
    this.subtitle,
    this.actions = const [],
    this.automaticallyImplyLeading = true,
  });
  final Widget title;
  final String? subtitle;
  final List<Widget> actions;
  final bool automaticallyImplyLeading;
  @override
  Size get preferredSize => const Size.fromHeight(65);
  @override
  Widget build(BuildContext context) {
    final session = context
        .dependOnInheritedWidgetOfExactType<WorkspaceSession>();
    return AppBar(
      automaticallyImplyLeading: automaticallyImplyLeading,
      title: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          title,
          if (subtitle != null)
            Text(
              subtitle!,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w400,
                color: AppColors.textMuted,
              ),
            ),
        ],
      ),
      shape: const Border(bottom: BorderSide(color: AppColors.border)),
      actions: [
        ...actions,
        if (session != null && !Navigator.of(context).canPop())
          IconButton(
            tooltip: 'Sign out',
            onPressed: session.onSignOut,
            icon: const Icon(Icons.logout, size: 20),
          ),
      ],
    );
  }
}

String reference(String prefix, int id) =>
    '$prefix-${id.toString().padLeft(4, '0')}';
String displayDate(Object? value) {
  final date = DateTime.tryParse(value?.toString() ?? '');
  if (date == null) return 'Not recorded';
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  return '${date.day} ${months[date.month - 1]} ${date.year}';
}

String money(num value) =>
    'LKR ${value.toStringAsFixed(2).replaceAllMapped(RegExp(r'(\d)(?=(\d{3})+\.)'), (m) => '${m[1]},')}';
String statusLabel(String value) =>
    value.replaceAllMapped(RegExp(r'([a-z])([A-Z])'), (m) => '${m[1]} ${m[2]}');
StatusTone statusTone(String value) => switch (value) {
  'Approved' ||
  'Active' ||
  'Confirmed' ||
  'Completed' ||
  'Accepted' ||
  'Received' => StatusTone.success,
  'Rejected' || 'Failed' || 'DiscrepancyReported' => StatusTone.danger,
  'AwaitingApproval' ||
  'PendingApproval' ||
  'RevisionRequested' ||
  'PartiallyAccepted' => StatusTone.warning,
  'Running' || 'InProgress' || 'Submitted' || 'Scheduled' => StatusTone.info,
  _ => StatusTone.neutral,
};

class FieldRow extends StatelessWidget {
  const FieldRow(this.label, this.value, {super.key});
  final String label, value;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 3),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          flex: 4,
          child: Text(label, style: Theme.of(context).textTheme.bodySmall),
        ),
        const SizedBox(width: 8),
        Expanded(
          flex: 6,
          child: Text(
            value,
            textAlign: TextAlign.right,
            style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
          ),
        ),
      ],
    ),
  );
}

class RecordCard extends StatelessWidget {
  const RecordCard({
    super.key,
    required this.title,
    required this.children,
    this.status,
    this.onTap,
    this.action,
  });
  final String title;
  final String? status, action;
  final List<Widget> children;
  final VoidCallback? onTap;
  @override
  Widget build(BuildContext context) => AppCard(
    onTap: onTap,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Wrap(
          alignment: WrapAlignment.spaceBetween,
          spacing: 8,
          runSpacing: 8,
          children: [
            Text(title, style: Theme.of(context).textTheme.titleMedium),
            if (status != null)
              StatusChip(
                label: statusLabel(status!),
                tone: statusTone(status!),
              ),
          ],
        ),
        const SizedBox(height: 10),
        ...children,
        if (action != null) ...[
          const Divider(height: 20),
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(onPressed: onTap, child: Text('$action →')),
          ),
        ],
      ],
    ),
  );
}

class AdvisoryBanner extends StatelessWidget {
  const AdvisoryBanner(
    this.message, {
    super.key,
    this.title = 'Advisory Analysis',
  });
  final String title, message;
  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: const Color(0xFFFEF3C7),
      border: Border.all(color: AppColors.accent),
      borderRadius: BorderRadius.circular(6),
    ),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Icon(
          Icons.warning_amber_rounded,
          color: AppColors.accent,
          size: 18,
        ),
        const SizedBox(width: 8),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: const TextStyle(
                  color: AppColors.accent,
                  fontWeight: FontWeight.w700,
                  fontSize: 13,
                ),
              ),
              const SizedBox(height: 4),
              Text(message, style: const TextStyle(fontSize: 12)),
            ],
          ),
        ),
      ],
    ),
  );
}

class FilterChips extends StatelessWidget {
  const FilterChips({
    super.key,
    required this.values,
    required this.selected,
    required this.onChanged,
  });
  final List<String> values;
  final String selected;
  final ValueChanged<String> onChanged;
  @override
  Widget build(BuildContext context) => Wrap(
    spacing: 8,
    runSpacing: 4,
    children: values
        .map(
          (value) => ChoiceChip(
            label: Text(statusLabel(value)),
            selected: value == selected,
            showCheckmark: false,
            selectedColor: AppColors.accent,
            backgroundColor: Colors.white,
            labelStyle: TextStyle(
              fontSize: 12,
              color: value == selected ? Colors.white : AppColors.textMuted,
            ),
            onSelected: (_) => onChanged(value),
          ),
        )
        .toList(),
  );
}

/// Keeps refresh, resume, loading and errors consistent without a new state framework.
class ApiView<T> extends StatefulWidget {
  const ApiView({super.key, required this.load, required this.builder});
  final Future<T> Function() load;
  final Widget Function(BuildContext context, T data, VoidCallback refresh)
  builder;
  @override
  State<ApiView<T>> createState() => _ApiViewState<T>();
}

class _ApiViewState<T> extends State<ApiView<T>> with WidgetsBindingObserver {
  late Future<T> _future;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _future = widget.load();
  }

  void refresh() {
    if (mounted) {
      setState(() {
        _future = widget.load();
      });
    }
  }

  @override
  void didUpdateWidget(covariant ApiView<T> oldWidget) {
    super.didUpdateWidget(oldWidget);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) refresh();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<T>(
    future: _future,
    builder: (context, snapshot) {
      if (snapshot.connectionState != ConnectionState.done) {
        return const LoadingWidget();
      }
      if (snapshot.hasError) {
        return ApiErrorState(error: snapshot.error!, onRetry: refresh);
      }
      return RefreshIndicator(
        onRefresh: () async {
          refresh();
          try {
            await _future;
          } catch (_) {
            /* FutureBuilder presents the error. */
          }
        },
        child: widget.builder(context, snapshot.data as T, refresh),
      );
    },
  );
}

class ApiErrorState extends StatelessWidget {
  const ApiErrorState({super.key, required this.error, required this.onRetry});
  final Object error;
  final VoidCallback onRetry;
  @override
  Widget build(BuildContext context) {
    final code = error is ApiException
        ? (error as ApiException).statusCode
        : null;
    final offline = error is http.ClientException || error is TimeoutException;
    return ErrorWidget(
      title: code == 401
          ? 'Session Expired'
          : code == 403
          ? 'Access Denied'
          : offline
          ? 'No Connection'
          : 'Something went wrong',
      message: offline
          ? 'Check your connection and try again.'
          : error.toString().replaceFirst('Exception: ', ''),
      onRetry: onRetry,
    );
  }
}
