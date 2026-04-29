# Phase 8 Reset Recovery Runbook

This runbook defines operator actions for reset-related incidents and reliability alerts.

## Alert meanings

- **API unavailable** (`Runtime.Alert.ApiUnavailable=true`)
  - SpaceTraders API cannot be reached by monitor/probe flow.
- **Token reset mismatch** (`Runtime.Alert.TokenResetMismatch=true`)
  - Stored/configured token reset date no longer matches server reset date.
- **Cache divergence** (`Runtime.Alert.CacheDivergence=true`)
  - Local cache ship state diverges from sampled API ship state and/or stale-sync thresholds.
- **Automation disabled** (`Runtime.Alert.AutomationDisabled=true`)
  - Automation is disabled manually or auto-paused for reset window.
- **Contract deadlines approaching** (`Runtime.Alert.ContractDeadlinesApproaching=true`)
  - At least one accepted contract deadline is within 6 hours.
- **Reset upcoming** (`Runtime.Alert.ResetUpcoming=true`)
  - `serverResets.next` is inside the monitor warning threshold.

## Normal reset window behavior

When `Reliability.PauseAutomationBeforeReset=true`:

1. Monitor warns when reset is near (`ServerResetWarningEvent`).
2. Monitor auto-pauses automation in the final pre-reset window.
3. After reset boundary passes, monitor re-enables automation and clears reset warning flags.

## Recovery actions

### 1) API unavailable

1. Verify upstream API status.
2. Keep automation disabled if upstream instability is prolonged.
3. When API recovers, confirm `Runtime.Alert.ApiUnavailable=false` and monitor activity log for resume events.

### 2) Token reset mismatch

1. Check bootstrap logs for token source (`active`, `configured`, or `stored`).
2. Confirm new registration/token activation succeeded.
3. Verify `Runtime.TokenResetMismatchDetected=false` and `Runtime.Alert.TokenResetMismatch=false` after recovery.

### 3) Cache divergence

1. Trigger manual sync via control endpoint (`POST /control/sync`) or operational tooling.
2. Re-check `/status/system-alerts` and dashboard alerts.
3. If divergence persists, inspect stale ships and recent API failures before re-enabling full automation loops.

### 4) Contract deadline pressure

1. Prioritize assignment and delivery routes for critical contracts.
2. Temporarily disable non-essential trade/exploration loops if needed.
3. Confirm alert clears after deadlines move outside the 6-hour window.

## Post-incident validation checklist

- `/status/system-alerts` shows expected cleared flags.
- Dashboard alert banner no longer displays resolved incidents.
- Activity log contains expected reset/recovery events.
- Automation state (`Automation.Enabled`) matches intended operator state.
