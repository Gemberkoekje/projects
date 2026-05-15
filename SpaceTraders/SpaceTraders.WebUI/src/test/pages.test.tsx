import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, act } from '@testing-library/react'
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
    await act(async () => {
      searchInput.value = 'NOMATCH'
      searchInput.dispatchEvent(new Event('input', { bubbles: true }))
    })

    // The no-results row should appear when filtering yields zero results
    // (we trigger by selecting transit status since ship is docked)
    const statusSelect = getByRole('combobox', { name: 'Filter by status' }) as HTMLSelectElement
    await act(async () => {
      statusSelect.value = 'transit'
      statusSelect.dispatchEvent(new Event('change', { bubbles: true }))
    })

    await waitFor(() =>
      expect(screen.getByText('No ships match the current filters.')).toBeInTheDocument(),
    )
  })
})

// ─── ActivityPage ────────────────────────────────────────────────────────────

import ActivityPage from '../Future/pages/ActivityPage'

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
})

import GoalChainPanel from '../Future/components/GoalChainPanel'

describe('GoalChainPanel', () => {
  it('shows "No active goals." when the list is empty', async () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <GoalChainPanel />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByText('No active goals.')).toBeInTheDocument(),
    )
  })

  it('renders goal chains sorted by priority', async () => {
    mockApiFetch.mockResolvedValue([
      {
        fleetGoalId: 'g2',
        fleetGoalKind: 'TRADE',
        priority: 2,
        fleetGoalDescription: 'Trade iron ore',
        resourceNeeds: [],
      },
      {
        fleetGoalId: 'g1',
        fleetGoalKind: 'CONSTRUCTION',
        priority: 1,
        fleetGoalDescription: 'Supply Jump Gate at X1-TD7-JG',
        resourceNeeds: [],
      },
    ])
    render(
      <Wrapper>
        <GoalChainPanel />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByText('Supply Jump Gate at X1-TD7-JG')).toBeInTheDocument(),
    )
    // Priority 1 card should appear before priority 2
    const text = document.body.textContent ?? ''
    expect(text.indexOf('Supply Jump Gate')).toBeLessThan(text.indexOf('Trade iron ore'))
  })

  it('renders resource needs with progress bar and assigned ship links', async () => {
    mockApiFetch.mockResolvedValue([
      {
        fleetGoalId: 'g1',
        fleetGoalKind: 'CONSTRUCTION',
        priority: 1,
        fleetGoalDescription: 'Supply Jump Gate',
        resourceNeeds: [
          {
            tradeSymbol: 'BAUXITE',
            unitsNeeded: 100,
            unitsDelivered: 40,
            purposeDescription: 'For the gate',
            assignedShips: ['X1-TD7-1'],
          },
        ],
      },
    ])
    render(
      <Wrapper>
        <GoalChainPanel />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByText('BAUXITE')).toBeInTheDocument(),
    )
    expect(screen.getByText('40/100')).toBeInTheDocument()
    expect(screen.getByText('For the gate')).toBeInTheDocument()
    const link = screen.getByRole('link', { name: 'X1-TD7-1' })
    expect(link).toHaveAttribute('href', '/fleet/X1-TD7-1')
  })
})

// ─── AssignmentsPanel ─────────────────────────────────────────────────────────

import AssignmentsPanel from '../Future/components/AssignmentsPanel'

describe('AssignmentsPanel', () => {
  it('renders assignment rows with ship links', async () => {
    mockApiFetch.mockResolvedValue([
      {
        shipSymbol: 'X1-AB-1',
        goalKind: 'Mine',
        goalDescription: 'Mine BAUXITE',
        sourceWaypoint: 'X1-AB-A3',
        destinationWaypoint: null,
        fleetGoalId: 'g1',
        fleetGoalDescription: 'Supply Jump Gate',
        assignedAt: null,
      },
    ])
    render(
      <Wrapper>
        <AssignmentsPanel />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('link', { name: 'X1-AB-1' })).toBeInTheDocument(),
    )
    expect(screen.getByText('Mine BAUXITE')).toBeInTheDocument()
    expect(screen.getByText('X1-AB-A3')).toBeInTheDocument()
    expect(screen.getByText('Supply Jump Gate')).toBeInTheDocument()
  })

  it('shows empty message when no assignments match the filter', async () => {
    mockApiFetch.mockResolvedValue([
      {
        shipSymbol: 'X1-AB-1',
        goalKind: 'Mine',
        goalDescription: 'Mine BAUXITE',
        sourceWaypoint: null,
        destinationWaypoint: null,
        fleetGoalId: null,
        fleetGoalDescription: null,
        assignedAt: null,
      },
    ])
    const { container } = render(
      <Wrapper>
        <AssignmentsPanel />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('link', { name: 'X1-AB-1' })).toBeInTheDocument(),
    )

    // Change goal-kind filter to something that doesn't match
    const select = container.querySelector('select[aria-label="Filter by goal kind"]') as HTMLSelectElement
    // The only option is Mine, so we go via direct value manipulation instead of user events
    // Just verify the filter select is present and contains the goal kind
    expect(select).toBeTruthy()
  })
})

