using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Sts2Extractor.Infrastructure;

internal sealed class CheckpointRecord
{
    public string ProcessId { get; set; }

    public string CharacterId { get; set; }

    public int BatchNumber { get; set; }

    public int TotalBatches { get; set; }

    public DateTime CompletedAt { get; set; }
}

internal sealed class CheckpointManager
{
    private readonly string _checkpointDirectory;
    private readonly string _checkpointFile;

    public CheckpointManager(string stagingDirectory, string processId)
    {
        _checkpointDirectory = stagingDirectory;
        Directory.CreateDirectory(_checkpointDirectory);
        _checkpointFile = Path.Combine(_checkpointDirectory, $".checkpoint_{processId}.json");
    }

    public CheckpointRecord LoadCheckpoint()
    {
        if (!File.Exists(_checkpointFile))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(_checkpointFile);
            return JsonSerializer.Deserialize<CheckpointRecord>(json);
        }
        catch
        {
            return null;
        }
    }

    public void SaveCheckpoint(string characterId, int batchNumber, int totalBatches)
    {
        CheckpointRecord record = new CheckpointRecord
        {
            ProcessId = Path.GetFileNameWithoutExtension(_checkpointFile),
            CharacterId = characterId,
            BatchNumber = batchNumber,
            TotalBatches = totalBatches,
            CompletedAt = DateTime.UtcNow
        };

        string json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_checkpointFile, json);
    }

    public void ClearCheckpoint()
    {
        if (File.Exists(_checkpointFile))
        {
            File.Delete(_checkpointFile);
        }
    }
}
