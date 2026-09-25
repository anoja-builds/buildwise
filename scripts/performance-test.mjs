const API_BASE = (process.env.BUILDWISE_API_URL || 'http://127.0.0.1:5078/api').replace(/\/$/, '')
const OUTPUT = process.argv[2]
const TOTAL = Number(process.env.PERFORMANCE_REQUESTS || 30)
const CONCURRENCY = Number(process.env.PERFORMANCE_CONCURRENCY || 5)

async function login() {
  const response = await fetch(`${API_BASE}/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email: 'procurement.manager@buildwise.demo', password: 'Passw0rd!' }),
    signal: AbortSignal.timeout(10_000),
  })
  if (!response.ok) throw new Error(`Performance login failed: HTTP ${response.status}`)
  return (await response.json()).token
}

async function measure({ name, url, options = {}, total = TOTAL, concurrency = CONCURRENCY, thresholdMs, validate }) {
  const latencies = []
  const errors = []
  let cursor = 0
  let succeeded = 0
  const started = performance.now()

  async function worker() {
    while (true) {
      const index = cursor++
      if (index >= total) return
      const callStarted = performance.now()
      try {
        const response = await fetch(url, { ...options, signal: AbortSignal.timeout(15_000) })
        const body = await response.text()
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        if (validate && !validate(body)) throw new Error('response contract assertion failed')
        succeeded++
      } catch (error) {
        errors.push(error.message)
      } finally {
        latencies.push(performance.now() - callStarted)
      }
    }
  }

  await Promise.all(Array.from({ length: Math.min(concurrency, total) }, worker))
  const durationMs = performance.now() - started
  latencies.sort((a, b) => a - b)
  const percentile = (p) => latencies[Math.min(latencies.length - 1, Math.ceil(latencies.length * p) - 1)] || 0
  const result = {
    name,
    total,
    concurrency,
    succeeded,
    failed: errors.length,
    successRatePct: Number(((succeeded / total) * 100).toFixed(2)),
    throughputRps: Number((total / (durationMs / 1000)).toFixed(2)),
    latencyMs: {
      average: Number((latencies.reduce((sum, value) => sum + value, 0) / latencies.length).toFixed(2)),
      p50: Number(percentile(0.5).toFixed(2)),
      p95: Number(percentile(0.95).toFixed(2)),
      max: Number((latencies.at(-1) || 0).toFixed(2)),
    },
    localAcceptanceP95Ms: thresholdMs,
    passed: succeeded === total && percentile(0.95) <= thresholdMs,
    sampleErrors: errors.slice(0, 3),
  }
  console.log(`${result.passed ? 'PASS' : 'FAIL'} ${name}: ${result.successRatePct}% success, p95 ${result.latencyMs.p95} ms, ${result.throughputRps} req/s`)
  return result
}

const json = (value) => ({
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify(value),
})

try {
  const token = await login()
  const results = []
  results.push(await measure({
    name: 'Shared ASP.NET Core API + PostgreSQL',
    url: `${API_BASE}/material-requests?page=1&pageSize=20`,
    options: { headers: { authorization: `Bearer ${token}` } },
    thresholdMs: 2_000,
    validate: (body) => Array.isArray(JSON.parse(body)),
  }))
  results.push(await measure({
    name: 'Agent 8001 quotation analysis',
    url: 'http://127.0.0.1:8001/analyze',
    options: json({
      material_request_id: 9001,
      quotations: [{
        quotation_id: 1, supplier_id: 1, supplier_name: 'Supplier A', supplier_status: 'Active',
        quantity_offered: { '1': 250 }, unit_prices: { '1': 2100 }, total_amount: 525000, valid: true,
      }],
      requested_quantities: { '1': 250 },
    }),
    thresholdMs: 1_000,
    validate: (body) => JSON.parse(body).recommended_quotation_id === 1,
  }))
  results.push(await measure({
    name: 'Agent 8002 request analysis',
    url: 'http://127.0.0.1:8002/api/agent/analyze-request',
    options: json({ request_id: 1, project_name: 'Performance', reason: 'urgent work', items_count: 3, total_quantity: 250 }),
    thresholdMs: 1_000,
    validate: (body) => JSON.parse(body).flags.includes('HIGH_URGENCY'),
  }))
  results.push(await measure({
    name: 'Agent 8003 discrepancy analysis',
    url: 'http://127.0.0.1:8003/api/agent/analyze-discrepancy',
    options: json({ ordered_qty: 250, received_qty: 240, damaged_qty: 5 }),
    thresholdMs: 1_000,
    validate: (body) => JSON.parse(body).shortage_detected && JSON.parse(body).damage_detected,
  }))
  results.push(await measure({
    name: 'Agent 8004 quality-risk analysis',
    url: 'http://127.0.0.1:8004/api/agent/analyze-quality-risk',
    options: json({ delivery_id: 1, items: [{ material_name: 'Cement', inspected_qty: 240, accepted_qty: 235, rejected_qty: 5, rejection_reason: 'Damage' }] }),
    thresholdMs: 1_000,
    validate: (body) => JSON.parse(body).requires_ncr,
  }))

  const report = {
    measuredAtUtc: new Date().toISOString(),
    environment: 'Local Windows development stack; not a production SLA',
    requestsPerScenario: TOTAL,
    concurrencyPerScenario: CONCURRENCY,
    results,
  }
  if (OUTPUT) {
    const { mkdir, writeFile } = await import('node:fs/promises')
    const { dirname } = await import('node:path')
    await mkdir(dirname(OUTPUT), { recursive: true })
    await writeFile(OUTPUT, `${JSON.stringify(report, null, 2)}\n`)
    console.log(`Evidence written to ${OUTPUT}`)
  }
  if (results.some((result) => !result.passed)) process.exitCode = 1
} catch (error) {
  console.error(`Performance harness failed: ${error.stack || error.message}`)
  process.exitCode = 1
}
