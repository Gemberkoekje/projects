import { Route, Routes } from 'react-router'
import { TopNav } from './TopNav'
import { Sidebar } from './Sidebar'
import { LiveUpdatesBanner } from '@/components/ui/LiveUpdatesBanner'
import OverviewPage from '@/pages/OverviewPage'
import FleetPage from '@/pages/FleetPage'
import MarketsPage from '@/pages/MarketsPage'
import HealthPage from '@/pages/HealthPage'
import SettingsPage from '@/pages/SettingsPage'
import SnapshotsPage from '@/pages/SnapshotsPage'
import OrchestrationPage from '@/Future/pages/OrchestrationPage'
import ShipDetailPage from '@/Future/pages/ShipDetailPage'

export function AppShell() {
  return (
    <div className="flex h-screen flex-col bg-background text-foreground">
      <TopNav />
      <LiveUpdatesBanner />

      <div className="flex flex-1 overflow-hidden">
        <Sidebar />

        <main className="flex-1 overflow-auto p-6">
          <Routes>
            <Route path="/" element={<OverviewPage />} />
            <Route path="/plans" element={<OrchestrationPage />} />
            <Route path="/fleet" element={<FleetPage />} />
            <Route path="/fleet/:symbol" element={<ShipDetailPage />} />
            <Route path="/markets" element={<MarketsPage />} />
            <Route path="/snapshots" element={<SnapshotsPage />} />
            <Route path="/health" element={<HealthPage />} />
            <Route path="/settings" element={<SettingsPage />} />
          </Routes>
        </main>
      </div>
    </div>
  )
}
