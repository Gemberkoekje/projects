import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import type { SettingDto } from '@/types'

export default function SettingsPage() {
  const settingsQ = useQuery<SettingDto[]>({
    queryKey: ['settings'],
    queryFn: () => apiFetch('/settings/'),
    refetchInterval: 60_000,
  })

  const settings = settingsQ.data ?? []

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Settings</h1>
        <span className="rounded-full bg-muted text-muted-foreground px-3 py-1 text-xs font-medium">
          Read-only mirror — values are set via the operator API
        </span>
      </div>

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
