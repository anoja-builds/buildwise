// Shared API transport helpers.
//
// Every client in the app (auth, procurement, quality, administration and the
// supplier portal) previously did its own bare `fetch`. When the API was not
// running, the browser surfaced the raw, unhelpful string "Failed to fetch" with
// no hint about what to do. This module centralises the base URL, network-failure
// detection, and a message that tells the user how to fix it.

export const API_BASE = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5078/api'

/**
 * True when an error is the browser's opaque network failure rather than a real
 * HTTP response. `fetch` rejects with a TypeError when the server is unreachable
 * (down, wrong port, blocked by CORS, or DNS failure), and the message text
 * differs between browsers, so both the type and the text are checked.
 *
 * The app also throws `TypeError('Failed to fetch')` deliberately in one place
 * (an unauthenticated 401 that should trigger the mock fallback), so the message
 * check is required, not just belt-and-braces.
 */
export function isNetworkFailure(err) {
  if (err instanceof TypeError) return true
  return typeof err?.message === 'string' && /failed to fetch|networkerror|load failed/i.test(err.message)
}

/**
 * Builds a message that names the unreachable URL and the fix, instead of the
 * browser's bare "Failed to fetch".
 */
export function networkFailureMessage(err, url = API_BASE) {
  const target = url ?? API_BASE
  return (
    `Cannot reach the BuildWise API at ${target}. ` +
    'It is not running, or the port is blocked. ' +
    'Start the full stack with: scripts\\start-dev.ps1 ' +
    '(then confirm scripts\\check-services.ps1 shows the API as UP).'
  )
}

/**
 * Wraps a `fetch` call so a network failure becomes a descriptive Error, while
 * leaving HTTP-level errors (401/403/404/500) untouched for the caller to handle.
 */
export async function fetchOrThrow(url, options) {
  try {
    return await fetch(url, options)
  } catch (err) {
    if (isNetworkFailure(err)) {
      throw new Error(networkFailureMessage(err, url))
    }
    throw err
  }
}