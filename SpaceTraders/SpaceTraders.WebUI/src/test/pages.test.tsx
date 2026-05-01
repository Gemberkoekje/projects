import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import type { ReactNode } from 'react'

// ─── helpers ────────────────────────────────────────────────────────────────

function makeQueryClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
}

function Wrapper({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={makeQueryClient()}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  )
}

function WrapperWithRoute({
  children,
  path,
  initialEntry,
}: {
  children: ReactNode
  path: string
  initialEntry: string
}) {
  return (
    <QueryClientProvider client={makeQueryClient()}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path={path} element={children} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

// ─── mocks ──────────────────────────────────────────────────────────────────

vi.mock('@/lib/api-fetch', () => ({
  apiFetch: vi.fn(),
}))

import { apiFetch } from '@/lib/api-fetch'
const mockApiFetch = vi.mocked(apiFetch)

// Also stub global.fetch for OverviewPage's direct fetch call (trade-opportunities)
const mockFetch = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', mockFetch)
  mockFetch.mockResolvedValue({
    ok: true,
    status: 204,
  })
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetAllMocks()
})

// ─── OverviewPage ────────────────────────────────────────────────────────────

import OverviewPage from '../pages/OverviewPage'

describe('OverviewPage', () => {
  it('renders the heading', async () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <OverviewPage />
      </Wrapper>,
    )
    expect(screen.getByText('Overview')).toBeInTheDocument()
  })

  it('shows loading state', () => {
    mockApiFetch.mockReturnValue(new Promise(() => {}))
    render(
      <Wrapper>
        <OverviewPage />
      </Wrapper>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('renders credits when agent data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/status/agent')
        return Promise.resolve({
          symbol: 'TEST',
          credits: 1_234_567,
          startingFaction: 'COSMIC',
          shipCount: 3,
          headquartersSymbol: null,
        })
      if (path === '/status/ships') return Promise.resolve([])
      if (path === '/status/contracts') return Promise.resolve([])
      if (path === '/status/rate-limit')
        return Promise.resolve({
          remaining: 30,
          limit: 60,
          burstRemaining: 10,
          burstLimit: 20,
          resetAt: new Date().toISOString(),
          limitType: null,
          totalRequests: 100,
          throttledCount: 0,
        })
      if (path === '/status/system-alerts')
        return Promise.resolve({
          apiUnavailable: false,
          tokenResetMismatch: false,
          cacheDivergence: false,
          automationDisabled: false,
          contractDeadlinesApproaching: false,
          resetUpcoming: false,
          nextReset: null,
        })
      if (path === '/runs/') return Promise.resolve([])
      if (path === '/finance/credits-history') return Promise.resolve([])
      return Promise.resolve(null)
    })

    render(
      <Wrapper>
        <OverviewPage />
      </Wrapper>,
    )

    await waitFor(() => expect(screen.getByText('1,234,567')).toBeInTheDocument())
  })
})

// ─── FleetPage ───────────────────────────────────────────────────────────────

import FleetPage from '../pages/FleetPage'

describe('FleetPage', () => {
  it('renders the heading', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <FleetPage />
      </Wrapper>,
    )
    expect(screen.getByRole('heading', { name: 'Fleet' })).toBeInTheDocument()
  })

  it('renders filter controls', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <FleetPage />
      </Wrapper>,
    )
    expect(screen.getByRole('searchbox', { name: 'Search ships' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Filter by status' })).toBeInTheDocument()
  })

  it('renders ship rows when data loads', async () => {
    mockApiFetch.mockResolvedValue([
      {
        symbol: 'SHIP-1',
        systemSymbol: 'X1-AB',
        waypointSymbol: 'X1-AB-01',
        status: 'DOCKED',
        flightMode: 'CRUISE',
        fuelCurrent: 400,
        fuelCapacity: 400,
        cargoCurrent: 20,
        cargoCapacity: 60,
        arrivesAt: null,
        isInTransit: false,
        lastSyncedAt: new Date().toISOString(),
      },
    ])
    render(
      <Wrapper>
        <FleetPage />
      </Wrapper>,
    )
    await waitFor(() => expect(screen.getByText('SHIP-1')).toBeInTheDocument())
    expect(screen.getByRole('link', { name: 'View details for SHIP-1' })).toBeInTheDocument()
  })

  it('shows no-results message when filter matches nothing', async () => {
    mockApiFetch.mockResolvedValue([
      {
        symbol: 'SHIP-1',
        systemSymbol: null,
        waypointSymbol: null,
        status: 'DOCKED',
        flightMode: null,
        fuelCurrent: 0,
        fuelCapacity: 0,
        cargoCurrent: 0,
        cargoCapacity: 0,
        arrivesAt: null,
        isInTransit: false,
        lastSyncedAt: new Date().toISOString(),
      },
    ])

    const { getByRole } = render(
      <Wrapper>
        <FleetPage />
      </Wrapper>,
    )

    await waitFor(() => expect(screen.getByText('SHIP-1')).toBeInTheDocument())

    const searchInput = getByRole('searchbox', { name: 'Search ships' }) as HTMLInputElement
    searchInput.value = 'NOMATCH'
    searchInput.dispatchEvent(new Event('input', { bubbles: true }))

    // The no-results row should appear when filtering yields zero results
    // (we trigger by selecting transit status since ship is docked)
    const statusSelect = getByRole('combobox', { name: 'Filter by status' }) as HTMLSelectElement
    statusSelect.value = 'transit'
    statusSelect.dispatchEvent(new Event('change', { bubbles: true }))

    await waitFor(() =>
      expect(screen.getByText('No ships match the current filters.')).toBeInTheDocument(),
    )
  })
})

