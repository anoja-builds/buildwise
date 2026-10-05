import 'package:flutter/material.dart';

import 'dart:async';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/field_format.dart';
import '../../../core/widgets/field_messages.dart';
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';
import '../widgets/agent_analysis_panels.dart';
import 'material_request_review_screen.dart';

class MaterialRequestsScreen extends StatefulWidget {
  const MaterialRequestsScreen({
    super.key,
    this.service,
    this.readOnly = false,
    this.canApprove = false,
    this.autoRefresh = true,
    this.ownRequestsOnly = false,
  });
  final OperationsService? service;
  final bool readOnly;
  final bool canApprove;
  final bool autoRefresh;
  final bool ownRequestsOnly;

  @override
  State<MaterialRequestsScreen> createState() => _MaterialRequestsScreenState();
}

class _MaterialRequestsScreenState extends State<MaterialRequestsScreen>
    with WidgetsBindingObserver {
  final _service = OperationsService();
  List<Map<String, dynamic>> _requests = const [];
  bool _loading = true;
  String? _error;
  Timer? _refreshTimer;
  String _query = '';
  String _status = 'all';
  String _priorityFilter = 'all';
  List<Map<String, dynamic>> get _visibleRequests => _requests.where((request) {
    if (_status != 'all' && request['status'] != _status) return false;
    if (_priorityFilter != 'all' && request['priority'] != _priorityFilter) {
      return false;
    }
    final query = _query.trim().toLowerCase();
    return query.isEmpty ||
        [
          request['id'],
          request['projectName'],
          request['reason'],
          FieldFormat.materialRequestLabel(request),
        ].any(
          (value) => (value?.toString() ?? '').toLowerCase().contains(query),
        );
  }).toList();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (widget.autoRefresh) {
      _refreshTimer = Timer.periodic(const Duration(seconds: 30), (_) {
        if (WidgetsBinding.instance.lifecycleState ==
            AppLifecycleState.resumed) {
          _load(background: true);
        }
      });
    }
    _load();
  }

  @override
  void dispose() {
    _refreshTimer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (widget.autoRefresh && state == AppLifecycleState.resumed) {
      _load(background: true);
    }
  }

  Future<void> _load({bool background = false}) async {
    if (!mounted) return;
    if (!background) {
      setState(() {
        _loading = true;
        _error = null;
      });
    }
    try {
      final requests = widget.readOnly && !widget.ownRequestsOnly
          ? await (widget.service ?? _service).listRequests(status: 'all')
          : await (widget.service ?? _service).listMyRequests();
      if (mounted) {
        setState(() {
          _requests = requests;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted && !background) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
    } finally {
      if (mounted && !background) setState(() => _loading = false);
    }
  }

  Future<void> _create() async {
    final created = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => Scaffold(
          appBar: AppBar(title: const Text('Create Material Request')),
          body: _CreateRequestSheet(service: widget.service ?? _service),
        ),
      ),
    );
    if (created == true) _load();
  }

  /// Runs the RequestAnalysisAgent (:8002) over one material request to surface
  /// planning risks. Advisory only: it flags urgency, bulk and large-quantity
  /// risk but never approves, rejects or changes the request.
  Future<void> _analyzeRequest(Map<String, dynamic> request) async {
    final id = (request['id'] as num).toInt();
    await showAiAnalysisSheet(
      context,
      title: 'AI Request Analysis - MR-$id',
      run: () => (widget.service ?? _service).analyzeRequest(id),
      builder: buildRequestAnalysisPanel,
    );
  }

  /// Approve or reject a material request.
  ///
  /// This is the authoritative human decision on the request. The
  /// RequestAnalysisAgent flags risk but never decides, so the request only
  /// becomes Approved when an authorized manager records that decision here.
  Future<void> _decide(
    Map<String, dynamic> request, {
    required String decision,
  }) async {
    final id = (request['id'] as num).toInt();
    final comments = await showDialog<String>(
      context: context,
      builder: (_) => _DecisionDialog(decision: decision),
    );
    if (comments == null || !mounted) return;
    try {
      await (widget.service ?? _service).decideMaterialRequest(
        id,
        decision: decision,
        comments: comments,
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('Request #$id $decision.')));
      _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(e.toString().replaceFirst('Exception: ', ''))),
      );
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Material Requests'),
      actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
      bottom: PreferredSize(
        preferredSize: const Size.fromHeight(140),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            children: [
              AppTextField(
                label: 'Search requests',
                hint: 'Request id, project, material or reason',
                onChanged: (value) => setState(() => _query = value),
              ),
              const SizedBox(height: 10),
              Row(
                children: [
                  Expanded(
                    child: AppDropdown(
                      label: 'Status',
                      value: _status,
                      items: const [
                        'all',
                        'PendingApproval',
                        'UnderReview',
                        'AwaitingProcurementApproval',
                        'Approved',
                        'Rejected',
                        'RevisionRequested',
                        'Fulfilled',
                      ],
                      itemLabels: const [
                        'All statuses',
                        'Pending Approval',
                        'Under Review',
                        'Awaiting Procurement Approval',
                        'Approved',
                        'Rejected',
                        'Revision Requested',
                        'Fulfilled',
                      ],
                      onChanged: (value) =>
                          setState(() => _status = value ?? 'all'),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: AppDropdown(
                      label: 'Priority',
                      value: _priorityFilter,
                      items: const ['all', 'Low', 'Normal', 'High', 'Urgent'],
                      itemLabels: const [
                        'All priorities',
                        'Low',
                        'Normal',
                        'High',
                        'Urgent',
                      ],
                      onChanged: (value) =>
                          setState(() => _priorityFilter = value ?? 'all'),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    ),
    floatingActionButton: widget.readOnly
        ? null
        : FloatingActionButton.extended(
            onPressed: _create,
            icon: const Icon(Icons.add),
            label: const Text('Create Request'),
          ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
        ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
        : _visibleRequests.isEmpty
        ? const EmptyStateWidget(
            title: 'No material requests',
            message: 'Create a request for an active project and material.',
          )
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView.separated(
              padding: const EdgeInsets.all(16),
              itemCount: _visibleRequests.length,
              separatorBuilder: (_, _) => const SizedBox(height: 10),
              itemBuilder: (_, index) {
                final request = _visibleRequests[index];
                return AppCard(
                  onTap: () async {
                    try {
                      final detail = await (widget.service ?? _service)
                          .getMaterialRequestDetail(
                            (request['id'] as num).toInt(),
                          );
                      if (!context.mounted) return;
                      await Navigator.of(context).push(
                        MaterialPageRoute<void>(
                          builder: (_) => MaterialRequestReviewScreen(
                            detail: detail,
                            service: widget.service ?? _service,
                            canApprove: widget.canApprove,
                            onDecided: _load,
                          ),
                        ),
                      );
                    } catch (e) {
                      if (context.mounted) {
                        ScaffoldMessenger.of(context).showSnackBar(
                          SnackBar(
                            content: Text(FieldMessages.friendly(e.toString())),
                          ),
                        );
                      }
                    }
                  },
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      // The material is what the row is *about*, so it is
                      // the headline. The API returns the line-ordered
                      // `materialNames`; `itemCount` alone left the list
                      // reading as bare "Request #77 · 1 item(s)", which
                      // tells a site user nothing they can act on.
                      Text(
                        FieldFormat.materialRequestLabel(request),
                        style: const TextStyle(fontWeight: FontWeight.bold),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        request['projectName']?.toString() ?? 'Project',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      const SizedBox(height: 6),
                      Text('Request #${request['id']}'),
                      const SizedBox(height: 6),
                      StatusChip(
                        label: request['status']?.toString() ?? 'Unknown',
                        tone: _statusTone(request['status']?.toString()),
                      ),
                      const SizedBox(height: 12),
                      if (widget.canApprove)
                        AppButton(
                          label: 'Run AI Analysis',
                          expand: true,
                          variant: AppButtonVariant.secondary,
                          onPressed: () => _analyzeRequest(request),
                        ),
                      // Approve/Reject is offered only on a request that
                      // is actually awaiting a decision, so the control
                      // never appears on an already-decided request.
                      if (widget.canApprove &&
                          [
                            'PendingApproval',
                            'UnderReview',
                            'AwaitingProcurementApproval',
                          ].contains(request['status'])) ...[
                        const SizedBox(height: 8),
                        Row(
                          children: [
                            Expanded(
                              child: AppButton(
                                label: 'Approve',
                                expand: true,
                                onPressed: () =>
                                    _decide(request, decision: 'Approved'),
                              ),
                            ),
                            const SizedBox(width: 8),
                            Expanded(
                              child: AppButton(
                                label: 'Reject',
                                expand: true,
                                variant: AppButtonVariant.danger,
                                onPressed: () =>
                                    _decide(request, decision: 'Rejected'),
                              ),
                            ),
                          ],
                        ),
                      ],
                    ],
                  ),
                );
              },
            ),
          ),
  );
}

/// Collects the reviewer's comments before recording an approval decision.
class _DecisionDialog extends StatefulWidget {
  const _DecisionDialog({required this.decision});

  final String decision;

  @override
  State<_DecisionDialog> createState() => _DecisionDialogState();
}

class _DecisionDialogState extends State<_DecisionDialog> {
  final _comments = TextEditingController();

  @override
  void dispose() {
    _comments.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text('${widget.decision} this request?'),
    content: AppTextField(
      label: 'Comments',
      hint: 'Recorded against the request for audit',
      controller: _comments,
      maxLines: 3,
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.of(context).pop(),
        child: const Text('Cancel'),
      ),
      AppButton(
        label: widget.decision,
        variant: widget.decision == 'Rejected'
            ? AppButtonVariant.danger
            : AppButtonVariant.primary,
        onPressed: () => Navigator.of(context).pop(_comments.text.trim()),
      ),
    ],
  );
}

StatusTone _statusTone(String? value) => switch (value) {
  'Approved' => StatusTone.success,
  'Rejected' => StatusTone.danger,
  'PendingApproval' => StatusTone.warning,
  _ => StatusTone.neutral,
};

/// A text box that resolves to a real record.
///
/// The engineer types ordinary text and the matching project or material is
/// offered underneath, which is faster on a phone than opening a drop-down
/// and scrolling it. Only a suggestion can be chosen — the submitted value is
/// always the record's id, because the API keys requests to existing rows.
class TypeAheadField extends StatelessWidget {
  const TypeAheadField({
    super.key,
    required this.label,
    required this.controller,
    required this.options,
    required this.isOpen,
    required this.onToggle,
    required this.onChanged,
    required this.onSelected,
    this.selectedId,
    this.fieldKey,
    this.allowFreeText = false,
    this.hint,
  });

  final String label;
  final TextEditingController controller;
  final List<Map<String, dynamic>> options;
  final int? selectedId;

  /// Whether the suggestion list is showing. Held in the form's state so the
  /// list closes on selection and when the engineer moves to another field.
  final bool isOpen;
  final VoidCallback onToggle;
  final ValueChanged<String> onChanged;
  final ValueChanged<Map<String, dynamic>> onSelected;

  /// Identifies the field, both to address it in tests and to tell the
  /// project and material boxes apart.
  final Key? fieldKey;

  /// When true, text that matches no record is kept and submitted as typed.
  /// Project and material use this so new names can be resolved by the API.
  final bool allowFreeText;
  final String? hint;

  static const int _maxSuggestions = 6;

  List<Map<String, dynamic>> get _matches {
    final query = controller.text.trim().toLowerCase();
    if (query.isEmpty) return options;
    return options
        .where(
          (option) =>
              (option['name']?.toString() ?? '').toLowerCase().contains(query),
        )
        .toList();
  }

  @override
  Widget build(BuildContext context) {
    final matches = isOpen
        ? _matches.take(_maxSuggestions).toList()
        : const <Map<String, dynamic>>[];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        TextField(
          key: fieldKey,
          controller: controller,
          onChanged: onChanged,
          onTap: onToggle,
          textInputAction: TextInputAction.search,
          decoration: InputDecoration(
            labelText: label,
            hintText: hint,
            helperText: isOpen ? 'Tap a suggestion to choose it.' : null,
            border: const OutlineInputBorder(),
            suffixIcon: IconButton(
              icon: Icon(isOpen ? Icons.arrow_drop_up : Icons.arrow_drop_down),
              tooltip: isOpen ? 'Hide suggestions' : 'Show suggestions',
              onPressed: onToggle,
            ),
          ),
        ),
        if (matches.isNotEmpty)
          Container(
            width: double.infinity,
            margin: const EdgeInsets.only(top: 4),
            decoration: BoxDecoration(
              border: Border.all(color: const Color(0xFFCBD5E1)),
              borderRadius: BorderRadius.circular(8),
            ),
            child: Column(
              children: [
                for (final option in matches)
                  ListTile(
                    dense: true,
                    contentPadding: const EdgeInsets.symmetric(horizontal: 12),
                    title: Text(
                      option['name']?.toString() ?? '',
                      overflow: TextOverflow.ellipsis,
                    ),
                    trailing: option['id'] == selectedId
                        ? const Icon(Icons.check, size: 18)
                        : null,
                    onTap: () => onSelected(option),
                  ),
              ],
            ),
          ),
        if (isOpen && _matches.isEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 6, left: 4),
            child: Text(
              allowFreeText
                  ? 'No match — this will be used as a new ${label.replaceAll('*', '').trim().toLowerCase()} name.'
                  : 'No match. Start typing to choose from the list.',
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: const Color(0xFF64748B)),
            ),
          ),
      ],
    );
  }
}

