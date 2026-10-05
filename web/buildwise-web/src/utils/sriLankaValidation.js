/**
 * Sri Lanka domain and validation utilities for BuildWise.
 * Conforms to TRCSL national numbering plan (10 digits, 07x prefix),
 * Sri Lanka 2026 public holidays schedule, and standard currency formatting (LKR / Rs.).
 */

// TRCSL 10-digit mobile prefixes: 070, 071, 072, 074, 075, 076, 077, 078
const LK_MOBILE_REGEX = /^(?:0|\+94|0094)?(7[0-24-8]\d{7})$/

/**
 * Validates and normalizes Sri Lankan mobile numbers.
 * @param {string} phone 
 * @returns {{ isValid: boolean, normalized: string|null, display: string|null, error: string|null }}
 */
export function validateSriLankanMobile(phone) {
  if (!phone || typeof phone !== 'string') {
    return { isValid: false, normalized: null, display: null, error: 'Phone number is required.' }
  }

  const cleaned = phone.replace(/[\s\-\(\)]/g, '')
  const match = cleaned.match(LK_MOBILE_REGEX)

  if (!match) {
    return {
      isValid: false,
      normalized: null,
      display: null,
      error: 'Please enter a valid 10-digit Sri Lankan mobile number (e.g. 0771234567 or 0751234567).',
    }
  }

  const sevenSeries = match[1] // e.g. "771234567"
  const normalized = `+94${sevenSeries}`
  const display = `0${sevenSeries.slice(0, 2)} ${sevenSeries.slice(2, 5)} ${sevenSeries.slice(5)}`

  return {
    isValid: true,
    normalized,
    display,
    error: null,
  }
}

/**
 * Validates standard email syntax.
 * @param {string} email
 * @returns {boolean}
 */
export function isValidEmail(email) {
  if (!email || typeof email !== 'string') return false
  const trimmed = email.trim()
  const emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/
  return emailRegex.test(trimmed)
}

/**
 * Sri Lanka 2026 Public & Bank Holidays (Ministry of Home Affairs schedule)
 */
export const SRI_LANKA_HOLIDAYS_2026 = {
  '2026-01-14': 'Tamil Thai Pongal Day',
  '2026-01-15': 'Duruthu Full Moon Poya Day',
  '2026-02-04': 'National Day (Independence Day)',
  '2026-02-14': 'Navam Full Moon Poya Day',
  '2026-02-17': 'Maha Shivaratri Day',
  '2026-03-15': 'Medin Full Moon Poya Day',
  '2026-03-21': 'Eid-ul-Fitr (Ramazan Festival Day)',
  '2026-04-03': 'Good Friday',
  '2026-04-13': 'Day prior to Sinhala & Tamil New Year Day',
  '2026-04-14': 'Sinhala & Tamil New Year Day',
  '2026-04-15': 'Bak Full Moon Poya Day',
  '2026-05-01': 'May Day (International Workers\' Day)',
  '2026-05-14': 'Vesak Full Moon Poya Day',
  '2026-05-15': 'Day following Vesak Full Moon Poya Day',
  '2026-05-28': 'Eid-ul-Adha (Hadji Festival Day)',
  '2026-06-13': 'Poson Full Moon Poya Day',
  '2026-07-12': 'Esala Full Moon Poya Day',
  '2026-08-11': 'Nikini Full Moon Poya Day',
  '2026-08-26': 'Milad-un-Nabi (Holy Prophet\'s Birthday)',
  '2026-09-09': 'Binara Full Moon Poya Day',
  '2026-10-09': 'Vap Full Moon Poya Day',
  '2026-11-08': 'Deepavali Festival Day',
  '2026-11-08': 'Il Full Moon Poya Day',
  '2026-12-07': 'Unduvap Full Moon Poya Day',
  '2026-12-25': 'Christmas Day',
}

/**
 * Checks if a selected date falls on a Sri Lankan public holiday and returns an advisory warning.
 * @param {string} dateString YYYY-MM-DD
 * @returns {string|null} Advisory warning or null
 */
export function getHolidayAdvisory(dateString) {
  if (!dateString) return null
  const holidayName = SRI_LANKA_HOLIDAYS_2026[dateString]
  if (holidayName) {
    return `⚠️ ${dateString} is a Sri Lankan Public Holiday (${holidayName}). Please confirm site operating and unloading availability.`
  }
  return null
}

/**
 * Formats a currency amount in Sri Lankan Rupees (LKR / Rs.)
 * @param {number|string} amount
 * @returns {string}
 */
export function formatLKR(amount) {
  const num = Number(amount ?? 0)
  if (isNaN(num)) return 'Rs. 0.00'
  return new Intl.NumberFormat('en-LK', {
    style: 'currency',
    currency: 'LKR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(num).replace('LKR', 'Rs.')
}

/**
 * Validates quantity based on discrete integer units vs decimal units.
 * @param {number|string} quantity
 * @param {string} unit
 * @returns {{ isValid: boolean, error: string|null }}
 */
export function validateQuantity(quantity, unit = '') {
  const num = Number(quantity)
  if (isNaN(num) || num <= 0) {
    return { isValid: false, error: 'Quantity must be a positive number greater than 0.' }
  }

  const discreteUnits = ['bags', 'bag', 'pieces', 'piece', 'nos', 'sheets', 'sheet', 'units', 'boxes']
  const isDiscrete = discreteUnits.includes((unit || '').trim().toLowerCase())

  if (isDiscrete && !Number.isInteger(num)) {
    return {
      isValid: false,
      error: `Decimal quantities are not allowed for '${unit}'. Please specify a whole integer quantity (e.g. 10 instead of 10.5).`,
    }
  }

  return { isValid: true, error: null }
}
