import { test, expect } from '@playwright/test'

const order = { id: 42, supplierName: 'BuildWise Supplier', status: 'Confirmed', totalAmount: 25000, orderDate: '2026-09-28', items: [{ id: 1, materialName: 'Cement', orderedQuantity: 100, unit: 'bags', unitPrice: 250, lineTotal: 25000 }] }
const delivery = { id: 1, purchaseOrderId: 42, deliveryReference: 'DEL-001', supplierName: 'BuildWise Supplier', projectName: 'Site A', status: 'Scheduled', items: [] }
async function signIn(page, roles, path) {
  await page.addInitScript((roles) => localStorage.setItem('buildwise.auth', JSON.stringify({ token: 'browser-fixture', user: { fullName: 'BuildWise Reviewer', roles } })), roles)
  await page.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    let data = []
    if (path === '/api/purchase-orders') data = { items: [order], total: 1, page: 1 }
    if (path === '/api/purchase-orders/42') data = order
    if (path === '/api/deliveries/expected') data = [delivery]
    if (path === '/api/deliveries/history') data = []
    await route.fulfill({ status: 200, json: data })
  })
  await page.goto(path)
}
async function fits(page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
}

test('procurement navigation, modal, direct refresh and history retain session', async ({ page }, testInfo) => {
  await signIn(page, ['ProcurementOfficer'], '/deliveries')
  await expect(page).toHaveURL(/\/deliveries$/)
  await expect(page.getByText('DEL-001', { exact: true })).toBeVisible()
  await fits(page)
  await page.screenshot({ path: testInfo.outputPath('receiving-deliveries.png'), fullPage: true, animations: 'disabled' })
  await page.getByRole('button', { name: '+ Schedule Delivery', exact: true }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await fits(page)
  await page.screenshot({ path: testInfo.outputPath('schedule-modal.png'), fullPage: true, animations: 'disabled' })
  await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await page.getByRole('link', { name: 'Purchase Orders', exact: true }).click()
  await page.getByRole('button', { name: 'View', exact: true }).click()
  await expect(page).toHaveURL(/\/purchase-orders\/42$/)
  await expect(page.getByRole('heading', { name: 'Order information' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Update status' })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Order information' })).toBeVisible()
  await page.goBack()
  await expect(page).toHaveURL(/\/purchase-orders$/)
  await page.goForward()
  await expect(page).toHaveURL(/\/purchase-orders\/42$/)
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()
  await fits(page)
  await page.goto('/quality-inspections/1')
  await expect(page.getByRole('heading', { name: 'Access Denied' })).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('access-denied.png'), fullPage: true, animations: 'disabled' })
})

test('administrator navigation fits and reduced motion disables page animation', async ({ page }, testInfo) => {
  await signIn(page, ['Administrator'], '/dashboard')
  await expect(page.getByRole('heading', { name: 'Procurement Dashboard' })).toBeVisible()
  await fits(page)
  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(navigation.getByRole('link')).toHaveCount(9)
  await page.screenshot({ path: testInfo.outputPath('administrator-dashboard.png'), fullPage: true, animations: 'disabled' })
  await navigation.getByRole('link', { name: 'Non-Conformances', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Non-Conformances', exact: true })).toBeVisible()
  await fits(page)
  await page.emulateMedia({ reducedMotion: 'reduce' })
  expect(await page.locator('.page-enter').evaluate((el) => getComputedStyle(el).animationName)).toBe('none')
})

test('site engineer landing and material table fit the viewport', async ({ page }, testInfo) => {
  await signIn(page, ['SiteEngineer'], '/')
  await expect(page).toHaveURL(/\/material-requests$/)
  await expect(page.getByRole('heading', { name: 'Material Requests', exact: true })).toBeVisible()
  await expect(page.getByRole('navigation').getByRole('link')).toHaveCount(2)
  await fits(page)
  await page.screenshot({ path: testInfo.outputPath('site-engineer-materials.png'), fullPage: true, animations: 'disabled' })
  await page.getByRole('link', { name: 'Deliveries', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Receive', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '+ Schedule Delivery', exact: true })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Purchase Orders', exact: true })).toHaveCount(0)
  await page.goto('/purchase-orders/42')
  await expect(page.getByRole('heading', { name: 'Access Denied' })).toBeVisible()
})
