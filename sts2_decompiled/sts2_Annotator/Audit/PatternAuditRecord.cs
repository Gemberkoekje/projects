using System.Collections.Generic;

namespace Sts2Extractor.Audit;

internal sealed class PatternAuditRecord
{
    public string FilePath { get; set; }

    public EntityKind EntityType { get; set; }

    public IReadOnlyList<string> FieldsFound { get; set; }

    public IReadOnlyList<string> FieldsMissing { get; set; }

    public decimal ConfidenceScore { get; set; }

    public bool FallbackRequired { get; set; }

    public PatternAuditRecord()
    {
        FilePath = string.Empty;
        EntityType = EntityKind.None;
        FieldsFound = new List<string>();
        FieldsMissing = new List<string>();
    }
}
