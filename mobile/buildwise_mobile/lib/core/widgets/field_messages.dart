/// Turns a transport or API failure into something a site user can act on.
///
/// A field phone is on patchy site networks, so the raw
/// `SocketException`/`ClientException`/HTTP status that the HTTP package throws
/// is never shown. This is presentation only — it never changes what was
/// submitted, and never claims success.
class FieldMessages {
  const FieldMessages._();

  /// A short, human explanation with an implied next step.
  static String friendly(String raw) {
    final message = raw
        .replaceFirst('Exception: ', '')
        .replaceFirst('Error: ', '')
        .trim();
    final lowered = message.toLowerCase();

    if (lowered.contains('socket') ||
        lowered.contains('connection') ||
        lowered.contains('failed to fetch') ||
        lowered.contains('network is unreachable') ||
        lowered.contains('host lookup')) {
      return 'No internet connection. Your information has not been submitted.';
    }
    if (lowered.contains('timed out') || lowered.contains('timeout')) {
      return 'The site network is slow. Please try again.';
    }
    if (lowered.contains('401') || lowered.contains('unauthorized')) {
      return 'Your session has expired. Please sign in again.';
    }
    if (lowered.contains('403') || lowered.contains('forbidden')) {
      return 'Your role does not have permission for this action.';
    }
    if (lowered.contains('409') || lowered.contains('conflict')) {
      return 'This was already submitted. Refresh to see the latest status.';
    }
    return message.isEmpty ? 'Something went wrong. Please try again.' : message;
  }

  /// Spinner caption shown while a field action is in flight.
  ///
  /// Always names the action, so the user knows what the phone is waiting for
  /// and cannot tell a slow request from a failed one.
  static String submitting(String action) => 'Submitting $action…';
}