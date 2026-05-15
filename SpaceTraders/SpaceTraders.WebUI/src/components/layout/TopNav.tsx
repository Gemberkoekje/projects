import { Rocket } from 'lucide-react'
import { ThemeToggle } from '@/components/ui/ThemeToggle'
import { useSignalR } from '@/lib/signalr-context'
import { cn } from '@/lib/utils'

/** Coloured dot that represents the SignalR connection state. */
function ConnectionDot() {
  const { state } = useSignalR()

  const label =
    state === 'connected'
      ? 'Live updates connected'
      : state === 'reconnecting'
        ? 'Reconnecting…'
        : 'Disconnected'

  const colorClass =
    state === 'connected'
      ? 'bg-status-green'
      : state === 'reconnecting'
        ? 'bg-status-yellow animate-pulse'
        : 'bg-status-red'

  return (
    <span className="flex items-center gap-1.5" title={label} aria-label={label}>
      <span className={cn('h-2 w-2 rounded-full', colorClass)} />
      <span className="hidden text-xs text-muted-foreground sm:inline">{label}</span>
    </span>
  )
}

export function TopNav() {
  return (
    <header className="flex h-14 shrink-0 items-center gap-4 border-b border-border bg-sidebar px-4">
      {/* Logo */}
      <div className="flex items-center gap-2 font-semibold">
        <Rocket size={20} className="text-primary" aria-hidden />
        <span>SpaceTraders</span>
      </div>

      {/* Spacer */}
      <div className="flex-1" />

      {/* Health strip */}
      <ConnectionDot />

      {/* Theme toggle */}
      <ThemeToggle />
    </header>
  )
}
