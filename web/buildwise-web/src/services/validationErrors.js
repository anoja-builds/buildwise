// Maps ASP.NET Core validation responses into messages a form can display.
//
// The API reports business-rule failures in several shapes depending on which
// layer rejected the request, and a form has to show all of them rather than a
// generic "something went wrong":
//
//   ValidationProblemDetails  { errors: { "TransportCharge": ["..."] } }  (RFC 7807)
//   { message: "..." }        controllers returning an anonymous object
//   { error:   "..." }        auth/validation endpoints
//   { title:   "..." }        bare ProblemDetails
//   "plain text body"         controllers returning BadRequest(string)
//
// A field key is only treated as a field when the server actually names one, so
// this never invents an association the API did not report.

const FIELD_KEY_HINTS = [
  'deliveryReference',
  'receivedQuantity',
  'damagedQuantity',
  'inspectedQuantity',
  'acceptedQuantity',
  'rejectedQuantity',
  'rejectionReason',
  'notes',
  'transportCharge',
  'quotationDate',
  'promisedDeliveryDate',
  'validUntil',
  'requiredResponseDate',
  'supplierIds',
  'email',
  'password',
  'fullName',
]

/**
 * Normalises a field name to the camelCase key a React form uses.
 * The API serialises properties in camelCase already, but a ProblemDetails
 * `title` can arrive PascalCased, so both are accepted.
 */
function normaliseKey(key) {
  const camel = key.charAt(0).toLowerCase() + key.slice(1)
  return camel
}

/**
 * @returns {{ fieldErrors: Record<string, string>, general: string|null }}
 *   `fieldErrors` maps a form field to its first message; `general` holds a
 *   business-rule message that is not tied to one input.
 */
export function mapValidationError(payload) {
  const fieldErrors = {}
  let general = null

  if (payload == null) return { fieldErrors, general }

  // Bare string body, e.g. BadRequest("Quotation date ... cannot be in the future.")
  if (typeof payload === 'string') {
    const text = payload.trim()
    return { fieldErrors, general: text.length > 0 ? text : null }
  }

  if (Array.isArray(payload)) {
    const joined = payload.filter(Boolean).join(' ')
    return { fieldErrors, general: joined.length > 0 ? joined : null }
  }

  // RFC 7807 ValidationProblemDetails
  if (payload.errors && typeof payload.errors === 'object' && !Array.isArray(payload.errors)) {
    for (const [rawKey, value] of Object.entries(payload.errors)) {
      const key = normaliseKey(rawKey)
      const message = Array.isArray(value) ? value[0] : value
      if (!message) continue
      fieldErrors[key] = String(message)
    }
  }

  const summary = payload.message || payload.error || payload.detail || payload.title
  if (summary) general = String(summary)

  // A 400 from a business rule sometimes names the field in prose only, e.g.
  // "Received quantity cannot exceed the ordered quantity." Attach those to the
  // most likely input so the user sees it beside the field as well.
  if (Object.keys(fieldErrors).length === 0 && general) {
    const lowered = general.toLowerCase()
    const hinted = FIELD_KEY_HINTS.find((hint) => lowered.includes(toWords(hint)))
    if (hinted) fieldErrors[hinted] = general
  }

  return { fieldErrors, general }
}

/**
 * Splits a camelCase field name into the words the server actually writes in a
 * message: `transportCharge` -> `transport charge`. Comparing the raw camel form
 * would never match, because the message is prose rather than JSON.
 */
function toWords(camelName) {
  return camelName.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase()
}

/**
 * Message for a non-400 failure, so a form never shows a raw stack trace or an
 * internal exception string.
 */
export function statusMessage(status, payload) {
  const { general } = mapValidationError(payload)

  switch (status) {
    case 400:
      return general || 'The submitted values were rejected. Check the highlighted fields and try again.'
    case 401:
      return 'Your session has expired. Sign in again to continue.'
    case 403:
      return 'Your role does not allow this action.'
    case 404:
      return general || 'That record no longer exists. Refresh the list and try again.'
    case 409:
      return general || 'That change conflicts with the current state of the record. Refresh and try again.'
    case 429:
      return 'Too many requests. Wait a moment and try again.'
    default:
      if (status >= 500) return 'The server could not complete that request. Try again shortly.'
      return general || 'The request could not be completed.'
  }
}

/**
 * Combines an API failure into the shape a form renders: per-field messages plus
 * a general banner. Never leaks a raw exception or stack trace.
 */
export function describeApiFailure(err) {
  const status = err?.status ?? err?.statusCode ?? null
  const payload = err?.payload ?? err?.body ?? err?.data ?? null

  if (err?.name === 'TypeError' || /failed to fetch|networkerror|load failed/i.test(err?.message ?? '')) {
    return {
      fieldErrors: {},
      general: 'Cannot reach the BuildWise API. Start the stack with scripts\\start-dev.ps1 and try again.',
    }
  }

  const { fieldErrors, general } = mapValidationError(payload)
  const banner = status ? statusMessage(status, payload) : (general || 'The request could not be completed.')

  return { fieldErrors, general: banner }
}