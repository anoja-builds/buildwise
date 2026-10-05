import 'package:flutter/material.dart';

class AppDropdown extends StatelessWidget {
  const AppDropdown({
    super.key,
    required this.label,
    required this.items,
    this.value,
    this.itemLabels,
    this.onChanged,
  });
  final String label;
  final List<String> items;
  final List<String>? itemLabels;
  final String? value;
  final ValueChanged<String?>? onChanged;

  @override
  Widget build(BuildContext context) => DropdownButtonFormField<String>(
    initialValue: value,
    decoration: InputDecoration(labelText: label),
    items: items
        .asMap().entries.map((entry) => DropdownMenuItem(value: entry.value, child: Text(itemLabels?[entry.key] ?? entry.value)))
        .toList(),
    onChanged: onChanged,
  );
}