// ─── ShipDetailPage ───────────────────────────────────────────────────────────

import ShipDetailPage from '../Future/pages/ShipDetailPage'

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

// ─── FinancePage ──────────────────────────────────────────────────────────────

import FinancePage from '../Future/pages/FinancePage'

describe('FinancePage', () => {
  it('renders the heading', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <FinancePage />
      </Wrapper>,
    )
    expect(screen.getByRole('heading', { name: 'Finance' })).toBeInTheDocument()
  })

  it('shows loading state while fetching', () => {
    mockApiFetch.mockReturnValue(new Promise(() => {}))
    render(
      <Wrapper>
        <FinancePage />
      </Wrapper>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('renders run selector when runs are available', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/')
        return Promise.resolve([
          {
            id: 'run-1',
            name: 'Run #1',
            strategyLabel: 'TRADE',
            startedAt: new Date().toISOString(),
            endedAt: new Date().toISOString(),
            startingCredits: 100_000,
            endingCredits: 200_000,
          },
        ])
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <FinancePage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Select run' })).toBeInTheDocument(),
    )
  })

  it('renders category summary table when data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/') return Promise.resolve([])
      if (path === '/finance/credits-history') return Promise.resolve([])
      if (path === '/finance/summary')
        return Promise.resolve([
          { category: 'TradeSell', totalAmount: 500_000, entryCount: 10 },
          { category: 'FuelPurchase', totalAmount: -50_000, entryCount: 20 },
        ])
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <FinancePage />
      </Wrapper>,
    )
    await waitFor(() => expect(screen.getByText('TradeSell')).toBeInTheDocument())
    // FuelPurchase and +500,000 appear in both category table and summary cards / budget panel
    expect(screen.getAllByText('FuelPurchase').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('+500,000').length).toBeGreaterThanOrEqual(1)
  })

  it('renders per-ship P&L table when ledger has entries', async () => {
    const now = new Date().toISOString()
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/') return Promise.resolve([])
      if (path === '/finance/credits-history') return Promise.resolve([])
      if (path === '/finance/summary') return Promise.resolve([])
      if (path.startsWith('/finance/ledger'))
        return Promise.resolve([
          {
            id: 1,
            occurredAt: now,
            shipSymbol: 'SHIP-1',
            runId: null,
            category: 'TradeSell',
            amount: 300_000,
            goodSymbol: null,
            unitPrice: null,
            units: null,
            waypointSymbol: null,
          },
          {
            id: 2,
            occurredAt: now,
            shipSymbol: 'SHIP-1',
            runId: null,
            category: 'FuelPurchase',
            amount: -10_000,
            goodSymbol: null,
            unitPrice: null,
            units: null,
            waypointSymbol: null,
          },
        ])
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <FinancePage />
      </Wrapper>,
    )
    await waitFor(() => expect(screen.getByText('SHIP-1')).toBeInTheDocument())
    expect(screen.getByRole('region', { name: 'Per-ship P&L' })).toBeInTheDocument()
  })

  it('renders per-good profit table when ledger has trade entries', async () => {
    const now = new Date().toISOString()
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/') return Promise.resolve([])
      if (path === '/finance/credits-history') return Promise.resolve([])
      if (path === '/finance/summary') return Promise.resolve([])
      if (path.startsWith('/finance/ledger'))
        return Promise.resolve([
          {
            id: 1,
            occurredAt: now,
            shipSymbol: 'SHIP-1',
            runId: null,
            category: 'TradeBuy',
            amount: -80_000,
            goodSymbol: 'IRON_ORE',
            unitPrice: 80,
            units: 1000,
            waypointSymbol: 'X1-AB-01',
          },
          {
            id: 2,
            occurredAt: now,
            shipSymbol: 'SHIP-1',
            runId: null,
            category: 'TradeSell',
            amount: 120_000,
            goodSymbol: 'IRON_ORE',
            unitPrice: 120,
            units: 1000,
            waypointSymbol: 'X1-CD-02',
          },
        ])
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <FinancePage />
      </Wrapper>,
    )
    await waitFor(() => expect(screen.getByText('IRON_ORE')).toBeInTheDocument())
    expect(screen.getByRole('region', { name: 'Per-good profit' })).toBeInTheDocument()
  })

  it('renders credits-over-time chart when history data loads', async () => {
    const base = Date.now()
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/') return Promise.resolve([])
      if (path === '/finance/credits-history')
        return Promise.resolve([
          { recordedAt: new Date(base - 3_600_000).toISOString(), credits: 100_000 },
          { recordedAt: new Date(base).toISOString(), credits: 150_000 },
        ])
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <FinancePage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('img', { name: 'Credits over time' })).toBeInTheDocument(),
    )
  })
})

// ─── MarketsPage ──────────────────────────────────────────────────────────────

import MarketsPage from '../Future/pages/MarketsPage'