class _CreateRequestSheet extends StatefulWidget {
  const _CreateRequestSheet({required this.service});
  final OperationsService service;

  @override
  State<_CreateRequestSheet> createState() => _CreateRequestSheetState();
}

class _CreateRequestSheetState extends State<_CreateRequestSheet> {
  final _quantity = TextEditingController();
  final _reason = TextEditingController();
  final _siteNotes = TextEditingController();

  /// Text typed into the project and material boxes. They hold what the engineer
  /// has typed. A complete catalogue name or a tapped suggestion resolves
  /// [_materialId]; editing it to unmatched text clears that selection.
  final _projectSearch = TextEditingController();
  final _materialSearch = TextEditingController();
  bool _projectOpen = false;
  bool _materialOpen = false;
  final _specification = TextEditingController();
  final _unitInput = TextEditingController();
  DateTime _requestDate = DateUtils.dateOnly(DateTime.now());

  /// Populated from `/projects` and `/materials`. This form previously submitted
  /// a hard-coded `projectId: 1, materialId: 1`, which silently filed every
  /// request against the first seeded row - wrong project, wrong material, and
  /// no way for the engineer to notice on the form.
  List<Map<String, dynamic>> _projects = const [];
  List<Map<String, dynamic>> _materials = const [];
  int? _projectId;
  int? _materialId;

