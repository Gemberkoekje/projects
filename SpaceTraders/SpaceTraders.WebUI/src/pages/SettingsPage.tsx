import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import type { SettingDto, ScheduledRunDto } from '@/types'

function formatTs(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatRelative(iso: string | null) {
  if (!iso) return null
  const ms = new Date(iso).getTime() - Date.now()
  const abs = Math.abs(ms)
  const h = Math.floor(abs / 3_600_000)
  const m = Math.floor((abs % 3_600_000) / 60_000)
  const label = ms < 0 ? ' ago' : ''
  if (h > 0) return `${h}h ${m}m${label}`
  return `${m}m${label}`
}

export default function SettingsPage() {
  const settingsQ = useQuery<SettingDto[]>({
    queryKey: ['settings'],
    queryFn: () => apiFetch('/settings/'),
    refetchInterval: 60_000,
  })

  const scheduledRunsQ = useQuery<ScheduledRunDto[]>({
    queryKey: ['scheduled-runs'],
    queryFn: () => apiFetch('/runs/scheduled'),
    refetchInterval: 30_000,
  })

  const settings = settingsQ.data ?? []
  const scheduledRuns = scheduledRunsQ.data ?? []

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Settings</h1>
        <span className="rounded-full bg-muted text-muted-foreground px-3 py-1 text-xs font-medium">
          Read-only mirror — values are set via the operator API
        </span>
      </div>

      {/* Scheduled runs */}
      <section aria-label="Scheduled runs">
        <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
          Scheduled Runs
        </h2>
        {scheduledRunsQ.isLoading && (
          <p className="text-muted-foreground text-sm">Loading…</p>
        )}
        {scheduledRuns.length === 0 && !scheduledRunsQ.isLoading && (
          <p className="text-muted-foreground text-sm">No scheduled runs pending.</p>
        )}
        {scheduledRuns.length > 0 && (
          <div className="flex flex-col gap-2">
            {scheduledRuns.map(run => (
              <div
                key={run.id}
                className="rounded-lg border border-border bg-background p-3 flex flex-col gap-1"
              >
                <div className="flex items-center gap-2">
                  <span className="font-medium text-sm">{run.name}</span>
                  <span className="rounded-full bg-status-yellow/15 text-status-yellow px-2 py-0.5 text-xs font-medium">
                    Scheduled
                  </span>
                </div>
                <p className="text-xs text-muted-foreground">{run.strategyLabel}</p>
                <p className="text-xs text-muted-foreground">
                  {run.activatesOnNextRestart
                    ? 'Activates on next restart'
                    : run.activatesAt
                      ? `Activates ${formatRelative(run.activatesAt)} (${formatTs(run.activatesAt)})`
                      : 'No activation time set'}
                </p>
                <p className="text-xs text-muted-foreground">
                  Created {formatTs(run.createdAt)}
                </p>
              </div>
            ))}
          </div>
        )}
      </section>

      {/* Settings table */}
      <section aria-label="Agent settings">
        <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
          Agent Settings
        </h2>
        {settingsQ.isLoading && (
          <p className="text-muted-foreground text-sm">Loading…</p>
        )}
        {settings.length === 0 && !settingsQ.isLoading && (
          <p className="text-muted-foreground text-sm">No settings found.</p>
        )}
        {settings.length > 0 && (
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-4 py-2">Key</th>
                  <th className="px-4 py-2">Value</th>
                  <th className="px-4 py-2">Type</th>
                  <th className="px-4 py-2">Description</th>
                </tr>
              </thead>
              <tbody>
                {settings.map(setting => (
                  <tr
                    key={setting.key}
                    className="border-b border-border last:border-0 hover:bg-accent/30 transition-colors"
                  >
                    <td className="px-4 py-2 font-mono text-xs">{setting.key}</td>
                    <td className="px-4 py-2 font-mono text-xs font-medium">{setting.value}</td>
                    <td className="px-4 py-2 text-muted-foreground text-xs">{setting.type}</td>
                    <td className="px-4 py-2 text-muted-foreground text-xs max-w-xs">
                      {setting.description}
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
