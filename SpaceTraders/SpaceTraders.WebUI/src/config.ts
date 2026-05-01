/**
 * Runtime configuration injected by the container at startup.
 * The nginx entrypoint writes window.__RUNTIME_CONFIG__ into /config.js,
 * which is loaded by index.html before the React bundle.
 * This avoids baking secrets into the static bundle at build time.
 */

interface RuntimeConfig {
  apiBaseUrl: string
  dashboardApiKey: string
}

declare global {
  interface Window {
    __RUNTIME_CONFIG__?: Partial<RuntimeConfig>
  }
}

function getConfig(): RuntimeConfig {
  const rc = window.__RUNTIME_CONFIG__ ?? {}
  return {
    apiBaseUrl: rc.apiBaseUrl ?? '/spacetraders/api',
    dashboardApiKey: rc.dashboardApiKey ?? '',
  }
}

export const config = getConfig()
