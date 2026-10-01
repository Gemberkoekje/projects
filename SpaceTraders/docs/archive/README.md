# Archived documents

Archived on 2026-10-01 (slice 0.2 of `../../PLAN.md`).

These documents record plans, designs and progress logs from earlier rounds of work. Many of them
mark phases as complete that were later undone or replaced, so **none of them describes the
current code**. For what the code does today, read `../HOW_IT_WORKS.md`. For what happens next,
read `../../PLAN.md`.

They are kept for their reasoning and for the game knowledge in them.

## Game strategy notes

Strategy is yours to decide. These are worth reading when you do:

| File | Contents |
|---|---|
| `STARTER_SYSTEM_STRATEGY.md` | Priorities in the starting system on the way to the jump gate (fab mats, advanced circuits, mining as a supplement). |
| `GAME_STRATEGY_PLAN.md` | Phase-by-phase strategy from market scouting to jump gate construction, with the priorities and budgets the old orchestrator used. |
| `EXPANSION_PLAN.md` | A generic expansion order for a new reset, with a credits-planning template. |

## Architecture rounds, in order

| File | Contents |
|---|---|
| `SPACE_TRADERS_IMPLEMENTATION_PLAN.md`, `SPACE_TRADERS_IMPLEMENTATION_PLAN_PROGRESS.md` | The phase 0–10 feature plan and its progress log. |
| `ship-automation-architecture-plan.md` | Replacing chain-of-command ship handlers with per-ship planners and automation events. |
| `SHIP_GOAL_DRIVEN_ARCHITECTURE_PLAN.md` | The goal-driven model: fleet goals, ship goals, an assignment resolver and goal executors. |
| `REFACTOR_PLAN_ShipAutomationTickEvent_Removal.md` | Removing the synthetic ship "tick" event in favour of direct executor calls on arrival and cooldown. |
| `basics-reset-plan.md` | Cutting everything back to a single "scout all marketplaces" plan. Contract, probe, mining and trading plans were added back afterwards. |

## Feature designs

| File | Contents |
|---|---|
| `contract-plan-implementation.md` | The mineral contract plan. |
| `MINING_AUTOMATION_PLAN.md`, `MINING_ASTEROID_SURVEY_WAYPOINT_DETAILS_PLAN.md`, `SURVEY_AUTOMATION_PLAN.md` | Mining, surveys and waypoint details. |
| `TRADING_AUTOMATION_PLAN.md` | The trading loop. |
| `PROBE_DEPLOYMENT_PLAN.md` | Deploying probes to market and shipyard waypoints. |
| `MODERN_FRONTEND_PLAN.md` | The React WebUI. |

## Superseded operational documents

| File | Replaced by |
|---|---|
| `CURRENT_IMPLEMENTATION_OVERVIEW.md` | `../HOW_IT_WORKS.md` |
| `RACE_CONDITION_PREVENTION_IMPLEMENTATION.md` | `../HOW_IT_WORKS.md`, which covers the safeguards that still exist. |
| `PHASE8_RESET_RECOVERY_RUNBOOK.md` | Nothing yet. The `ResetAndReliabilityMonitorService` it describes no longer exists; reset handling is slice 1.8 of the plan. |
| `LOCAL_DEVELOPMENT.md` | `../../README.md`. The Razor Pages app and the `k8s/` folder it describes no longer exist. |
