import { defineConfig } from '@playwright/test'
if (!process.env.BUILDWISE_LIVE_DIR || !process.env.VITE_API_BASE_URL) {
  throw new Error('Start the isolated procurement host and set BUILDWISE_LIVE_DIR and VITE_API_BASE_URL.')
}
export default defineConfig({
  testDir: './e2e', testMatch: '**/procurement.live.js', workers: 1,
  timeout: 120000, outputDir: '../../logs/phase4-browser',
  reporter: [['list']],
  use: { baseURL: 'http://127.0.0.1:5192', browserName: 'chromium', actionTimeout: 15000,
    channel: process.env.PLAYWRIGHT_CHANNEL || undefined,
    viewport: { width: 1440, height: 1000 }, screenshot: 'only-on-failure' },
  webServer: { command: 'npm run dev -- --host 127.0.0.1 --port 5192 --strictPort',
    url: 'http://127.0.0.1:5192', reuseExistingServer: false,
    env: { VITE_API_BASE_URL: process.env.VITE_API_BASE_URL } },
})