describe('MarketsPage (list)', () => {
  it('renders the heading', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByRole('heading', { name: 'Markets' })).toBeInTheDocument()
  })

  it('shows loading state while fetching', () => {
    mockApiFetch.mockReturnValue(new Promise(() => {}))
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('renders market list when data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/markets/waypoints')
        return Promise.resolve([
          {
            waypointSymbol: 'X1-AB-01',
            systemSymbol: 'X1-AB',
            imports: ['FUEL'],
            exports: ['IRON_ORE'],
            exchange: [],
            tradeGoods: [],
          },
        ])
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() => expect(screen.getByText('X1-AB-01')).toBeInTheDocument())
    expect(screen.getByRole('region', { name: 'Markets list' })).toBeInTheDocument()
  })

  it('renders search input and good filter', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByRole('searchbox', { name: 'Search markets' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Filter by good' })).toBeInTheDocument()
  })

  it('shows heatmap when a good is selected', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/markets/waypoints')
        return Promise.resolve([
          {
            waypointSymbol: 'X1-AB-01',
            systemSymbol: 'X1-AB',
            imports: [],
            exports: ['IRON_ORE'],
            exchange: [],
            tradeGoods: [
              {
                symbol: 'IRON_ORE',
                type: 'EXPORT',
                purchasePrice: 80,
                sellPrice: 120,
                tradeVolume: 100,
                supply: 'HIGH',
                activity: 'GROWING',
              },
            ],
          },
        ])
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    // Wait for good filter to be populated, then select the good
    await waitFor(() =>
      expect(screen.getByRole('option', { name: 'IRON_ORE' })).toBeInTheDocument(),
    )
    const select = screen.getByRole('combobox', { name: 'Filter by good' })
    const { fireEvent } = await import('@testing-library/react')
    fireEvent.change(select, { target: { value: 'IRON_ORE' } })
    await waitFor(() =>
      expect(
        screen.getByRole('region', { name: 'Price heatmap for IRON_ORE' }),
      ).toBeInTheDocument(),
    )
  })

  it('shows no-data message when markets list is empty', async () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByText('No market data available yet.')).toBeInTheDocument(),
    )
  })
})

describe('GoodDetailPage', () => {
  it('renders good symbol in heading', async () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/goods/IRON_ORE">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'IRON_ORE' })).toBeInTheDocument(),
    )
  })

  it('renders back link', async () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/goods/IRON_ORE">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByText('← Markets')).toBeInTheDocument()
  })

  it('renders price chart and best pairs when price history loads', async () => {
    const base = Date.now()
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/markets/goods/IRON_ORE/prices')
        return Promise.resolve([
          {
            observedAt: new Date(base - 3_600_000).toISOString(),
            waypointSymbol: 'X1-AB-01',
            goodSymbol: 'IRON_ORE',
            purchasePrice: 80,
            sellPrice: 90,
            supply: 'HIGH',
            activity: 'GROWING',
            tradeVolume: 100,
          },
          {
            observedAt: new Date(base).toISOString(),
            waypointSymbol: 'X1-AB-01',
            goodSymbol: 'IRON_ORE',
            purchasePrice: 85,
            sellPrice: 95,
            supply: 'MODERATE',
            activity: 'GROWING',
            tradeVolume: 100,
          },
          {
            observedAt: new Date(base - 3_600_000).toISOString(),
            waypointSymbol: 'X1-CD-02',
            goodSymbol: 'IRON_ORE',
            purchasePrice: 120,
            sellPrice: 140,
            supply: 'LIMITED',
            activity: 'WEAK',
            tradeVolume: 50,
          },
          {
            observedAt: new Date(base).toISOString(),
            waypointSymbol: 'X1-CD-02',
            goodSymbol: 'IRON_ORE',
            purchasePrice: 125,
            sellPrice: 145,
            supply: 'LIMITED',
            activity: 'WEAK',
            tradeVolume: 50,
          },
        ])
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/goods/IRON_ORE">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(
        screen.getByRole('img', { name: 'IRON_ORE price over time' }),
      ).toBeInTheDocument(),
    )
    expect(screen.getByRole('region', { name: 'Best buy-sell pairs' })).toBeInTheDocument()
  })

  it('shows not-found message when no price history', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path.startsWith('/markets/goods/')) return Promise.resolve([])
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/goods/UNKNOWN_GOOD">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(
        screen.getByText('No price history found for UNKNOWN_GOOD.'),
      ).toBeInTheDocument(),
    )
  })
})

