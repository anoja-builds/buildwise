/// The ASP.NET Core API root, including /api/ (not the Python agent URL).
/// Supply with --dart-define=API_BASE_URL=https://your-host/api/
abstract final class ApiConfig {
  static const baseUrl = String.fromEnvironment('API_BASE_URL');

  static Uri parseBaseUrl(String value) {
    final uri = Uri.tryParse(value.trim());
    if (uri == null ||
        !['http', 'https'].contains(uri.scheme) ||
        uri.host.isEmpty ||
        uri.userInfo.isNotEmpty ||
        uri.hasQuery ||
        uri.hasFragment) {
      throw ArgumentError(
        'Set API_BASE_URL to an HTTP(S) API root ending in /api/.',
      );
    }
    // A trailing slash keeps Uri.resolve from replacing the last path segment.
    return uri.replace(
      path: uri.path.endsWith('/') ? uri.path : '${uri.path}/',
    );
  }
}
