# Resume and Resilience Improvements for Annotator

## Overview

This document describes the improvements made to the STS2 Annotator to handle and recover from LLM API failures, particularly during the `sync-postgres` command when running affinity scoring.

## Problem

Previously, when the `sync-postgres --all-characters --annotate` command ran long-duration annotation tasks (like `score-affinities`), failures during API rate limiting or provider overload (HTTP 529) would force you to restart the entire process, losing all progress.

In your case:
- The process completed 54/60 batches for the defect character
- Hit Anthropic API overload (529 error) after 8 retry attempts
- No way to resume from batch 54 - had to restart from batch 1

## Solutions Implemented

### 1. **Checkpoint-Based Progress Tracking**

A new `CheckpointManager` class (`Infrastructure/CheckpointManager.cs`) automatically saves progress after each batch completes:

```
.checkpoint_score-affinities.json
{
  "processId": "score-affinities",
  "characterId": "defect",
  "batchNumber": 54,
  "totalBatches": 60,
  "completedAt": "2024-01-15T10:30:45Z"
}
```

**Features:**
- Automatically created in the staging directory after each batch
- JSON-based format for easy debugging
- Automatically cleared on successful completion
- Only created when `--resume` flag is used

### 2. **Improved Exponential Backoff**

The `AnthropicProvider` now uses more aggressive exponential backoff for rate limiting:

**Old behavior:**
- Max backoff: 60 seconds
- Sequence: 2s → 4s → 8s → 16s → 32s → 60s → 60s → 60s

**New behavior:**
- Max backoff: 120 seconds
- Sequence: 2s → 4s → 8s → 16s → 32s → 60s → 120s → 120s

This gives Anthropic's infrastructure more time to recover from overload without exhausting all retries.

### 3. **Two-Mode Resume Support**

You now have two ways to resume:

#### Option A: Automatic Resume from Checkpoint

```bash
# Automatically resumes from the last checkpoint
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters --annotate --resume
```

This will:
1. Check for a `.checkpoint_score-affinities.json` file
2. If found, skip all completed batches and resume from the next one
3. Clear the checkpoint on successful completion

#### Option B: Explicit Resume with Batch Number

```bash
# Resume from specific batch in specific character
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters --annotate --resume-character defect --resume-batch 54
```

This is useful when you want to:
- Skip to a specific batch without using checkpoint files
- Resume from a batch manually identified from logs
- Use explicit batch ranges (e.g., re-run just batches 54-60)

**Command-line reference:**
- `--resume`: Enable checkpoint-based resumption
- `--resume-character <id>`: Specify character to resume for (e.g., `defect`)
- `--resume-batch <n>`: Specify starting batch number (1-indexed)

**Note:** CLI flags (`--resume-character` and `--resume-batch`) take precedence over checkpoint files.

### 4. **Only-Missing Affinities on Resume**

The `--only-missing` flag is automatically enabled when you use `--resume`, ensuring that:
- Previously completed affinities are not re-scored
- Only missing relationships are processed
- Resume is much faster than restarting

## Usage Examples

### Scenario 1: First Run (Fail at batch 54)

```bash
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters --annotate --resume
```

Process fails at batch 54, checkpoint is saved.

### Scenario 2: Resume After Failure

```bash
# Just run the same command again
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters --annotate --resume
```

Process automatically resumes from batch 54 (defect character).

### Scenario 3: Manual Resume from Specific Batch

```bash
# If you want to start from batch 54 instead of relying on checkpoint
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters --annotate --resume-character defect --resume-batch 54
```

### Scenario 4: Skip Checkpoint and Force Restart

```bash
# Don't use --resume; checkpoint will be ignored and fresh restart begins
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- sync-postgres --root .\sts2 --all-characters --annotate
```

## Checkpoint Files

Checkpoint files are stored as:
```
<staging-directory>/.checkpoint_score-affinities.json
```

Where `<staging-directory>` is:
- `--output` if specified
- `output/postgres_sync` relative to root if not specified
- Or explicitly: `.\sts2\output\postgres_sync\.checkpoint_score-affinities.json`

**Manual checkpoint clearing:**
```bash
# If you want to force a clean restart
Remove-Item ".\sts2\output\postgres_sync\.checkpoint_score-affinities.json" -Force
```

## Implementation Details

### Files Modified

1. **`Infrastructure/CheckpointManager.cs`** (NEW)
   - `CheckpointRecord`: Data class for serializing progress
   - `CheckpointManager`: Loads/saves/clears checkpoint JSON

2. **`Annotation/Providers/AnthropicProvider.cs`**
   - Increased exponential backoff cap from 60s to 120s
   - Better handling of provider overload (529 errors)

3. **`Extractors/ScoreAffinitiesRunner.cs`**
   - Added checkpoint loading at start
   - Added checkpoint saving after each batch
   - Skip already-completed batches on resume
   - Clear checkpoint on completion

4. **`Cli/CliOptions.cs`**
   - Added `ResumeCharacterId` property
   - Added `ResumeBatchNumber` property
   - Added parsing for `--resume-character` flag
   - Added parsing for `--resume-batch` flag
   - Updated usage string

## Batch Numbering

Batches are 1-indexed in logs and CLI arguments:

```
[score-affinities] (1/5) defect batch 1/60: ...
[score-affinities] (1/5) defect batch 54/60: ...
```

When resuming, use `--resume-batch 54` to start from batch 54 (not 55).

## Rate Limiting Context

Per the copilot-instructions.md:
- The system uses Haiku-class models for large mechanical annotation batches
- Sonnet-class for archetype discovery/synergy reasoning
- Current setup routes affinity scoring through Anthropic

The improved backoff helps avoid exhausting retries when Anthropic experiences temporary overload.

## Testing the Resume Logic

To verify the resume works:

1. Run the sync-postgres command and let it fail at some batch
2. Verify the checkpoint file exists: `ls -la .\sts2\output\postgres_sync\.checkpoint_*`
3. Run the command again with `--resume` flag
4. Watch the logs show "Resuming from checkpoint" and skip completed batches

## Future Improvements

Possible enhancements:
- Checkpoint support for other runners (MechanicalAnnotationRunner, etc.)
- Distributed checkpointing for multi-machine setups
- Automatic cleanup of old checkpoints
- Prometheus metrics for batch timing and retry counts
