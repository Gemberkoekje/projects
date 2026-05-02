/**
 * E2E smoke test: /orchestration route
 *
 * Phase 17h: Verify that all three orchestration panels are present in the DOM
 * when navigating to the /spacetraders/dashboard/orchestration route.
 *
 * Prerequisites: the full application stack must be running and accessible at
 * PLAYWRIGHT_BASE_URL (default: http://localhost:5173).
 *
 * Run with:
 *   npx playwright test --config=playwright.config.ts
 */
import { test, expect } from '@playwright/test'

test.describe('OrchestrationPage smoke test', () => {
  test('all three panels are present in the DOM', async ({ page }) => {
    await page.goto('/spacetraders/dashboard/orchestration')

    // Page-level heading
    await expect(page.getByRole('heading', { name: 'Orchestration' })).toBeVisible()

    // Each section panel must be present
    await expect(page.getByRole('region', { name: 'Goal Chains' })).toBeVisible()
    await expect(page.getByRole('region', { name: 'Assignments' })).toBeVisible()
    await expect(page.getByRole('region', { name: 'Activity' })).toBeVisible()
  })

  test('Goal Chains section heading is visible', async ({ page }) => {
    await page.goto('/spacetraders/dashboard/orchestration')
    await expect(page.getByRole('heading', { name: 'Goal Chains' })).toBeVisible()
  })

  test('Assignments section heading is visible', async ({ page }) => {
    await page.goto('/spacetraders/dashboard/orchestration')
    await expect(page.getByRole('heading', { name: 'Assignments' })).toBeVisible()
  })

  test('Activity section heading is visible', async ({ page }) => {
    await page.goto('/spacetraders/dashboard/orchestration')
    await expect(page.getByRole('heading', { name: 'Activity' })).toBeVisible()
  })
})
