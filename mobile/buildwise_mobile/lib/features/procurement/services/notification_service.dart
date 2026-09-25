import 'package:flutter_local_notifications/flutter_local_notifications.dart';

/// Device feature: local push-style notifications for procurement status
/// changes (spec §9.2 — "recommendation awaiting approval" / "purchase
/// order created"). Uses on-device notifications rather than remote FCM so
/// it works without a Firebase project being configured for this build;
/// swapping in `firebase_messaging` later only changes how a notification
/// is triggered, not this display layer.
class NotificationService {
  NotificationService._();
  static final NotificationService instance = NotificationService._();

  final FlutterLocalNotificationsPlugin _plugin = FlutterLocalNotificationsPlugin();
  bool _initialized = false;

  Future<void> initialize() async {
    if (_initialized) return;

    const androidInit = AndroidInitializationSettings('@mipmap/ic_launcher');
    const iosInit = DarwinInitializationSettings(
      requestAlertPermission: true,
      requestBadgePermission: true,
      requestSoundPermission: true,
    );
    const settings = InitializationSettings(android: androidInit, iOS: iosInit);

    await _plugin.initialize(settings);

    final androidPlugin = _plugin
        .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();
    await androidPlugin?.requestNotificationsPermission();

    _initialized = true;
  }

  Future<void> showQualityUpdate({
    required int id,
    required String title,
    required String body,
  }) async {
    await initialize();
    const androidDetails = AndroidNotificationDetails(
      'quality_updates',
      'Quality updates',
      channelDescription: 'Notifies when inspection and NCR results are available.',
      importance: Importance.high,
      priority: Priority.high,
    );
    const details = NotificationDetails(android: androidDetails, iOS: DarwinNotificationDetails());
    await _plugin.show(id, title, body, details);
  }

  Future<void> showProcurementUpdate({
    required int id,
    required String title,
    required String body,
  }) async {
    await initialize();

    const androidDetails = AndroidNotificationDetails(
      'procurement_updates',
      'Procurement updates',
      channelDescription: 'Notifies when a material request\'s procurement status changes.',
      importance: Importance.high,
      priority: Priority.high,
    );
    const details = NotificationDetails(
      android: androidDetails,
      iOS: DarwinNotificationDetails(),
    );

    await _plugin.show(id, title, body, details);
  }
}
