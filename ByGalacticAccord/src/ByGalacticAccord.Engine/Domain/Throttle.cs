namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// How hard a ship is driven on its laden leg, chosen at departure (design §8.4, §16.7): the choice between beating the late fee
/// and risking the cargo. The empty leg to a pickup is always flown at cruise: nothing aboard is worth the risk.
/// </summary>
public enum Throttle
{
    /// <summary>No laden leg to choose for: a collection, or a ship flying home empty. Flown as cruise.</summary>
    None,

    /// <summary>Standard speed.</summary>
    Cruise,

    /// <summary>Faster (speed ×1.25), and an incident likelier (×1.5, or ×2 for a drive that runs hot).</summary>
    Push,
}
