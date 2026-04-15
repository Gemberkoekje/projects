# Slay the Spire 2 — Remaining Work

## Remaining Operations

1. **Full annotation refresh**
   - Re-run `sync-postgres --annotate --all-characters` to refresh all annotation fields with keyword-aware context.

2. **Full affinity verification pass**
   - Execute one full verification pass of:
     - `discover-archetypes --all-characters`
     - `score-affinities --all-characters`
   - Validate outputs against production DB snapshots.

---

## Priority Order

| Priority | Item | Effort | Impact |
|---|---|---|---|
| **P0** | Re-run full annotation + affinity pipeline | Medium | Refreshes and validates all derived data |