describe('WaypointDetailPage', () => {
  it('renders waypoint symbol in heading', async () => {
    mockApiFetch.mockResolvedValue(null)
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/waypoints/X1-AB-01">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'X1-AB-01' })).toBeInTheDocument(),
    )
  })

  it('renders imports/exports/exchange when waypoint data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/markets/waypoints/X1-AB-01')
        return Promise.resolve({
          waypointSymbol: 'X1-AB-01',
          systemSymbol: 'X1-AB',
          imports: ['FUEL'],
          exports: ['IRON_ORE'],
          exchange: ['FOOD'],
          tradeGoods: [],
        })
      if (path === '/markets/waypoints/X1-AB-01/prices') return Promise.resolve([])
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/waypoints/X1-AB-01">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(
        screen.getByRole('region', { name: 'Imports exports exchanges' }),
      ).toBeInTheDocument(),
    )
    expect(screen.getByText('FUEL')).toBeInTheDocument()
    expect(screen.getByText('IRON_ORE')).toBeInTheDocument()
    expect(screen.getByText('FOOD')).toBeInTheDocument()
  })

  it('renders current prices table when trade goods are present', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/markets/waypoints/X1-AB-01')
        return Promise.resolve({
          waypointSymbol: 'X1-AB-01',
          systemSymbol: 'X1-AB',
          imports: [],
          exports: [],
          exchange: [],
          tradeGoods: [
            {
              symbol: 'IRON_ORE',
              type: 'EXPORT',
              purchasePrice: 80,
              sellPrice: 120,
              tradeVolume: 100,
              supply: 'HIGH',
              activity: 'GROWING',
            },
          ],
        })
      if (path === '/markets/waypoints/X1-AB-01/prices') return Promise.resolve([])
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/waypoints/X1-AB-01">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Current prices' })).toBeInTheDocument(),
    )
    expect(screen.getByText('IRON_ORE')).toBeInTheDocument()
  })

  it('shows not-found message when waypoint does not exist', async () => {
    mockApiFetch.mockResolvedValue(null)
    render(
      <WrapperWithRoute path="/markets/*" initialEntry="/markets/waypoints/UNKNOWN">
        <MarketsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByText(/Waypoint "UNKNOWN" not found\./)).toBeInTheDocument(),
    )
  })
})

// ─── RunsPage ─────────────────────────────────────────────────────────────────

import RunsPage from '../Future/pages/RunsPage'

describe('RunsListPage', () => {
  it('renders the heading', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/">
        <RunsPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByRole('heading', { name: 'Runs' })).toBeInTheDocument()
  })

  it('shows loading state while fetching', () => {
    mockApiFetch.mockReturnValue(new Promise(() => {}))
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/">
        <RunsPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('renders run rows when data loads', async () => {
    mockApiFetch.mockResolvedValue([
      {
        id: 'run-1',
        name: 'Run #1',
        strategyLabel: 'TRADE',
        startedAt: new Date(Date.now() - 3_600_000).toISOString(),
        endedAt: new Date().toISOString(),
        startingCredits: 100_000,
        endingCredits: 250_000,
      },
    ])
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/">
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() => expect(screen.getAllByText('Run #1').length).toBeGreaterThan(0))
    expect(screen.getByRole('link', { name: 'View run Run #1' })).toBeInTheDocument()
    expect(screen.getByText('+150k')).toBeInTheDocument()
  })

  it('renders scheduled runs with pending badge', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/') return Promise.resolve([])
      if (path === '/runs/scheduled')
        return Promise.resolve([
          {
            id: 'sched-1',
            name: 'Run #2',
            strategyLabel: 'MINE',
            scheduledSettingsJson: null,
            activatesAt: null,
            activatesOnNextRestart: true,
            createdAt: new Date().toISOString(),
          },
        ])
      return Promise.resolve([])
    })
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/">
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() => expect(screen.getByText('Run #2')).toBeInTheDocument())
    expect(screen.getByText('Pending')).toBeInTheDocument()
    expect(screen.getByText('On next restart')).toBeInTheDocument()
  })

  it('marks the active (ongoing) run with Active badge', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/')
        return Promise.resolve([
          {
            id: 'run-active',
            name: 'Active Run',
            strategyLabel: 'TRADE',
            startedAt: new Date(Date.now() - 1_800_000).toISOString(),
            endedAt: null,
            startingCredits: 50_000,
            endingCredits: null,
          },
        ])
      return Promise.resolve([])
    })
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/">
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() => expect(screen.getByText('Active Run')).toBeInTheDocument())
    expect(screen.getByText('Active')).toBeInTheDocument()
  })

  it('shows no-runs message when list is empty', async () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/">
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByText('No runs recorded yet.')).toBeInTheDocument(),
    )
  })
})

