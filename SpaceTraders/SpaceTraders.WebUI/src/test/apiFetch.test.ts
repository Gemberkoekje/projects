import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { apiFetch } from '../lib/api-fetch'

describe('apiFetch', () => {
  const mockFetch = vi.fn()

  beforeEach(() => {
    vi.stubGlobal('fetch', mockFetch)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    mockFetch.mockReset()
  })

  it('sends the X-Api-Key header from runtime config', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ result: 'ok' }),
    })

    await apiFetch('/test')

    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect((init.headers as Record<string, string>)['X-Api-Key']).toBeDefined()
  })

  it('constructs the URL from the configured apiBaseUrl', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({}),
    })

    await apiFetch('/finance/summary')

    const [url] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect(url).toContain('/finance/summary')
  })

  it('returns the parsed JSON body on success', async () => {
    const payload = { credits: 42_000 }
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => payload,
    })

    const result = await apiFetch<typeof payload>('/agent')
    expect(result).toEqual(payload)
  })

  it('throws on a non-OK response', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 401,
      statusText: 'Unauthorized',
    })

    await expect(apiFetch('/agent')).rejects.toThrow('401')
  })

  it('does not set Content-Type on body-less requests', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({}),
    })

    await apiFetch('/test')

    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    const headers = init.headers as Record<string, string>
    expect(headers['Content-Type']).toBeUndefined()
  })

  it('sets Content-Type: application/json when a body is present', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({}),
    })

    await apiFetch('/test', { method: 'POST', body: JSON.stringify({ x: 1 }) })

    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    const headers = init.headers as Record<string, string>
    expect(headers['Content-Type']).toBe('application/json')
  })
})
