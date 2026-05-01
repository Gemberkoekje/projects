import '@testing-library/jest-dom'
import { vi } from 'vitest'

// Mock @microsoft/signalr to prevent real network calls during tests.
// HubConnectionBuilder must be a real class so `new HubConnectionBuilder()`
// works at runtime.
vi.mock('@microsoft/signalr', () => {
  const mockConnection = {
    on: vi.fn(),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
  }

  class HubConnectionBuilder {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    build() {
      return mockConnection
    }
  }

  return { HubConnectionBuilder }
})

// Navigate to the dashboard base path so BrowserRouter (basename =
// "/spacetraders/dashboard") can match routes correctly in jsdom.
window.history.pushState({}, '', '/spacetraders/dashboard/')
