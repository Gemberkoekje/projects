import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import type { RateLimitStatusDto, AutomationHealthDto, ApiEndpointUsageDto } from '@/types'
import { cn } from '@/lib/utils'

function formatTs(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function formatRelative(iso: string) {
  const ms = Date.now() - new Date(iso).getTime()
  const s = Math.floor(ms / 1_000)
  if (s < 60) return `${s}s ago`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}m ago`
  const h = Math.floor(m / 60)
  return `${h}h ${m % 60}m ago`
}

function StatusBadge({ ok, labelTrue, labelFalse }: { ok: boolean; labelTrue: string; labelFalse: string }) {
  return (
    <span
      className={cn(
        'rounded-full px-2 py-0.5 text-xs font-medium',
        ok
          ? 'bg-status-green/15 text-status-green'
          : 'bg-destructive/15 text-destructive',
      )}
    >
      {ok ? labelTrue : labelFalse}
    </span>
  )
}

export default function HealthPage() {
  const rateLimitQ = useQuery<RateLimitStatusDto>({
    queryKey: ['rate-limit'],
    queryFn: () => apiFetch('/status/rate-limit'),
    refetchInterval: 10_000,
  })

  const automationQ = useQuery<AutomationHealthDto>({
    queryKey: ['health-automation'],
    queryFn: () => apiFetch('/health/automation'),
    refetchInterval: 15_000,
  })

  const usageQ = useQuery<ApiEndpointUsageDto[]>({
    queryKey: ['rate-limit-history'],
    queryFn: () => apiFetch('/health/rate-limit/history'),
    refetchInterval: 30_000,
  })

  const rateLimit = rateLimitQ.data
  const automation = automationQ.data
  const usage = usageQ.data ?? []

  const isLoading = rateLimitQ.isLoading || automationQ.isLoading

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Health &amp; Ops</h1>
        <span className="rounded-full bg-muted text-muted-foreground px-3 py-1 text-xs font-medium">
          Read-only — this UI cannot change game state
        </span>
      </div>

      {isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {/* Automation status */}
      {automation && (
        <section aria-label="Automation status">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Automation
          </h2>
          <div className="flex gap-3 flex-wrap">
            <div className="rounded-lg border border-border bg-background p-3 flex items-center gap-2">
              <StatusBadge
                ok={automation.automationEnabled}
                labelTrue="Enabled"
                labelFalse="Disabled"
              />
              <span className="text-sm text-muted-foreground">Automation</span>
            </div>
            <div className="rounded-lg border border-border bg-background p-3 flex items-center gap-2">
              <StatusBadge
                ok={automation.isLeader}
                labelTrue="Leader"
                labelFalse="Follower"
              />
              <span className="text-sm text-muted-foreground">Leader election</span>
            </div>
          </div>
        </section>
      )}

      {/* Rate limit */}
      {rateLimit && (
        <section aria-label="Rate limit">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Rate Limit
          </h2>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 max-w-2xl">
            <div className="rounded-lg border border-border bg-background p-3">
              <p className="text-xs text-muted-foreground">Remaining</p>
              <p className="text-xl font-bold tabular-nums">{rateLimit.remaining}</p>
              <p className="text-xs text-muted-foreground">of {rateLimit.limit}</p>
            </div>
            <div className="rounded-lg border border-border bg-background p-3">
              <p className="text-xs text-muted-foreground">Burst remaining</p>
              <p className="text-xl font-bold tabular-nums">{rateLimit.burstRemaining}</p>
              <p className="text-xs text-muted-foreground">of {rateLimit.burstLimit}</p>
            </div>
            <div className="rounded-lg border border-border bg-background p-3">
              <p className="text-xs text-muted-foreground">Total requests</p>
              <p className="text-xl font-bold tabular-nums">{rateLimit.totalRequests}</p>
            </div>
            <div className="rounded-lg border border-border bg-background p-3">
              <p className="text-xs text-muted-foreground">Throttled</p>
              <p className={cn(
                'text-xl font-bold tabular-nums',
                rateLimit.throttledCount > 0 ? 'text-destructive' : '',
              )}>
                {rateLimit.throttledCount}
              </p>
            </div>
          </div>
          <div className="mt-3 max-w-2xl">
            <div className="flex flex-col gap-1">
              <div className="flex justify-between text-xs text-muted-foreground">
                <span>Rate limit headroom</span>
                <span className="tabular-nums">
                  {rateLimit.remaining}/{rateLimit.limit} (
                  {Math.round((rateLimit.remaining / rateLimit.limit) * 100)}%)
                </span>
              </div>
              <div className="h-2 rounded-full bg-muted overflow-hidden">
                <div
                  className={cn(
                    'h-full rounded-full transition-all',
                    rateLimit.remaining / rateLimit.limit < 0.25
                      ? 'bg-destructive'
                      : rateLimit.remaining / rateLimit.limit < 0.5
                        ? 'bg-status-yellow'
                        : 'bg-status-green',
                  )}
                  style={{
                    width: `${Math.round((rateLimit.remaining / rateLimit.limit) * 100)}%`,
                  }}
                />
              </div>
            </div>
          </div>
          <p className="text-xs text-muted-foreground mt-1">
            Resets {formatTs(rateLimit.resetAt)}
            {rateLimit.limitType ? ` · Type: ${rateLimit.limitType}` : ''}
          </p>
        </section>
      )}

      {/* API endpoint usage */}
      <section aria-label="API endpoint usage">
        <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
          API Endpoint Usage
        </h2>
        {usageQ.isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}
        {usage.length === 0 && !usageQ.isLoading && (
          <p className="text-muted-foreground text-sm">No usage data recorded yet.</p>
        )}
        {usage.length > 0 && (
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-4 py-2">Method</th>
                  <th className="px-4 py-2">Endpoint</th>
                  <th className="px-4 py-2 text-right">Calls</th>
                  <th className="px-4 py-2">Last called</th>
                </tr>
              </thead>
              <tbody>
                {usage.map((u, i) => (
                  <tr
                    key={i}
                    className="border-b border-border last:border-0 hover:bg-accent/30 transition-colors"
                  >
                    <td className="px-4 py-2">
                      <span
                        className={cn(
                          'rounded px-1.5 py-0.5 text-xs font-mono font-medium',
                          u.httpMethod === 'GET'
                            ? 'bg-status-green/15 text-status-green'
                            : u.httpMethod === 'POST'
                              ? 'bg-primary/15 text-primary'
                              : 'bg-muted text-muted-foreground',
                        )}
                      >
                        {u.httpMethod}
                      </span>
                    </td>
                    <td className="px-4 py-2 font-mono text-xs">{u.endpoint}</td>
                    <td className="px-4 py-2 text-right tabular-nums">{u.calls}</td>
                    <td className="px-4 py-2 text-muted-foreground text-xs">
                      {formatRelative(u.lastCalledAt)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
