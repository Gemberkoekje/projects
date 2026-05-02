/**
 * Playwright configuration for SpaceTraders WebUI E2E smoke tests.
 * Requires the full application stack (API + WebUI) to be running.
 *
 * Start the dev server with:
 *   npm run dev
 * or point PLAYWRIGHT_BASE_URL at a deployed instance.
 */
import { defineConfig, devices } from '@playwright/test'

const BASE_URL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173'

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  fullyParallel: true,
  retries: process.env.CI ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL: BASE_URL,
    trace: 'on-first-retry',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
