import { describe, expect, it, beforeEach } from 'vitest'

describe('config', () => {
  beforeEach(() => {
    // Reset the runtime config before each test
    window.__RUNTIME_CONFIG__ = undefined
  })

  it('uses default apiBaseUrl when runtime config is absent', async () => {
    const { config } = await import('../config')
    expect(config.apiBaseUrl).toBe('/spacetraders/api')
  })

  it('uses default empty dashboardApiKey when runtime config is absent', async () => {
    const { config } = await import('../config')
    expect(config.dashboardApiKey).toBe('')
  })
})
