import {
  HeartPulse,
  LayoutDashboard,
  Rocket,
  ShoppingCart,
  Settings,
  Workflow,
  Camera,
  type LucideIcon,
} from 'lucide-react'
import { NavLink } from 'react-router'
import { cn } from '@/lib/utils'

interface NavItem {
  path: string
  label: string
  Icon: LucideIcon
}

const NAV_ITEMS: NavItem[] = [
  { path: '/', label: 'Overview', Icon: LayoutDashboard },
  { path: '/plans', label: 'Plans', Icon: Workflow },
  { path: '/fleet', label: 'Fleet', Icon: Rocket },
  { path: '/markets', label: 'Markets', Icon: ShoppingCart },
  { path: '/snapshots', label: 'Snapshots', Icon: Camera },
  { path: '/health', label: 'Health', Icon: HeartPulse },
  { path: '/settings', label: 'Settings', Icon: Settings },
]

export function Sidebar() {
  return (
    <nav
      aria-label="Main navigation"
      className="flex w-56 shrink-0 flex-col gap-0.5 border-r border-sidebar-border bg-sidebar p-2"
    >
      {NAV_ITEMS.map(({ path, label, Icon }) => (
        <NavLink
          key={path}
          to={path}
          end={path === '/'}
          className={({ isActive }) =>
            cn(
              'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
              isActive
                ? 'bg-accent text-accent-foreground'
                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground',
            )
          }
        >
          <Icon size={16} aria-hidden />
          {label}
        </NavLink>
      ))}
    </nav>
  )
}
