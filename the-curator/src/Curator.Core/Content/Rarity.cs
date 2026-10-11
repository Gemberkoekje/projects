namespace Curator.Core.Content;

/// <summary>How rare a book is; sets its fee unless the book names one.</summary>
public enum Rarity
{
    None = 0,
    Common,
    Uncommon,
    Rare,
    Singular,
}
