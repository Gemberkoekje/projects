# Step 5: Burndown Calculation - Implementation Summary

## Overview
Implemented the burndown calculation logic as specified in Step 5 of the plan. The system now calculates detailed burndown metrics for both CoPilot and Claude usage.

## Components Created

### 1. `BurndownReport.cs` (Model)
A comprehensive record that captures all burndown metrics:
- **Budget & Period Info**: Budget, period start/end, days elapsed/remaining
- **Consumption Metrics**: Actual usage, ideal usage (linear burndown), projected end-of-period usage
- **Delta Metrics**: Current delta (change since last snapshot), rolling average delta
- **Status Indicators**: Computed properties for over-budget detection and percentage calculations

### 2. `IBurndownCalculator.cs` (Interface)
Service interface defining the burndown calculation contract:
```csharp
Task<BurndownReport> CalculateBurndownAsync(string provider, long budget, CancellationToken cancellationToken)
```

### 3. `BurndownCalculator.cs` (Service)
The core implementation that:
- Queries Marten for all `UsageSnapshotRecorded` events for a provider
- Calculates period metrics (days elapsed, remaining, total)
- Computes ideal usage: `budget × (days_elapsed / total_days)` — the perfect linear pace
- Computes projected usage: `actual × (total_days / days_elapsed)` — where you'll end if pace holds
- Calculates current delta: difference between last two snapshots
- Calculates rolling average delta: mean of last N deltas (configurable via `Burndown:RollingWindowSize`)

### 4. Configuration Updates
- Added `Burndown:RollingWindowSize` to `appsettings.json` (default: 7)
- This controls how many historical deltas are used for the rolling average

### 5. Worker Integration
Updated `Worker.cs` to:
- Inject `IBurndownCalculator`
- Calculate burndown reports for both providers after persisting snapshots
- Read budgets from configuration (`CoPilot:Budget` and `Claude:Budget`)
- Log summary information (percent used, days remaining)

## Key Calculations

### Ideal Usage (Linear Burndown)
```
ideal = budget × (days_elapsed / total_days_in_period)
```
This represents what should have been consumed by now if usage was perfectly steady.

### Projected Usage
```
projected = actual_usage × (total_days_in_period / days_elapsed)
```
This extrapolates current usage to predict end-of-period consumption.

### Current Delta
```
delta = latest_snapshot.tokens - previous_snapshot.tokens
```
Shows consumption since the last check.

### Rolling Average Delta
```
average_delta = mean(last_N_deltas)
```
Smooths out variance to establish a baseline for spike detection (Step 6).

## Edge Case Handling
- **No snapshots**: Returns empty report with zero usage
- **Single snapshot**: Delta equals total usage (no previous to compare)
- **Period boundary issues**: Ensures days elapsed never exceeds period length or goes negative
- **Rolling window**: Uses min(snapshot_count, window_size) to handle early periods

## Next Steps
This sets up perfectly for Step 6 (Spike Detection), which will compare `current_delta` to `rolling_average_delta × threshold` to detect unusual consumption spikes.

## Build Status
✅ Build successful - all files compile without errors
