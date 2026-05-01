import { config } from '@/config'

/**
 * Thin fetch wrapper used by all TanStack Query query functions.
 * Injects the dashboard-scoped `X-Api-Key` header so callers never have to
 * handle auth themselves.
 */
export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const url = `${config.apiBaseUrl}${path}`
  const response = await fetch(url, {
    ...init,
    headers: {
      // Only set Content-Type for requests that carry a body.
      ...(init?.body != null ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
      // Set X-Api-Key last so it cannot be overridden by callers.
      'X-Api-Key': config.dashboardApiKey,
    },
  })

  if (!response.ok) {
    throw new Error(`API error ${response.status}: ${response.statusText}`)
  }

  return response.json() as Promise<T>
}
