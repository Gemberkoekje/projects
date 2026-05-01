import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import { config } from '@/config'
import type {
  AgentDto,
  ShipDto,
  ContractDto,
  TradeOpportunityDto,
  RateLimitStatusDto,
  SystemAlertsDto,
  RunSummaryDto,
  ContractDeliverableDto,
  CreditSampleDto,
  AnomalyDto,
  TradeRouteDto,
} from '@/types'
import { AlertTriangle, CheckCircle2, XCircle } from 'lucide-react'
import { cn } from '@/lib/utils'

function formatCredits(n: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(n)
}

function formatDelta(n: number) {
  const sign = n >= 0 ? '+' : ''
  return `${sign}${formatCredits(n)}`
}

function formatDuration(startedAt: string) {
  const ms = Date.now() - new Date(startedAt).getTime()
  const h = Math.floor(ms / 3_600_000)
  const m = Math.floor((ms % 3_600_000) / 60_000)
  if (h > 0) return `${h}h ${m}m`
  return `${m}m`
}

function formatRelative(iso: string | null) {
  if (!iso) return '—'
  const ms = new Date(iso).getTime() - Date.now()
  const abs = Math.abs(ms)
  const h = Math.floor(abs / 3_600_000)
  const m = Math.floor((abs % 3_600_000) / 60_000)
  const suffix = ms < 0 ? ' ago' : ' from now'
  if (h > 0) return `${h}h ${m}m${suffix}`
  return `${m}m${suffix}`
}

/** Minimal inline SVG sparkline from an array of numbers. */
function Sparkline({ values, className }: { values: number[]; className?: string }) {
  if (values.length < 2) return null
  const min = Math.min(...values)
  const max = Math.max(...values)
  const range = max - min || 1
  const w = 120
  const h = 32
  const pts = values.map((v, i) => {
    const x = (i / (values.length - 1)) * w
    const y = h - ((v - min) / range) * h
    return `${x},${y}`
  })
  return (
    <svg
      width={w}
      height={h}
      viewBox={`0 0 ${w} ${h}`}
      className={cn('text-primary', className)}
      aria-hidden
    >
      <polyline
        points={pts.join(' ')}
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinejoin="round"
        strokeLinecap="round"
      />
    </svg>
  )
}

function StatCard({
  title,
  value,
  sub,
  children,
}: {
  title: string
  value: React.ReactNode
  sub?: React.ReactNode
  children?: React.ReactNode
}) {
  return (
    <div className="rounded-lg border border-border bg-background p-4 flex flex-col gap-1">
      <p className="text-xs text-muted-foreground uppercase tracking-wide">{title}</p>
      <p className="text-2xl font-bold leading-none">{value}</p>
      {sub && <p className="text-sm text-muted-foreground">{sub}</p>}
      {children}
    </div>
  )
}

function AlertDot({ active, label }: { active: boolean; label: string }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        active
          ? 'bg-destructive/15 text-destructive'
          : 'bg-status-green/15 text-status-green',
      )}
    >
      {active ? <XCircle size={12} aria-hidden /> : <CheckCircle2 size={12} aria-hidden />}
      {label}
    </span>
  )
}

