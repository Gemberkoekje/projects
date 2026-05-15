/**
 * OrchestrationPage – Fleet orchestration dashboard.
 * Phase 17a: placeholder page with three vertically-stacked section stubs.
 * Phase 17b: Goal Chains panel implemented via GoalChainPanel.
 * Phase 17c: Assignments panel implemented via AssignmentsPanel.
 * Phase 17d: Activity panel implemented via ActivityPanel.
 */
import GoalChainPanel from '@/Future/components/GoalChainPanel'
import AssignmentsPanel from '@/Future/components/AssignmentsPanel'
import ActivityPanel from '@/Future/components/ActivityPanel'
import MiningOpportunitiesPanel from '@/Future/components/MiningOpportunitiesPanel'

export default function OrchestrationPage() {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Plans</h1>

      <section aria-labelledby="goal-chains-heading">
        <h2 id="goal-chains-heading" className="mb-3 text-lg font-medium">
          Active Plans
        </h2>
        <GoalChainPanel />
      </section>

      <section aria-labelledby="assignments-heading">
        <h2 id="assignments-heading" className="mb-3 text-lg font-medium">
          Ships by Plan
        </h2>
        <AssignmentsPanel />
      </section>

      <section aria-labelledby="mining-opportunities-heading">
        <h2 id="mining-opportunities-heading" className="mb-3 text-lg font-medium">
          Queued Mining Opportunities
        </h2>
        <MiningOpportunitiesPanel />
      </section>

      <section aria-labelledby="activity-heading">
        <h2 id="activity-heading" className="mb-3 text-lg font-medium">
          Current Status
        </h2>
        <ActivityPanel />
      </section>
    </div>
  )
}