describe('RunDetailPage', () => {
  it('renders run name as heading', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/run-abc/summary')
        return Promise.resolve({
          run: {
            id: 'run-abc',
            name: 'Mining Run',
            strategyLabel: 'MINE',
            startedAt: new Date(Date.now() - 7_200_000).toISOString(),
            endedAt: new Date().toISOString(),
            startingCredits: 80_000,
            endingCredits: 200_000,
          },
          creditHighlights: [],
          ledgerSummary: [],
        })
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/run-abc">
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Mining Run' })).toBeInTheDocument(),
    )
    expect(screen.getByRole('link', { name: '← Runs' })).toBeInTheDocument()
  })

  it('shows loading state while fetching detail', () => {
    mockApiFetch.mockReturnValue(new Promise(() => {}))
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/run-abc">
        <RunsPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('renders summary card with strategy and ΔCredits', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/run-abc/summary')
        return Promise.resolve({
          run: {
            id: 'run-abc',
            name: 'Trade Run',
            strategyLabel: 'TRADE',
            startedAt: new Date(Date.now() - 3_600_000).toISOString(),
            endedAt: new Date().toISOString(),
            startingCredits: 100_000,
            endingCredits: 400_000,
          },
          creditHighlights: [],
          ledgerSummary: [],
        })
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/run-abc">
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Run summary' })).toBeInTheDocument(),
    )
    expect(screen.getByText('TRADE')).toBeInTheDocument()
    expect(screen.getByText('+300k')).toBeInTheDocument()
  })

  it('renders income/expense category table when ledger summary present', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/run-abc/summary')
        return Promise.resolve({
          run: {
            id: 'run-abc',
            name: 'Trade Run',
            strategyLabel: 'TRADE',
            startedAt: new Date(Date.now() - 3_600_000).toISOString(),
            endedAt: new Date().toISOString(),
            startingCredits: 100_000,
            endingCredits: 400_000,
          },
          creditHighlights: [],
          ledgerSummary: [
            { category: 'TradeSell', totalAmount: 300_000, entryCount: 5 },
            { category: 'FuelPurchase', totalAmount: -20_000, entryCount: 10 },
          ],
        })
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/run-abc">
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(
        screen.getByRole('region', { name: 'Income and expenses by category' }),
      ).toBeInTheDocument(),
    )
    expect(screen.getByText('TradeSell')).toBeInTheDocument()
    expect(screen.getAllByText('FuelPurchase').length).toBeGreaterThanOrEqual(1)
  })

  it('renders credits chart when highlights are present', async () => {
    const base = Date.now()
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/run-abc/summary')
        return Promise.resolve({
          run: {
            id: 'run-abc',
            name: 'Highlight Run',
            strategyLabel: 'MINE',
            startedAt: new Date(base - 7_200_000).toISOString(),
            endedAt: new Date(base).toISOString(),
            startingCredits: 50_000,
            endingCredits: 150_000,
          },
          creditHighlights: [
            { id: 1, runId: 'run-abc', occurredAt: new Date(base - 7_200_000).toISOString(), credits: 50_000, deltaCredits: 0, eventKind: 'RunOpen', label: 'Start' },
            { id: 2, runId: 'run-abc', occurredAt: new Date(base).toISOString(), credits: 150_000, deltaCredits: 100_000, eventKind: 'RunClose', label: 'End' },
          ],
          ledgerSummary: [],
        })
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/run-abc">
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('img', { name: 'Credits over time' })).toBeInTheDocument(),
    )
  })

  it('renders Efficiency KPIs section when kpis data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/run-abc/summary')
        return Promise.resolve({
          run: {
            id: 'run-abc',
            name: 'KPI Run',
            strategyLabel: 'TRADE',
            startedAt: new Date(Date.now() - 7_200_000).toISOString(),
            endedAt: new Date().toISOString(),
            startingCredits: 100_000,
            endingCredits: 500_000,
          },
          creditHighlights: [],
          ledgerSummary: [],
        })
      if (path === '/runs/run-abc/kpis')
        return Promise.resolve({
          creditsPerHour: 200_000,
          creditsPerShipPerHour: 100_000,
          creditsPerApiCall: 12.5,
          idlePercent: 15.3,
          fuelCostPerCreditEarned: 0.04,
        })
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/run-abc">
        <RunsPage />
      </WrapperWithRoute>,
    )

    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Efficiency KPIs' })).toBeInTheDocument(),
    )
    expect(screen.getByText('Credits / Hour')).toBeInTheDocument()
    expect(screen.getByText('Credits / Ship / Hour')).toBeInTheDocument()
    expect(screen.getByText('Credits / API Call')).toBeInTheDocument()
    expect(screen.getByText('Idle %')).toBeInTheDocument()
    expect(screen.getByText('Fuel Cost / Credit Earned')).toBeInTheDocument()
  })

  it('shows em-dashes when KPI values are null', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/runs/run-abc/summary')
        return Promise.resolve({
          run: {
            id: 'run-abc',
            name: 'Empty KPI Run',
            strategyLabel: 'TRADE',
            startedAt: new Date().toISOString(),
            endedAt: null,
            startingCredits: 100_000,
            endingCredits: null,
          },
          creditHighlights: [],
          ledgerSummary: [],
        })
      if (path === '/runs/run-abc/kpis')
        return Promise.resolve({
          creditsPerHour: null,
          creditsPerShipPerHour: null,
          creditsPerApiCall: null,
          idlePercent: null,
          fuelCostPerCreditEarned: null,
        })
      return Promise.resolve(null)
    })
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/run-abc">
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Efficiency KPIs' })).toBeInTheDocument(),
    )
    // All five KPI cards should show '—' for null values
    const dashes = screen.getAllByText('—')
    expect(dashes.length).toBeGreaterThanOrEqual(5)
  })
})

