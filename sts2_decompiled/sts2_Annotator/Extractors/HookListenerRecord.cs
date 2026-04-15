namespace Sts2Extractor.Extractors;

internal sealed class HookListenerRecord
{
    public string EntityType { get; set; }

    public string EntityId { get; set; }

    public string HookName { get; set; }

    public HookListenerRecord()
    {
        EntityType = string.Empty;
        EntityId = string.Empty;
        HookName = string.Empty;
    }
}