  /// Unit comes from the chosen material's master record, so the engineer does
  /// not have to remember whether cement is counted in bags or tonnes.
  String _unit = '';

  DateTime _requiredDate = DateTime.now().add(const Duration(days: 5));
  String _priority = 'Normal';

  bool _loadingOptions = true;
  bool _submitting = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadOptions();
  }

  Future<void> _loadOptions() async {
    try {
      final results = await Future.wait([
        widget.service.listProjects(),
        widget.service.listMaterials(),
      ]);
      if (!mounted) return;
      setState(() {
        _projects = results[0];
        _materials = results[1];
        // Preselect only when there is exactly one candidate; otherwise the
        // engineer must choose deliberately.
        _projectId = _projects.length == 1
            ? _projects.first['id'] as int?
            : null;
        if (_projects.length == 1) {
          _projectSearch.text = _projects.first['name']?.toString() ?? '';
        }
        if (_materials.length == 1) _selectMaterial(_materials.first);
        _loadingOptions = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loadingOptions = false;
        _error = FieldMessages.friendly(error.toString());
      });
    }
  }

  void _selectMaterial(Map<String, dynamic> material) {
    setState(() {
      _materialId = material['id'] as int?;
      _unit = material['unit']?.toString() ?? '';
      _unitInput.text = _unit;
      _materialSearch.text = material['name']?.toString() ?? '';
      _materialOpen = false;
    });
  }

  @override
  void dispose() {
    _quantity.dispose();
    _reason.dispose();
    _siteNotes.dispose();
    _specification.dispose();
    _unitInput.dispose();
    _projectSearch.dispose();
    _materialSearch.dispose();
    super.dispose();
  }

  /// Client-side validation, for immediate feedback only.
  ///
  /// This is UX. The backend re-validates every field and stays authoritative -
  /// a bypassed check must still fail server-side.
  String? _validate() {
    // The project is typed text: any non-empty name is accepted and the API
    // resolves or creates it. Material names are resolved the same way.
    if (_projectSearch.text.trim().isEmpty) return 'Enter a project name.';

    final typedMaterial = _materialSearch.text.trim().toLowerCase();
    if (typedMaterial.isEmpty) return 'Enter a material.';
    if (_materialSearch.text.trim().length > 200) {
      return 'Enter a material name of 200 characters or less.';
    }

    final rawQuantity = _quantity.text.trim();
    if (rawQuantity.isEmpty) return 'Enter a quantity.';
    final quantity = double.tryParse(rawQuantity);
    if (quantity == null || !RegExp(r'^\d+(\.\d+)?$').hasMatch(rawQuantity)) {
      return 'Quantity must be a number (digits only).';
    }
    if (!quantity.isFinite || quantity <= 0) {
      return 'Quantity must be a positive number greater than 0.';
    }

    final unit = _unitInput.text.trim();
    if ([
          'bags',
          'bag',
          'pieces',
          'piece',
          'nos',
          'sheets',
          'sheet',
          'units',
          'boxes',
        ].contains(unit.toLowerCase()) &&
        quantity != quantity.roundToDouble()) {
      return "Decimal quantities are not allowed for '$unit'. Please specify a whole integer quantity (e.g. 10 instead of 10.5).";
    }
    if (!_requiredDate.isAfter(_requestDate)) {
      return 'Required date must be after the request date.';
    }
    if (_requiredDate.isBefore(
      DateUtils.dateOnly(DateTime.now()).add(const Duration(days: 3)),
    )) {
      return 'Required date must be at least 3 days in the future.';
    }
    if (_reason.text.trim().isEmpty) {
      return 'Enter a justification for this request.';
    }
    if (_siteNotes.text.trim().isEmpty) {
      return 'Enter site notes for this request.';
    }
    return null;
  }

  Future<void> _submit() async {
    final problem = _validate();
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }

    setState(() {
      _submitting = true;
      _error = null;
    });

    try {
      // The id is sent when the typed name still matches a project BuildWise
      // knows; otherwise the name goes alone and the API resolves it. This
      // keeps the exact reference when we have one and still allows a site that
      // is new to the system.
      final typedProject = _projectSearch.text.trim().toLowerCase();
      final matchedProject = _projects
          .where(
            (p) =>
                (p['name']?.toString() ?? '').trim().toLowerCase() ==
                typedProject,
          )
          .firstOrNull;

      await widget.service.createRequest(
        projectId: matchedProject?['id'] as int? ?? 0,
        projectName: _projectSearch.text.trim(),
        requiredDate: _dateOnly(_requiredDate),
        requestDate: _dateOnly(_requestDate),
        materialId: _materialId ?? 0,
        materialName: _materialSearch.text.trim(),
        quantity: double.parse(_quantity.text.trim()),
        reason: _reason.text.trim(),
        priority: _priority,
        siteNotes: _siteNotes.text.trim().isEmpty
            ? null
            : _siteNotes.text.trim(),
        description: _specification.text.trim().isEmpty
            ? null
            : _specification.text.trim(),
        unit: _unitInput.text.trim().isEmpty ? null : _unitInput.text.trim(),
        itemRequiredDate: _dateOnly(_requiredDate),
      );
      if (mounted) Navigator.pop(context, true);
    } catch (error) {
      if (mounted) {
        setState(() => _error = FieldMessages.friendly(error.toString()));
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  static String _dateOnly(DateTime value) =>
      '${value.year.toString().padLeft(4, '0')}-'
      '${value.month.toString().padLeft(2, '0')}-'
      '${value.day.toString().padLeft(2, '0')}';

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final minimum = DateUtils.dateOnly(now).add(const Duration(days: 3));
    final afterRequest = _requestDate.add(const Duration(days: 1));
    final first = minimum.isAfter(afterRequest) ? minimum : afterRequest;
    final initial = _requiredDate.isBefore(first) ? first : _requiredDate;
    final selected = await showDatePicker(
      context: context,
      firstDate: first,
      lastDate: first.add(const Duration(days: 365)),
      initialDate: initial,
    );
    if (selected != null) setState(() => _requiredDate = selected);
  }

  @override
  Widget build(BuildContext context) {
    if (_loadingOptions) {
      return const Padding(
        padding: EdgeInsets.all(32),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              CircularProgressIndicator(),
              SizedBox(height: 14),
              Text('Loading projects and materials...'),
            ],
          ),
        ),
      );
    }

    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        20,
        20,
        MediaQuery.viewInsetsOf(context).bottom + 20,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Request details',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            // The error sits directly under the title, not at the bottom of the
            // sheet: on a phone the button and the failure message are often in
            // different scroll positions, so an error the user must scroll to
            // find reads as "nothing happened".
            if (_error != null) ...[
              const SizedBox(height: 12),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: const Color(0xFFB91C1C).withValues(alpha: 0.08),
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: const Color(0xFFB91C1C)),
                ),
                child: Row(
                  children: [
                    const Icon(
                      Icons.error_outline,
                      color: Color(0xFFB91C1C),
                      size: 20,
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        _error!,
                        style: const TextStyle(
                          color: Color(0xFFB91C1C),
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],
            const SizedBox(height: 16),
            TypeAheadField(
              label: 'Project *',
              fieldKey: const Key('project-field'),
              allowFreeText: true,
              controller: _projectSearch,
              options: _projects,
              selectedId: _projectId,
              isOpen: _projectOpen,
              onToggle: () => setState(() {
                _projectOpen = !_projectOpen;
                if (_projectOpen) _materialOpen = false;
              }),
              onChanged: (_) => setState(() => _projectOpen = true),
              onSelected: (project) => setState(() {
                _projectId = project['id'] as int?;
                _projectSearch.text = project['name']?.toString() ?? '';
                _projectOpen = false;
              }),
            ),
            const SizedBox(height: 12),
            TypeAheadField(
              label: 'Material *',
              allowFreeText: true,
              hint: 'Type a material name with letters and numbers, e.g. Cement 50kg',
              fieldKey: const Key('material-field'),
              controller: _materialSearch,
              options: _materials,
              selectedId: _materialId,
              isOpen: _materialOpen,
              onToggle: () => setState(() {
                _materialOpen = !_materialOpen;
                if (_materialOpen) _projectOpen = false;
              }),
              onChanged: (text) => setState(() {
                _materialOpen = true;
                final matches = _materials.where(
                  (material) =>
                      material['name']?.toString().trim().toLowerCase() ==
                      text.trim().toLowerCase(),
                );
                _materialId = matches.isEmpty
                    ? null
                    : matches.first['id'] as int?;
                if (matches.isNotEmpty) {
                  _unit = matches.first['unit']?.toString() ?? '';
                  _unitInput.text = _unit;
                }
              }),
              onSelected: _selectMaterial,
            ),
            const SizedBox(height: 12),
            AppTextField(
              label: 'Quantity *',
              controller: _quantity,
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
              ),
              hint: _unit.isEmpty ? 'e.g. 500' : 'e.g. 500 $_unit',
            ),
            const SizedBox(height: 12),
            AppTextField(
              label: 'Unit',
              controller: _unitInput,
              hint: 'For example: bags, kg, pieces',
            ),
            const SizedBox(height: 12),
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Request Date *'),
              trailing: Text(FieldFormat.date(_requestDate)),
              onTap: () async {
                final date = await showDatePicker(
                  context: context,
                  initialDate: _requestDate,
                  firstDate: DateTime(2020),
                  lastDate: DateTime.now().add(const Duration(days: 365)),
                );
                if (date != null && mounted) {
                  setState(() => _requestDate = date);
                }
              },
            ),
            const SizedBox(height: 12),
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Required Date *'),
              trailing: Text(FieldFormat.date(_requiredDate)),
              onTap: _pickDate,
            ),
            const SizedBox(height: 6),
            DropdownButtonFormField<String>(
              initialValue: _priority,
              decoration: const InputDecoration(
                labelText: 'Priority',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(value: 'Low', child: Text('Low')),
                DropdownMenuItem(value: 'Normal', child: Text('Normal')),
                DropdownMenuItem(value: 'High', child: Text('High')),
                DropdownMenuItem(value: 'Urgent', child: Text('Urgent')),
              ],
              onChanged: (value) =>
                  setState(() => _priority = value ?? 'Normal'),
            ),
            const SizedBox(height: 12),
            AppTextField(
              label: 'Justification *',
              controller: _reason,
              maxLines: 2,
              hint: 'Why is this material needed?',
            ),
            const SizedBox(height: 12),
            AppTextField(
              label: 'Site Notes',
              controller: _siteNotes,
              maxLines: 2,
            ),
            const SizedBox(height: 12),
            AppTextField(
              label: 'Material Specification / Description',
              controller: _specification,
              maxLines: 2,
            ),
            const SizedBox(height: 18),
            AppButton(
              // Disabled while in flight so a double tap cannot create two
              // material requests.
              label: _submitting
                  ? FieldMessages.submitting('request')
                  : 'Submit Request',
              expand: true,
              onPressed: _submitting ? null : _submit,
            ),
          ],
        ),
      ),
    );
  }
}