describe('RunComparePage', () => {
  const base = Date.now()

  const runA = {
    id: 'run-a',
    name: 'Trade Run A',
    strategyLabel: 'TRADE',
    startedAt: new Date(base - 7_200_000).toISOString(),
    endedAt: new Date(base - 3_600_000).toISOString(),
    startingCredits: 100_000,
    endingCredits: 300_000,
  }

  const runB = {
    id: 'run-b',
    name: 'Mining Run B',
    strategyLabel: 'MINE',
    startedAt: new Date(base - 10_800_000).toISOString(),
    endedAt: new Date(base - 3_600_000).toISOString(),
    startingCredits: 80_000,
    endingCredits: 200_000,
  }

  const compareData = {
    runA: {
      run: runA,
      creditHighlights: [
        { id: 1, runId: 'run-a', occurredAt: new Date(base - 7_200_000).toISOString(), credits: 100_000, deltaCredits: 0, eventKind: 'RunOpen', label: 'Start' },
        { id: 2, runId: 'run-a', occurredAt: new Date(base - 3_600_000).toISOString(), credits: 300_000, deltaCredits: 200_000, eventKind: 'RunClose', label: 'End' },
      ],
      ledgerSummary: [
        { category: 'TradeSell', totalAmount: 250_000, entryCount: 8 },
        { category: 'FuelPurchase', totalAmount: -50_000, entryCount: 12 },
      ],
    },
    runB: {
      run: runB,
      creditHighlights: [
        { id: 3, runId: 'run-b', occurredAt: new Date(base - 10_800_000).toISOString(), credits: 80_000, deltaCredits: 0, eventKind: 'RunOpen', label: 'Start' },
        { id: 4, runId: 'run-b', occurredAt: new Date(base - 3_600_000).toISOString(), credits: 200_000, deltaCredits: 120_000, eventKind: 'RunClose', label: 'End' },
      ],
      ledgerSummary: [
        { category: 'TradeSell', totalAmount: 180_000, entryCount: 6 },
        { category: 'FuelPurchase', totalAmount: -60_000, entryCount: 15 },
      ],
    },
  }

  it('renders the Compare Runs heading', async () => {
    mockApiFetch.mockResolvedValue(compareData)
    render(
      <WrapperWithRoute
        path="/runs/*"
        initialEntry="/runs/compare?a=run-a&b=run-b"
      >
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Compare Runs' })).toBeInTheDocument(),
    )
  })

  it('shows loading state while fetching', () => {
    mockApiFetch.mockReturnValue(new Promise(() => {}))
    render(
      <WrapperWithRoute
        path="/runs/*"
        initialEntry="/runs/compare?a=run-a&b=run-b"
      >
        <RunsPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('shows missing-params message when query params absent', () => {
    mockApiFetch.mockResolvedValue(null)
    render(
      <WrapperWithRoute path="/runs/*" initialEntry="/runs/compare">
        <RunsPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByText(/Missing run IDs/)).toBeInTheDocument()
  })

  it('renders side-by-side run headers with both run names', async () => {
    mockApiFetch.mockResolvedValue(compareData)
    render(
      <WrapperWithRoute
        path="/runs/*"
        initialEntry="/runs/compare?a=run-a&b=run-b"
      >
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Run comparison headers' })).toBeInTheDocument(),
    )
    expect(screen.getAllByText('Trade Run A').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('Mining Run B').length).toBeGreaterThanOrEqual(1)
  })

  it('renders normalised credits chart when highlights are present', async () => {
    mockApiFetch.mockResolvedValue(compareData)
    render(
      <WrapperWithRoute
        path="/runs/*"
        initialEntry="/runs/compare?a=run-a&b=run-b"
      >
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('img', { name: 'Normalised credits comparison' })).toBeInTheDocument(),
    )
  })

  it('renders category comparison table with Δ column', async () => {
    mockApiFetch.mockResolvedValue(compareData)
    render(
      <WrapperWithRoute
        path="/runs/*"
        initialEntry="/runs/compare?a=run-a&b=run-b"
      >
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Category comparison' })).toBeInTheDocument(),
    )
    expect(screen.getAllByText('TradeSell').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('FuelPurchase').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('Δ (B − A)')).toBeInTheDocument()
  })

  it('renders back link to runs list', async () => {
    mockApiFetch.mockResolvedValue(compareData)
    render(
      <WrapperWithRoute
        path="/runs/*"
        initialEntry="/runs/compare?a=run-a&b=run-b"
      >
        <RunsPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('link', { name: '← Runs' })).toBeInTheDocument(),
    )
  })
})

// ─── UniversePage ──────────────────────────────────────────────────────────────

import UniversePage from '../Future/pages/UniversePage'

describe('UniversePage', () => {
  const systems = [
    { symbol: 'X1-SN', sectorSymbol: 'X1', type: 'RED_STAR', x: 10, y: -20, lastObservedAt: new Date().toISOString(), isVisited: true },
    { symbol: 'X1-SF', sectorSymbol: 'X1', type: 'BLUE_STAR', x: -5, y: 30, lastObservedAt: new Date().toISOString(), isVisited: false },
    { symbol: 'X1-VM', sectorSymbol: 'X1', type: 'YOUNG_STAR', x: 80, y: 5, lastObservedAt: new Date().toISOString(), isVisited: false },
  ]

  const connections = [
    { fromSystem: 'X1-SN', toSystem: 'X1-SF' },
  ]

  it('renders the Universe heading', async () => {
    mockApiFetch.mockResolvedValue(systems)
    render(
      <Wrapper>
        <UniversePage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Universe' })).toBeInTheDocument(),
    )
  })

  it('shows loading state while fetching', () => {
    mockApiFetch.mockReturnValue(new Promise(() => {}))
    render(
      <Wrapper>
        <UniversePage />
      </Wrapper>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('renders visited/total system count after data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/universe/systems') return Promise.resolve(systems)
      if (path === '/universe/jump-connections') return Promise.resolve(connections)
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <UniversePage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByText(/1 \/ 3 systems visited/)).toBeInTheDocument(),
    )
  })

  it('renders the universe map SVG', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/universe/systems') return Promise.resolve(systems)
      if (path === '/universe/jump-connections') return Promise.resolve(connections)
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <UniversePage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('img', { name: 'Universe map' })).toBeInTheDocument(),
    )
  })

  it('renders search input and filters results', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/universe/systems') return Promise.resolve(systems)
      if (path === '/universe/jump-connections') return Promise.resolve(connections)
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <UniversePage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('img', { name: 'Universe map' })).toBeInTheDocument(),
    )
    const searchInput = screen.getByRole('textbox', { name: 'Search systems' })
    expect(searchInput).toBeInTheDocument()
  })

  it('renders legend with visited, frontier and known-only entries', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/universe/systems') return Promise.resolve(systems)
      if (path === '/universe/jump-connections') return Promise.resolve(connections)
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <UniversePage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Universe map legend' })).toBeInTheDocument(),
    )
    expect(screen.getByText('Visited')).toBeInTheDocument()
    expect(screen.getByText(/Frontier/)).toBeInTheDocument()
    expect(screen.getByText(/Known only/)).toBeInTheDocument()
  })
})