export default function OverviewPage() {
  const agentQ = useQuery<AgentDto>({
    queryKey: ['agent'],
    queryFn: () => apiFetch('/status/agent'),
    refetchInterval: 30_000,
  })

  const shipsQ = useQuery<ShipDto[]>({
    queryKey: ['ships'],
    queryFn: () => apiFetch('/status/ships'),
    refetchInterval: 30_000,
  })

  const contractsQ = useQuery<ContractDto[]>({
    queryKey: ['contracts'],
    queryFn: () => apiFetch('/status/contracts'),
    refetchInterval: 60_000,
  })

  const tradeQ = useQuery<TradeOpportunityDto | null>({
    queryKey: ['trade-opportunities'],
    queryFn: async () => {
      const res = await fetch(`${config.apiBaseUrl}/status/trade-opportunities`, {
        headers: { 'X-Api-Key': config.dashboardApiKey },
      })
      if (res.status === 204) return null
      if (!res.ok) throw new Error(`API error ${res.status}`)
      return res.json() as Promise<TradeOpportunityDto>
    },
    refetchInterval: 60_000,
  })

  const rateLimitQ = useQuery<RateLimitStatusDto>({
    queryKey: ['rate-limit'],
    queryFn: () => apiFetch('/status/rate-limit'),
    refetchInterval: 15_000,
  })

  const alertsQ = useQuery<SystemAlertsDto>({
    queryKey: ['system-alerts'],
    queryFn: () => apiFetch('/status/system-alerts'),
    refetchInterval: 15_000,
  })

  const runsQ = useQuery<RunSummaryDto[]>({
    queryKey: ['runs'],
    queryFn: () => apiFetch('/runs/'),
    refetchInterval: 60_000,
  })

  const creditsHistoryQ = useQuery<CreditSampleDto[]>({
    queryKey: ['credits-history'],
    queryFn: () => apiFetch('/finance/credits-history'),
    refetchInterval: 60_000,
  })

  const anomalyQ = useQuery<AnomalyDto>({
    queryKey: ['anomalies'],
    queryFn: () => apiFetch('/status/anomalies'),
    refetchInterval: 60_000,
  })

  const topRoutesQ = useQuery<TradeRouteDto[]>({
    queryKey: ['top-trade-routes'],
    queryFn: () => apiFetch('/status/top-trade-routes?limit=5'),
    refetchInterval: 120_000,
  })

  const agent = agentQ.data
  const ships = shipsQ.data ?? []
  const contracts = contractsQ.data ?? []
  const trade = tradeQ.data ?? null
  const rateLimit = rateLimitQ.data
  const alerts = alertsQ.data
  const activeRun = runsQ.data?.find(r => r.endedAt === null) ?? null
  const creditSamples = creditsHistoryQ.data ?? []
  const anomaly = anomalyQ.data ?? null
  const topRoutes = topRoutesQ.data ?? []

  // Compute 24h delta from credits history
  const now = Date.now()
  const yesterday = now - 86_400_000
  const recentSamples = creditSamples.filter(s => new Date(s.recordedAt).getTime() >= yesterday)
  const delta24h =
    recentSamples.length >= 2
      ? recentSamples[recentSamples.length - 1].credits - recentSamples[0].credits
      : null

  const sparklineValues = creditSamples.map(s => s.credits)

  // Group ships by status
  const inTransit = ships.filter(s => s.isInTransit).length
  const docked = ships.filter(s => !s.isInTransit && s.status === 'DOCKED').length
  const inOrbit = ships.filter(s => !s.isInTransit && s.status === 'IN_ORBIT').length

  const isLoading = agentQ.isLoading || shipsQ.isLoading

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-bold">Overview</h1>

      {isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {/* Credits */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          title="Credits"
          value={agent ? formatCredits(agent.credits) : '—'}
          sub={
            delta24h !== null
              ? `24h: ${formatDelta(delta24h)}`
              : undefined
          }
        >
          {sparklineValues.length >= 2 && (
            <Sparkline values={sparklineValues} className="mt-1" />
          )}
        </StatCard>

        <StatCard
          title="Ships"
          value={agent?.shipCount ?? '—'}
          sub={`${inTransit} in transit · ${docked} docked · ${inOrbit} in orbit`}
        />

        <StatCard
          title="Active Run"
          value={activeRun?.name ?? '—'}
          sub={
            activeRun
              ? `${activeRun.strategyLabel} · ${formatDuration(activeRun.startedAt)} uptime`
              : undefined
          }
        />

        <StatCard
          title="Rate Limit"
          value={
            rateLimit
              ? `${rateLimit.remaining}/${rateLimit.limit}`
              : '—'
          }
          sub={
            rateLimit
              ? `Burst: ${rateLimit.burstRemaining}/${rateLimit.burstLimit} · ${rateLimit.throttledCount} throttled`
              : undefined
          }
        />
      </div>

      {/* Health strip */}
      {alerts && (
        <section aria-label="System health">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            System Health
          </h2>
          <div className="flex flex-wrap gap-2">
            <AlertDot active={alerts.apiUnavailable} label="API" />
            <AlertDot active={alerts.automationDisabled} label="Automation" />
            <AlertDot active={alerts.tokenResetMismatch} label="Token" />
            <AlertDot active={alerts.cacheDivergence} label="Cache" />
            <AlertDot active={alerts.contractDeadlinesApproaching} label="Contract deadline" />
            {alerts.resetUpcoming && (
              <span className="inline-flex items-center gap-1 rounded-full bg-status-yellow/15 text-status-yellow px-2 py-0.5 text-xs font-medium">
                <AlertTriangle size={12} aria-hidden />
                {`Server reset${alerts.nextReset ? ` ${formatRelative(alerts.nextReset)}` : ''}`}
              </span>
            )}
          </div>
        </section>
      )}

      {/* Active contracts */}
      {contracts.length > 0 && (
        <section aria-label="Active contracts">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Active Contracts
          </h2>
          <div className="flex flex-col gap-2">
            {contracts.filter(c => c.isAccepted && !c.isFulfilled).map(c => {
              const deliverables: ContractDeliverableDto[] = c.deliverablesJson
                ? (JSON.parse(c.deliverablesJson) as ContractDeliverableDto[])
                : []
              const totalRequired = deliverables.reduce((s, d) => s + d.unitsRequired, 0)
              const totalFulfilled = deliverables.reduce((s, d) => s + d.unitsFulfilled, 0)
              const pct = totalRequired > 0 ? Math.round((totalFulfilled / totalRequired) * 100) : 0
              return (
                <div
                  key={c.id}
                  className="rounded-lg border border-border bg-background p-3 flex flex-col gap-2"
                >
                  <div className="flex items-center justify-between text-sm">
                    <span className="font-medium">{c.type}</span>
                    <span className="text-muted-foreground text-xs">
                      {c.termsDeadline ? `Deadline ${formatRelative(c.termsDeadline)}` : ''}
                    </span>
                  </div>
                  {deliverables.map(d => (
                    <div key={d.tradeSymbol} className="flex flex-col gap-0.5">
                      <div className="flex justify-between text-xs text-muted-foreground">
                        <span>
                          {d.tradeSymbol} → {d.destinationSymbol}
                        </span>
                        <span>
                          {d.unitsFulfilled}/{d.unitsRequired}
                        </span>
                      </div>
                      <div className="h-1.5 rounded-full bg-muted overflow-hidden">
                        <div
                          className="h-full rounded-full bg-primary transition-all"
                          style={{
                            width: `${d.unitsRequired > 0 ? Math.round((d.unitsFulfilled / d.unitsRequired) * 100) : 0}%`,
                          }}
                        />
                      </div>
                    </div>
                  ))}
                  {deliverables.length === 0 && (
                    <div className="h-1.5 rounded-full bg-muted overflow-hidden">
                      <div
                        className="h-full rounded-full bg-primary"
                        style={{ width: `${pct}%` }}
                      />
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        </section>
      )}

      {/* Top trade opportunity */}
      {trade && (
        <section aria-label="Top trade opportunity">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Top Trade Opportunity
          </h2>
          <div className="rounded-lg border border-border bg-background p-3 text-sm flex flex-col gap-1">
            <div className="flex items-center justify-between">
              <span className="font-medium">{trade.tradeSymbol}</span>
              <span className="font-semibold text-status-green">
                +{formatCredits(trade.profitPerUnit)}/unit
              </span>
            </div>
            <p className="text-muted-foreground text-xs">
              {trade.buyWaypoint} → {trade.sellWaypoint} ·{' '}
              {trade.distanceJumps} jump{trade.distanceJumps !== 1 ? 's' : ''} ·
              score {Math.round(Number(trade.routeScore))}
            </p>
          </div>
        </section>
      )}

      {/* Anomaly warning */}
      {anomaly?.creditGrowthAnomaly && (
        <section aria-label="Credit growth anomaly">
          <div className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 flex items-start gap-2">
            <AlertTriangle size={16} className="text-destructive mt-0.5 shrink-0" aria-hidden />
            <div className="flex flex-col gap-0.5 text-sm">
              <span className="font-semibold text-destructive">Credit Growth Anomaly</span>
              <span className="text-muted-foreground text-xs">
                Recent rate: {formatCredits(Math.round(anomaly.recentCreditRatePerHour))}/h · 24h avg:{' '}
                {formatCredits(Math.round(anomaly.avgCreditRatePerHour))}/h
              </span>
            </div>
          </div>
        </section>
      )}

      {/* Decision candidates — top trade routes */}
      {topRoutes.length > 0 && (
        <section aria-label="Decision candidates">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Decision Candidates
          </h2>
          <div className="flex flex-col gap-2">
            {topRoutes.map((r, i) => (
              <div
                key={i}
                className={cn(
                  'rounded-lg border bg-background p-3 text-sm flex items-center justify-between gap-2',
                  i === 0 ? 'border-primary/50' : 'border-border',
                )}
              >
                <div className="flex flex-col gap-0.5">
                  <div className="flex items-center gap-2">
                    {i === 0 && (
                      <span className="text-xs bg-primary/15 text-primary rounded-full px-1.5 py-0.5 font-medium">
                        #1
                      </span>
                    )}
                    <span className="font-mono font-medium">{r.good}</span>
                  </div>
                  <span className="text-xs text-muted-foreground">
                    {r.buyWaypoint} → {r.sellWaypoint}
                  </span>
                </div>
                <span className="font-semibold text-status-green tabular-nums">
                  {formatCredits(Math.round(r.profitPerHour))}/h
                </span>
              </div>
            ))}
          </div>
        </section>
      )}
    </div>
  )
}
