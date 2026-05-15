import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import type { MiningAutomationPlanStateDto } from '@/types'

function formatObservedAt(value: string) {
  const time = new Date(value).getTime()
  if (!Number.isFinite(time)) return '—'

  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(time)
}

export default function MiningOpportunitiesPanel() {
  const { data, isLoading } = useQuery<MiningAutomationPlanStateDto>({
    queryKey: ['mining-opportunities'],
    queryFn: () => apiFetch('/status/mining-opportunities'),
    refetchInterval: 10_000,
  })

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  const opportunities = data?.opportunities ?? []

  if (opportunities.length === 0) {
    return <p className="text-sm text-muted-foreground">No queued mining opportunities.</p>
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-border">
      <table className="min-w-full text-sm">
        <thead>
          <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
            <th className="px-4 py-2">Resource</th>
            <th className="px-4 py-2">Sell waypoint</th>
            <th className="px-4 py-2">Status</th>
            <th className="px-4 py-2">Ship</th>
            <th className="px-4 py-2">Last observed</th>
            <th className="px-4 py-2">Reason</th>
          </tr>
        </thead>
        <tbody>
          {opportunities.map(opportunity => (
            <tr key={opportunity.opportunityKey} className="border-b border-border last:border-0">
              <td className="px-4 py-3 font-mono font-medium">{opportunity.tradeSymbol}</td>
              <td className="px-4 py-3 font-mono text-muted-foreground">
                {opportunity.sellWaypointSymbol}
              </td>
              <td className="px-4 py-3">{opportunity.status}</td>
              <td className="px-4 py-3 font-mono text-muted-foreground">
                {opportunity.assignedShipSymbol ?? '—'}
              </td>
              <td className="px-4 py-3 text-muted-foreground">
                {formatObservedAt(opportunity.lastObservedAt)}
              </td>
              <td className="px-4 py-3 text-muted-foreground">
                {opportunity.stopReason ?? '—'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
