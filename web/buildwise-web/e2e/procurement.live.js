import { test, expect } from '@playwright/test'
import fs from 'node:fs'
import path from 'node:path'

// No route interception or API fixtures: the Flutter-created request, users,
// suppliers, quotations, agent workflow and PO all live in the isolated database.
test('Flutter request → React approval, quotations and PO confirmation', async ({ page }) => {
  const directory = process.env.BUILDWISE_LIVE_DIR
  const { requestId } = JSON.parse(fs.readFileSync(path.join(directory, 'request.json'), 'utf8'))
  const failures = []
  page.on('pageerror', err => failures.push(err.message))
  await page.goto('/')
  async function login(email) {
    await page.getByLabel(/^Email/).fill(email)
    await page.getByLabel(/^Password/).fill('Passw0rd!')
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()
  }
  async function nav(name) { await page.getByRole('navigation').getByRole('link', { name, exact: true }).click() }
  async function workspace() {
    await nav('Quotations')
    await page.getByRole('row').filter({ has: page.getByText(`MR-${requestId}`, { exact: true }) })
      .getByRole('button', { name: 'Open workspace' }).click()
    await expect(page.getByText(`MATERIAL REQUEST #${requestId}`, { exact: true })).toBeVisible()
  }
  await login('project.manager@buildwise.demo')
  await nav('Material Requests')
  await page.getByRole('button', { name: `Review request #${requestId}`, exact: true }).click()
  await page.getByRole('button', { name: 'Confirm decision', exact: true }).click()
  await expect(page.getByRole('button', { name: `Review request #${requestId}`, exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'Sign out' }).click()
  await login('procurement.officer@buildwise.demo')

  // Record B while active, then suspend it before analysis, as can happen to
  // an already received quotation. Both status transitions use the existing UI.
  async function supplierBStatus(status) {
    await nav('Suppliers')
    await page.getByRole('row').filter({ hasText: 'Supplier B' }).getByRole('button', { name: 'View', exact: true }).click()
    const changed = page.waitForResponse(r => r.url().endsWith('/status') && r.request().method() === 'PATCH')
    await page.getByRole('combobox', { name: 'Change supplier status' }).selectOption(status)
    expect((await changed).status()).toBe(204)
  }
  await supplierBStatus('Active')
  await workspace()
  for (const [prefix, quantity, price] of [['Supplier A', 250, 2100], ['Supplier B', 250, 2040], ['Supplier C', 200, 2050]]) {
    const select = page.getByRole('combobox', { name: 'Supplier', exact: true })
    const option = select.locator('option').filter({ hasText: prefix })
    await expect(option).toHaveCount(1)
    const supplierId = await option.getAttribute('value')
    await select.selectOption(supplierId)
    await page.getByRole('spinbutton', { name: /^Quantity/ }).fill(String(quantity))
    await page.getByRole('spinbutton', { name: 'Unit price', exact: true }).fill(String(price))
    const created = page.waitForResponse(r => r.url().endsWith(`/material-requests/${requestId}/quotations`) && r.request().method() === 'POST')
    await page.getByRole('button', { name: 'Save quotation', exact: true }).click()
    const response = await created
    expect(response.status()).toBe(200)
    expect((await response.json()).totalAmount).toBe(quantity * price)
    await expect(select).toHaveValue('')
  }
  await supplierBStatus('Suspended')
  await workspace()
  await expect(page.getByText('Suspended', { exact: true })).toBeVisible()
  const started = page.waitForResponse(r => r.url().endsWith(`/material-requests/${requestId}/procurement-workflow`) && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Run AI Analysis', exact: true }).click()
  const response = await started
  expect(response.ok()).toBeTruthy()
  const { workflowId } = await response.json()
  await expect(page.getByText('Deterministic validation passed', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Approve', exact: true })).toHaveCount(0)
  const analysisMode = await page.getByRole('status').filter({ hasText: 'Analysis mode:' }).innerText()
  await expect(page.locator('.ai-card__winner-name')).toContainText('Supplier A')
  await expect(page.locator('.warning-list')).toContainText('Suspended')
  await page.getByRole('button', { name: 'Sign out' }).click()
  await login('procurement.manager@buildwise.demo')
  await workspace()
  await page.getByRole('button', { name: 'Comparison & AI Recommendation', exact: true }).click()
  await expect(page.getByText('Deterministic validation passed', { exact: true })).toBeVisible()
  const approved = page.waitForResponse(r => r.url().endsWith(`/procurement-workflow/${workflowId}/decision`) && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Approve', exact: true }).click()
  const approval = await approved
  expect(approval.status()).toBe(200)
  const { purchaseOrderId } = await approval.json()
  await expect(page.getByText(`PO-${purchaseOrderId}`, { exact: true })).toBeVisible()
  await page.getByRole('combobox', { name: 'New status' }).selectOption('Confirmed')
  const confirmed = page.waitForResponse(r => r.url().endsWith(`/purchase-orders/${purchaseOrderId}/status`) && r.request().method() === 'PATCH')
  await page.getByRole('button', { name: 'Update status', exact: true }).click()
  expect((await confirmed).status()).toBe(204)
  await expect(page.getByText('Confirmed', { exact: true })).toBeVisible()
  expect(failures).toEqual([])
  fs.writeFileSync(path.join(directory, 'result.json'), JSON.stringify({ requestId, workflowId, purchaseOrderId, analysisMode, status: 'Confirmed' }, null, 2))
})
