namespace Sts2Viewer.Data;

public sealed class CharacterRow
{
    public string Id { get; set; }

    public string Title { get; set; }

    public CharacterRow()
    {
        Id = string.Empty;
        Title = string.Empty;
    }
}
