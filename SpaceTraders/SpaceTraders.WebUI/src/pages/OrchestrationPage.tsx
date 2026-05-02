/**
 * OrchestrationPage – Fleet orchestration dashboard.
 * Phase 17a: placeholder page with three vertically-stacked section stubs.
 * Phase 17b: Goal Chains panel implemented via GoalChainPanel.
 * Phase 17c: Assignments panel implemented via AssignmentsPanel.
 * Phase 17d: Activity panel implemented via ActivityPanel.
 */
import GoalChainPanel from '@/components/GoalChainPanel'
import AssignmentsPanel from '@/components/AssignmentsPanel'
import ActivityPanel from '@/components/ActivityPanel'

export default function OrchestrationPage() {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Orchestration</h1>

      {/* Phase 17b: Goal Chains panel */}
      <section aria-labelledby="goal-chains-heading">
        <h2 id="goal-chains-heading" className="mb-3 text-lg font-medium">
          Goal Chains
        </h2>
        <GoalChainPanel />
      </section>

      {/* Phase 17c: Assignments panel */}
      <section aria-labelledby="assignments-heading">
        <h2 id="assignments-heading" className="mb-3 text-lg font-medium">
          Assignments
        </h2>
        <AssignmentsPanel />
      </section>

      {/* Phase 17d: Activity panel */}
      <section aria-labelledby="activity-heading">
        <h2 id="activity-heading" className="mb-3 text-lg font-medium">
          Activity
        </h2>
        <ActivityPanel />
      </section>
    </div>
  )
}