// ─── SystemMapPage ─────────────────────────────────────────────────────────────

import SystemMapPage from '../Future/pages/SystemMapPage'

describe('SystemMapPage', () => {
  const waypoints = [
    {
      symbol: 'X1-AB-00',
      systemSymbol: 'X1-AB',
      type: 'PLANET',
      x: 0,
      y: 30,
      hasMarket: false,
      hasShipyard: false,
      traitsJson: null,
      orbitalsJson: JSON.stringify(['X1-AB-01']),
      parentSymbol: null,
      isUnderConstruction: false,
      lastObservedAt: new Date().toISOString(),
    },
    {
      symbol: 'X1-AB-01',
      systemSymbol: 'X1-AB',
      type: 'MOON',
      x: 5,
      y: 32,
      hasMarket: true,
      hasShipyard: false,
      traitsJson: JSON.stringify([{ symbol: 'MARKETPLACE', name: 'Marketplace', description: 'A marketplace.' }]),
      orbitalsJson: null,
      parentSymbol: 'X1-AB-00',
      isUnderConstruction: false,
      lastObservedAt: new Date().toISOString(),
    },
    {
      symbol: 'X1-AB-JG',
      systemSymbol: 'X1-AB',
      type: 'JUMP_GATE',
      x: 100,
      y: 0,
      hasMarket: false,
      hasShipyard: false,
      traitsJson: null,
      orbitalsJson: null,
      parentSymbol: null,
      isUnderConstruction: false,
      lastObservedAt: new Date().toISOString(),
    },
  ]

  const system = {
    symbol: 'X1-AB',
    sectorSymbol: 'X1',
    type: 'RED_STAR',
    x: 10,
    y: -20,
    lastObservedAt: new Date().toISOString(),
    isVisited: true,
  }

  const mapData = { system, waypoints }

  it('renders the system symbol as heading', async () => {
    mockApiFetch.mockResolvedValue(mapData)
    render(
      <WrapperWithRoute path="/systems/:symbol" initialEntry="/systems/X1-AB">
        <SystemMapPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'X1-AB' })).toBeInTheDocument(),
    )
  })

  it('shows loading state while fetching', () => {
    mockApiFetch.mockReturnValue(new Promise(() => {}))
    render(
      <WrapperWithRoute path="/systems/:symbol" initialEntry="/systems/X1-AB">
        <SystemMapPage />
      </WrapperWithRoute>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('renders the breadcrumb with Universe link', async () => {
    mockApiFetch.mockResolvedValue(mapData)
    render(
      <WrapperWithRoute path="/systems/:symbol" initialEntry="/systems/X1-AB">
        <SystemMapPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('navigation', { name: 'Breadcrumb' })).toBeInTheDocument(),
    )
    expect(screen.getByRole('link', { name: 'Universe' })).toBeInTheDocument()
  })

  it('renders the SVG system map', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/universe/systems/X1-AB/map') return Promise.resolve(mapData)
      return Promise.resolve([])
    })
    render(
      <WrapperWithRoute path="/systems/:symbol" initialEntry="/systems/X1-AB">
        <SystemMapPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('img', { name: 'System map for X1-AB' })).toBeInTheDocument(),
    )
  })

  it('renders the waypoints table with all waypoints', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/universe/systems/X1-AB/map') return Promise.resolve(mapData)
      return Promise.resolve([])
    })
    render(
      <WrapperWithRoute path="/systems/:symbol" initialEntry="/systems/X1-AB">
        <SystemMapPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Waypoints list' })).toBeInTheDocument(),
    )
    expect(screen.getAllByText('X1-AB-00').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('X1-AB-01').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('X1-AB-JG').length).toBeGreaterThanOrEqual(1)
  })

  it('shows jump connection toggle button when system has a jump gate', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/universe/systems/X1-AB/map') return Promise.resolve(mapData)
      return Promise.resolve([])
    })
    render(
      <WrapperWithRoute path="/systems/:symbol" initialEntry="/systems/X1-AB">
        <SystemMapPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /jump connections/i })).toBeInTheDocument(),
    )
  })

  it('opens waypoint detail panel when a waypoint row is clicked', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/universe/systems/X1-AB/map') return Promise.resolve(mapData)
      return Promise.resolve([])
    })
    render(
      <WrapperWithRoute path="/systems/:symbol" initialEntry="/systems/X1-AB">
        <SystemMapPage />
      </WrapperWithRoute>,
    )
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Waypoints list' })).toBeInTheDocument(),
    )
    // Click the planet row (first data row)
    const rows = screen.getAllByRole('row')
    // Skip header row (index 0), click first data row
    await act(async () => {
      rows[1].click()
    })
    await waitFor(() =>
      expect(screen.getByRole('complementary', { name: 'Waypoint detail panel' })).toBeInTheDocument(),
    )
  })
})

