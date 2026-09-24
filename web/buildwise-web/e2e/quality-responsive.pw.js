import { test, expect } from '@playwright/test'

// Browser layout fixtures only: this suite does not claim live API integration.
const inspection = {
  id: 1, deliveryId: 2, deliveryReference: 'DEL-240', inspectorUserId: 7,
  inspectorName: 'Quality Inspector', inspectionDate: '2026-09-24T10:00:00Z',
  status: 'Completed', overallDecision: 'PartiallyAccepted', notes: '235 bags accepted; five rejected.',
  deliveryItems: [{ deliveryItemId: 3, receivedQuantity: 240 }],
  items: [{ id: 9, deliveryItemId: 3, condition: 'Moisture damage', acceptedQuantity: 235, rejectedQuantity: 5, remarks: 'Quarantine rejected bags.' }],
}
const ncr = {
  id: 5, inspectionId: 1, inspectionItemId: 9, deliveryItemId: 3,
  issueDescription: 'Five damaged cement bags', severity: 'High', status: 'CorrectiveActionRequired',
  condition: 'Moisture damage', acceptedQuantity: 235, rejectedQuantity: 5,
  correctiveAction: `Replace rejected bags. Reference: https://example.test/${'reference'.repeat(40)}`,
  createdAt: '2026-09-24T10:00:00Z', updatedAt: '2026-09-24T10:00:00Z',
}
const workflow = {
  workflowId: 20, inspectionId: 1, status: 'Completed', approvalStatus: 'Pending',
  finalOutcome: 'Advisory fixture for browser layout verification.',
  steps: [
    { stepOrder: 1, stepName: 'Run Agentic Quality Analysis', status: 'Completed', structuredResult: {
      recommendation: {
        riskLevel: 'High', evidenceSummary: 'Five damaged bags.', rationaleSummary: 'Review rejected material.', ncrRecommended: true,
        riskFlags: [{ flag: 'Moisture damage', evidenceReferences: ['inspection-item:9'] }],
        itemRecommendations: [{ inspectionItemId: 9, ncrRecommended: true, suggestedSeverity: 'High', suggestedIssueDescription: 'Moisture damage', suggestedCorrectiveAction: ncr.correctiveAction, rationale: 'Replacement needed.', evidenceReferences: ['inspection-item:9'] }],
      },
      trace: [{ action: 'get_current_inspection_evidence', success: true, evidence: 'long-structured-value-'.repeat(50) }],
    } },
    { stepOrder: 2, stepName: 'Validate Quality Recommendation', status: 'Completed', validationResult: { valid: true, advisoryOnly: true } },
  ],
}

test.beforeEach(async ({ page }) => {
  // A catch-all prevents accidental access to a configured/shared API.
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    const responses = {
      '/api/auth/login': { token: 'layout-test-token', expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: 7, fullName: 'Quality Inspector', roles: ['QualityInspector'] } },
      '/api/inspections': [inspection],
      '/api/inspections/1': inspection,
      '/api/non-conformances': route.request().method() === 'POST' ? ncr : [ncr],
      '/api/non-conformances/5': ncr,
      '/api/quality-risk-agent/workflows/20': workflow,
    }
    const data = responses[url.pathname]
    await route.fulfill({ status: data ? 200 : 404, json: data || { detail: 'Quality-agent workflow not found.' } })
  })
  await page.goto('/')
  await page.getByLabel(/Email/).fill('layout@example.test')
  await page.getByLabel(/Password/).fill('layout-test-password')
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await page.getByRole('navigation', { name: 'Main navigation' }).waitFor()
})

async function fitsViewport(page) {
  // Check actual rendered geometry, not CSS declarations or jsdom's zero widths.
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
  const geometry = await page.locator('.quality-workspace').evaluate((workspace) => ({
    width: innerWidth,
    bounds: [...workspace.querySelectorAll('.card, .page-header, input, textarea, select, pre')].map((element) => {
      const box = element.getBoundingClientRect()
      return { left: box.left, right: box.right, width: box.width }
    }),
    clipping: [document.documentElement, document.body, document.querySelector('.app-main'), document.querySelector('.content'), workspace]
      .some((element) => ['hidden', 'clip'].includes(getComputedStyle(element).overflowX)),
  }))
  expect(geometry.clipping, 'Do not conceal page overflow').toBe(false)
  for (const box of geometry.bounds.filter((box) => box.width > 0)) {
    expect(box.left).toBeGreaterThanOrEqual(-1)
    expect(box.right).toBeLessThanOrEqual(geometry.width + 1)
  }
}

