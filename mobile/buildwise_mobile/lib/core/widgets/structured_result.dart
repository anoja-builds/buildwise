import 'dart:convert';

import 'package:flutter/material.dart';

/// Displays the stored public result of an agent, including validation failures.
class StructuredResult extends StatelessWidget {
  const StructuredResult({super.key, required this.label, this.value});

  final String label;
  final Object? value;

  static String pretty(Object? value) {
    if (value == null || value == '') return 'Not recorded';
    try {
      return const JsonEncoder.withIndent('  ')
          .convert(value is String ? jsonDecode(value) : value);
    } catch (_) {
      return value.toString();
    }
  }

  @override
  Widget build(BuildContext context) => ExpansionTile(
    title: Text(label),
    children: [
      Padding(
        padding: const EdgeInsets.all(12),
        child: Align(
          alignment: Alignment.centerLeft,
          child: SelectableText(pretty(value)),
        ),
      ),
    ],
  );
}
