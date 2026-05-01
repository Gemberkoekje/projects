import { Route, Routes } from 'react-router'
import { TopNav } from './TopNav'
import { Sidebar } from './Sidebar'
import { LiveUpdatesBanner } from '@/components/ui/LiveUpdatesBanner'
import OverviewPage from '@/pages/OverviewPage'
import FleetPage from '@/pages/FleetPage'
import ShipDetailPage from '@/pages/ShipDetailPage'
import FinancePage from '@/pages/FinancePage'
import MarketsPage from '@/pages/MarketsPage'
import RunsPage from '@/pages/RunsPage'
import UniversePage from '@/pages/UniversePage'
import ContractsPage from '@/pages/ContractsPage'
import ActivityPage from '@/pages/ActivityPage'
import HealthPage from '@/pages/HealthPage'
import SettingsPage from '@/pages/SettingsPage'

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
            <Route path="/fleet" element={<FleetPage />} />
            <Route path="/fleet/:symbol" element={<ShipDetailPage />} />
            <Route path="/finance" element={<FinancePage />} />
            <Route path="/markets/*" element={<MarketsPage />} />
            <Route path="/runs/*" element={<RunsPage />} />
            <Route path="/universe" element={<UniversePage />} />
            <Route path="/contracts" element={<ContractsPage />} />
            <Route path="/activity" element={<ActivityPage />} />
            <Route path="/health" element={<HealthPage />} />
            <Route path="/settings" element={<SettingsPage />} />
          </Routes>
        </main>
      </div>
    </div>
  )
}