// ─── ActivityPage ────────────────────────────────────────────────────────────

import ActivityPage from '../pages/ActivityPage'

describe('ActivityPage', () => {
  it('renders the heading', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <ActivityPage />
      </Wrapper>,
    )
    expect(screen.getByRole('heading', { name: 'Activity' })).toBeInTheDocument()
  })

  it('renders filter controls', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <ActivityPage />
      </Wrapper>,
    )
    expect(screen.getByRole('combobox', { name: 'Filter by ship' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Filter by event type' })).toBeInTheDocument()
  })

  it('renders activity rows when data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path.startsWith('/status/activity'))
        return Promise.resolve([
          {
            id: 1,
            timestamp: new Date().toISOString(),
            shipSymbol: 'SHIP-1',
            eventType: 'NAVIGATE',
            message: 'Navigating to X1-AB-01',
            jsonDetails: null,
          },
        ])
      return Promise.resolve([])
    })

    render(
      <Wrapper>
        <ActivityPage />
      </Wrapper>,
    )

    await waitFor(() => {
      // The event type appears in the table row - find within the table body
      const rows = screen.getAllByText('NAVIGATE')
      // Expect at least one (could be in the dropdown option + table cell)
      expect(rows.length).toBeGreaterThanOrEqual(1)
    })
    expect(screen.getByText('Navigating to X1-AB-01')).toBeInTheDocument()
  })

  it('shows pagination controls', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <ActivityPage />
      </Wrapper>,
    )
    expect(screen.getByRole('button', { name: 'Previous' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next' })).toBeInTheDocument()
  })
})

// ─── HealthPage ───────────────────────────────────────────────────────────────

import HealthPage from '../pages/HealthPage'

describe('HealthPage', () => {
  it('renders the heading', () => {
    mockApiFetch.mockResolvedValue({
      remaining: 60,
      limit: 60,
      burstRemaining: 20,
      burstLimit: 20,
      resetAt: new Date().toISOString(),
      limitType: null,
      totalRequests: 0,
      throttledCount: 0,
    })
    render(
      <Wrapper>
        <HealthPage />
      </Wrapper>,
    )
    expect(screen.getByRole('heading', { name: 'Health & Ops' })).toBeInTheDocument()
  })

  it('renders read-only banner', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <HealthPage />
      </Wrapper>,
    )
    expect(
      screen.getByText('Read-only — this UI cannot change game state'),
    ).toBeInTheDocument()
  })

  it('renders automation section when data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/health/automation')
        return Promise.resolve({ automationEnabled: true, isLeader: true })
      if (path === '/status/rate-limit')
        return Promise.resolve({
          remaining: 30,
          limit: 60,
          burstRemaining: 10,
          burstLimit: 20,
          resetAt: new Date().toISOString(),
          limitType: null,
          totalRequests: 100,
          throttledCount: 0,
        })
      return Promise.resolve([])
    })

    render(
      <Wrapper>
        <HealthPage />
      </Wrapper>,
    )

    await waitFor(() => expect(screen.getByText('Enabled')).toBeInTheDocument())
    expect(screen.getByText('Leader')).toBeInTheDocument()
  })

  it('renders API endpoint usage table', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/health/rate-limit/history')
        return Promise.resolve([
          {
            httpMethod: 'GET',
            endpoint: '/my/ships',
            calls: 42,
            lastCalledAt: new Date().toISOString(),
          },
        ])
      return Promise.resolve({
        remaining: 60,
        limit: 60,
        burstRemaining: 20,
        burstLimit: 20,
        resetAt: new Date().toISOString(),
        limitType: null,
        totalRequests: 0,
        throttledCount: 0,
      })
    })

    render(
      <Wrapper>
        <HealthPage />
      </Wrapper>,
    )

    await waitFor(() => expect(screen.getByText('/my/ships')).toBeInTheDocument())
    expect(screen.getByText('42')).toBeInTheDocument()
  })
})