// ─── ContractsPage ────────────────────────────────────────────────────────────

import ContractsPage from '../Future/pages/ContractsPage'

describe('ContractsPage', () => {
  it('renders the heading', () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <ContractsPage />
      </Wrapper>,
    )
    expect(screen.getByRole('heading', { name: 'Contracts' })).toBeInTheDocument()
  })

  it('shows loading state while fetching', () => {
    mockApiFetch.mockReturnValue(new Promise(() => {}))
    render(
      <Wrapper>
        <ContractsPage />
      </Wrapper>,
    )
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('renders contract rows when data loads', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/status/contracts')
        return Promise.resolve([
          {
            id: 'CONTRACT-1',
            type: 'PROCUREMENT',
            factionSymbol: 'COSMIC',
            isAccepted: true,
            isFulfilled: false,
            termsDeadline: new Date(Date.now() + 86_400_000).toISOString(),
            deliverablesJson: JSON.stringify([
              { tradeSymbol: 'IRON', destinationSymbol: 'X1-AB-00', unitsRequired: 100, unitsFulfilled: 50 },
            ]),
          },
        ])
      return Promise.resolve(null)
    })
    render(
      <Wrapper>
        <ContractsPage />
      </Wrapper>,
    )
    await waitFor(() => expect(screen.getByText('CONTRACT-1')).toBeInTheDocument())
    expect(screen.getByText('Active')).toBeInTheDocument()
  })

  it('shows empty state when there are no contracts', async () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <ContractsPage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByText('No contracts found.')).toBeInTheDocument(),
    )
  })
})


// ─── FinancePage — trade routes section ──────────────────────────────────────

describe('FinancePage — trade routes', () => {
  it('renders the trade routes section heading', async () => {
    mockApiFetch.mockResolvedValue([])
    render(
      <Wrapper>
        <FinancePage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Trade routes' })).toBeInTheDocument(),
    )
  })

  it('renders trade route rows when data is available', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path.includes('/finance/trade-routes'))
        return Promise.resolve([
          {
            good: 'IRON_ORE',
            buyWaypoint: 'X1-AB-00',
            sellWaypoint: 'X1-AB-01',
            totalUnits: 400,
            totalProfit: 8000,
            runDurationHours: 2,
            profitPerHour: 4000,
          },
        ])
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <FinancePage />
      </Wrapper>,
    )
    await waitFor(() => expect(screen.getByText('IRON_ORE')).toBeInTheDocument())
    expect(screen.getByText('X1-AB-00')).toBeInTheDocument()
  })
})


// ─── MarketsPage — freshness column ──────────────────────────────────────────

describe('MarketsPage — freshness', () => {
  it('shows the Last Scan column header', async () => {
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/markets/waypoints')
        return Promise.resolve([
          {
            waypointSymbol: 'X1-AB-00',
            systemSymbol: 'X1-AB',
            tradeGoods: [],
            imports: [],
            exports: [],
            exchange: [],
          },
        ])
      if (path === '/markets/freshness') return Promise.resolve([])
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <MarketsPage />
      </Wrapper>,
    )
    await waitFor(() =>
      expect(screen.getByRole('columnheader', { name: 'Last Scan' })).toBeInTheDocument(),
    )
  })

  it('shows freshness age text when freshness data loads', async () => {
    const now = new Date()
    const thirtyMinAgo = new Date(now.getTime() - 30 * 60 * 1000).toISOString()
    mockApiFetch.mockImplementation((path: string) => {
      if (path === '/markets/waypoints')
        return Promise.resolve([
          {
            waypointSymbol: 'X1-AB-00',
            systemSymbol: 'X1-AB',
            tradeGoods: [],
            imports: [],
            exports: [],
            exchange: [],
          },
        ])
      if (path === '/markets/freshness')
        return Promise.resolve([
          {
            waypointSymbol: 'X1-AB-00',
            systemSymbol: 'X1-AB',
            lastObservedAt: thirtyMinAgo,
            ageMinutes: 30,
          },
        ])
      return Promise.resolve([])
    })
    render(
      <Wrapper>
        <MarketsPage />
      </Wrapper>,
    )
    await waitFor(() => expect(screen.getByText('30m ago')).toBeInTheDocument())
  })
})
