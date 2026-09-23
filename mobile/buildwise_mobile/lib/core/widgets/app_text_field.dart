import 'package:flutter/material.dart';

class AppTextField extends StatelessWidget {
  const AppTextField({
    super.key,
    required this.label,
    this.hint,
    this.controller,
    this.keyboardType,
    this.maxLines = 1,
    this.errorText,
    this.readOnly = false,
    this.suffixIcon,
    this.onTap,
    this.obscureText = false,
    this.onChanged,
  });
  final String label;
  final String? hint;
  final TextEditingController? controller;
  final TextInputType? keyboardType;
  final int maxLines;
  final String? errorText;
  final bool readOnly;
  final Widget? suffixIcon;
  final VoidCallback? onTap;
  final bool obscureText;
  final ValueChanged<String>? onChanged;

  @override
  Widget build(BuildContext context) => TextField(
    controller: controller,
    keyboardType: keyboardType,
    maxLines: obscureText ? 1 : maxLines,
    readOnly: readOnly,
    onTap: onTap,
    obscureText: obscureText,
    onChanged: onChanged,
    decoration: InputDecoration(
      labelText: label,
      hintText: hint,
      errorText: errorText,
      suffixIcon: suffixIcon,
    ),
  );
}
