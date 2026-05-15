import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import type {
  AgentDto,
  ShipDto,
  RateLimitStatusDto,
  SystemAlertsDto,
} from '@/types'
import { AlertTriangle, CheckCircle2, XCircle } from 'lucide-react'
import { cn } from '@/lib/utils'

function formatCredits(n: number) {
  return new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 }).format(n)
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

function StatCard({
  title,
  value,
  sub,
}: {
  title: string
  value: React.ReactNode
  sub?: React.ReactNode
}) {
  return (
    <div className="rounded-lg border border-border bg-background p-4 flex flex-col gap-1">
      <p className="text-xs text-muted-foreground uppercase tracking-wide">{title}</p>
      <p className="text-2xl font-bold leading-none">{value}</p>
      {sub && <p className="text-sm text-muted-foreground">{sub}</p>}
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

  const agent = agentQ.data
  const ships = shipsQ.data ?? []
  const rateLimit = rateLimitQ.data
  const alerts = alertsQ.data

  const inTransit = ships.filter(s => s.isInTransit).length
  const docked = ships.filter(s => !s.isInTransit && s.status === 'DOCKED').length
  const inOrbit = ships.filter(s => !s.isInTransit && s.status === 'IN_ORBIT').length

  const isLoading = agentQ.isLoading || shipsQ.isLoading

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-bold">Overview</h1>

      {isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <StatCard title="Credits" value={agent ? formatCredits(agent.credits) : '—'} />

        <StatCard
          title="Ships"
          value={agent?.shipCount ?? '—'}
          sub={`${inTransit} in transit · ${docked} docked · ${inOrbit} in orbit`}
        />

        <StatCard
          title="Rate Limit"
          value={rateLimit ? `${rateLimit.remaining}/${rateLimit.limit}` : '—'}
          sub={
            rateLimit
              ? `Burst: ${rateLimit.burstRemaining}/${rateLimit.burstLimit} · ${rateLimit.throttledCount} throttled`
              : undefined
          }
        />
      </div>

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
    </div>
  )
}