async function inspectionDetail(page) {
  await page.getByRole('button', { name: 'Quality Inspections', exact: true }).click()
  await page.getByRole('button', { name: 'Inspection #1', exact: true }).click()
  await page.getByRole('heading', { name: 'Inspection items', exact: true }).waitFor()
}

async function tableScrollsLocally(page) {
  const table = page.locator('.quality-workspace .table-wrap')
  const size = await table.evaluate((element) => {
    element.scrollLeft = element.scrollWidth
    return { client: element.clientWidth, content: element.scrollWidth, left: element.scrollLeft, viewport: innerWidth }
  })
  if (size.viewport <= 768) {
    expect(size.content).toBeGreaterThan(size.client)
    expect(size.left, 'The last columns must remain reachable').toBeGreaterThan(0)
  } else {
    expect(size.content).toBeLessThanOrEqual(size.client + 1)
  }
  await table.evaluate((element) => { element.scrollLeft = 0 })
}

test('history and inspection tables stay inside cards and navigation still works', async ({ page }, testInfo) => {
  await page.getByRole('button', { name: 'Quality Inspections', exact: true }).click()
  await page.getByRole('button', { name: 'Inspection #1', exact: true }).waitFor()
  await fitsViewport(page)
  await tableScrollsLocally(page)
  await page.getByRole('button', { name: 'Inspection #1', exact: true }).click()
  await page.getByRole('heading', { name: 'Inspection items', exact: true }).waitFor()
  await fitsViewport(page)
  await tableScrollsLocally(page)
  await page.screenshot({ path: testInfo.outputPath('inspection.png'), fullPage: true })

  // Reach the action at the far end of the table without moving the document.
  const action = page.getByRole('button', { name: 'Create NCR for item #9', exact: true })
  await action.scrollIntoViewIfNeeded()
  await expect(action).toBeInViewport()
  await expect.poll(() => page.evaluate(() => scrollX)).toBe(0)
  await action.click()
  await expect(page.getByRole('heading', { name: 'Create non-conformance', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  await page.getByRole('button', { name: 'Back to Quality Inspections', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Inspection #1', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Non-Conformances', exact: true }).click()
  await expect(page.getByRole('button', { name: 'NCR #5', exact: true })).toBeVisible()
  await fitsViewport(page)
  if (page.viewportSize().width === 1440) {
    expect((await page.locator('.sidebar').boundingBox()).width).toBe(268)
    expect((await page.locator('.app-main').boundingBox()).x).toBe(268)
  }
})

test('NCR forms, list and long corrective actions fit without clipping', async ({ page }, testInfo) => {
  await inspectionDetail(page)
  await page.getByRole('button', { name: 'Create NCR for item #9', exact: true }).click()
  await fitsViewport(page)
  await page.getByLabel(/Issue description/).fill('Five damaged bags')
  await page.getByLabel(/Corrective action/).fill(ncr.correctiveAction)
  await page.screenshot({ path: testInfo.outputPath('ncr-form.png'), fullPage: true })
  await page.getByRole('button', { name: 'Create NCR', exact: true }).click()
  await page.getByRole('heading', { name: 'Non-conformance details', exact: true }).waitFor()
  await fitsViewport(page)
  await page.getByLabel(/Corrective action/).fill('Replacement arranged')
  await fitsViewport(page)
  await page.getByRole('button', { name: 'Non-Conformances', exact: true }).click()
  await page.getByRole('button', { name: 'NCR #5', exact: true }).waitFor()
  await fitsViewport(page)
  await tableScrollsLocally(page)
  await page.getByRole('button', { name: 'NCR #5', exact: true }).click()
  await page.getByRole('heading', { name: 'Non-conformance details', exact: true }).waitFor()
  await fitsViewport(page)
})

test('agent inputs, expanded structured results and errors fit the viewport', async ({ page }, testInfo) => {
  await inspectionDetail(page)
  await page.getByLabel(/Workflow ID/).fill('20')
  await page.getByRole('button', { name: 'Load workflow', exact: true }).click()
  await page.getByRole('region', { name: 'Validated advisory recommendation' }).waitFor()
  await page.getByText('Structured result and execution trace', { exact: true }).click()
  await expect(page.locator('details pre')).toBeVisible()
  await fitsViewport(page)
  await page.screenshot({ path: testInfo.outputPath('agent-results.png'), fullPage: true })
  await page.getByLabel(/Workflow ID/).fill('999')
  await page.getByRole('button', { name: 'Load workflow', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Quality-agent workflow not found.')
  await fitsViewport(page)
})