// ─── SettingsPage ─────────────────────────────────────────────────────────────

import SettingsPage from '../pages/SettingsPage'

describe('SettingsPage', () => {
  it('renders the heading', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <SettingsPage />
      </Wrapper>,
    )
    expect(screen.getByRole('heading', { name: 'Settings' })).toBeInTheDocument()
  })

  it('renders read-only banner', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <SettingsPage />
      </Wrapper>,
    )
    expect(
      screen.getByText('Read-only mirror — values are set via the operator API'),
    ).toBeInTheDocument()
  })

  it('renders settings rows when data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/settings/')
        return Promise.resolve([
          {
            key: 'Automation.Enabled',
            value: 'true',
            type: 'bool',
            description: 'Master switch for automation.',
          },
        ])
      return Promise.resolve([])
    })

    render(
      <Wrapper>
        <SettingsPage />
      </Wrapper>,
    )

    await waitFor(() => expect(screen.getByText('Automation.Enabled')).toBeInTheDocument())
    expect(screen.getByText('Master switch for automation.')).toBeInTheDocument()
  })

  it('renders scheduled runs when present', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/scheduled')
        return Promise.resolve([
          {
            id: 'abc123',
            name: 'Run #2',
            strategyLabel: 'TRADE',
            scheduledSettingsJson: null,
            activatesAt: null,
            activatesOnNextRestart: true,
            createdAt: new Date().toISOString(),
          },
        ])
      return Promise.resolve([])
    })

    render(
      <Wrapper>
        <SettingsPage />
      </Wrapper>,
    )

    await waitFor(() => expect(screen.getByText('Run #2')).toBeInTheDocument())
    expect(screen.getByText('Activates on next restart')).toBeInTheDocument()
  })
})

// ─── ShipDetailPage ───────────────────────────────────────────────────────────

import ShipDetailPage from '../pages/ShipDetailPage'

describe('ShipDetailPage', () => {
  it('renders back link and ship symbol', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/status/ships')
        return Promise.resolve([
          {
            symbol: 'SHIP-1',
            systemSymbol: 'X1-AB',
            waypointSymbol: 'X1-AB-01',
            status: 'DOCKED',
            flightMode: 'CRUISE',
            fuelCurrent: 400,
            fuelCapacity: 400,
            cargoCurrent: 20,
            cargoCapacity: 60,
            arrivesAt: null,
            isInTransit: false,
            lastSyncedAt: new Date().toISOString(),
          },
        ])
      if (path === '/ships/SHIP-1/timeline') return Promise.resolve([])
      if (path === '/ships/SHIP-1/stats') return Promise.resolve({ ship: {}, ledger: [], summary: [] })
      if (path.startsWith('/status/activity')) return Promise.resolve([])
      return Promise.resolve([])
    })

    render(
      <WrapperWithRoute path="/fleet/:symbol" initialEntry="/fleet/SHIP-1">
        <ShipDetailPage />
      </WrapperWithRoute>,
    )

    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'SHIP-1' })).toBeInTheDocument(),
    )
    expect(screen.getByRole('link', { name: 'Back to Fleet' })).toBeInTheDocument()
  })

  it('shows not-found message for unknown ship', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/status/ships') return Promise.resolve([])
      return Promise.resolve([])
    })

    render(
      <WrapperWithRoute path="/fleet/:symbol" initialEntry="/fleet/UNKNOWN">
        <ShipDetailPage />
      </WrapperWithRoute>,
    )

    await waitFor(() =>
      expect(screen.getByText(/Ship "UNKNOWN" not found/)).toBeInTheDocument(),
    )
  })
})
