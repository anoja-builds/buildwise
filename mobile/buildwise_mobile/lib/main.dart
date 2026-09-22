import 'package:flutter/material.dart';

import 'common/screens/screens.dart';
import 'core/theme/app_theme.dart';
import 'features/quality/screens/pending_inspections_screen.dart';

void main() => runApp(const BuildWiseApp());

class BuildWiseApp extends StatelessWidget {
  const BuildWiseApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'BuildWise',
    debugShowCheckedModeBanner: false,
    theme: AppTheme.light,
    home: const CommonUiPreview(),
  );
}

class CommonUiPreview extends StatefulWidget {
  const CommonUiPreview({super.key});
  @override
  State<CommonUiPreview> createState() => _CommonUiPreviewState();
}

class _CommonUiPreviewState extends State<CommonUiPreview> {
  int selectedIndex = 0;

  static const screens = [
    MobileHomeBaseScreen(),
    MobileListBaseScreen(),
    MobileFormBaseScreen(),
    MobileDetailBaseScreen(),
    MobileUiStatesScreen(),
  ];

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('BuildWise'),
      actions: [
        TextButton.icon(
          style: TextButton.styleFrom(
            foregroundColor: Theme.of(context).colorScheme.onPrimary,
          ),
          icon: const Icon(Icons.fact_check_outlined),
          label: const Text('Quality'),
          onPressed: () => Navigator.of(context).push(
            MaterialPageRoute<void>(
              builder: (_) => const PendingInspectionsScreen(),
            ),
          ),
        ),
        IconButton(
          onPressed: () {},
          tooltip: 'Notifications',
          icon: const Icon(Icons.notifications_none),
        ),
      ],
    ),
    body: IndexedStack(index: selectedIndex, children: screens),
    bottomNavigationBar: NavigationBar(
      selectedIndex: selectedIndex,
      onDestinationSelected: (index) => setState(() => selectedIndex = index),
      destinations: const [
        NavigationDestination(
          icon: Icon(Icons.home_outlined),
          selectedIcon: Icon(Icons.home),
          label: 'Home',
        ),
        NavigationDestination(
          icon: Icon(Icons.list_alt_outlined),
          selectedIcon: Icon(Icons.list_alt),
          label: 'List',
        ),
        NavigationDestination(
          icon: Icon(Icons.edit_note_outlined),
          selectedIcon: Icon(Icons.edit_note),
          label: 'Form',
        ),
        NavigationDestination(
          icon: Icon(Icons.description_outlined),
          selectedIcon: Icon(Icons.description),
          label: 'Detail',
        ),
        NavigationDestination(
          icon: Icon(Icons.widgets_outlined),
          selectedIcon: Icon(Icons.widgets),
          label: 'States',
        ),
      ],
    ),
  );
}
