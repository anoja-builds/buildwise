import { defineConfig } from '@playwright/test'

// Layout tests use fixture responses, never a real API or database.
const origin = 'http://127.0.0.1:5191'

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.pw.js',
  fullyParallel: false,
  workers: 1,
  outputDir: '../../logs/quality-responsive',
  reporter: [['list'], ['json', { outputFile: '../../logs/quality-responsive-results.json' }]],
  use: {
    baseURL: origin,
    browserName: 'chromium',
    channel: process.env.PLAYWRIGHT_CHANNEL || undefined,
    screenshot: 'only-on-failure',
    trace: 'off',
  },
  projects: [
    { name: 'phone-390', use: { viewport: { width: 390, height: 844 } } },
    { name: 'tablet-768', use: { viewport: { width: 768, height: 1024 } } },
    { name: 'desktop-1440', use: { viewport: { width: 1440, height: 1000 } } },
  ],
  webServer: {
    command: 'npm run dev -- --host 127.0.0.1 --port 5191 --strictPort',
    url: origin,
    reuseExistingServer: false,
    env: { VITE_API_BASE_URL: `${origin}/api` },
  },
})
