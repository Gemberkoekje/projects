namespace ByGalacticAccord.Engine.Actors;

/// <summary>
/// Actor archetypes (§4.5.1). All actors share one base model; the role only changes which
/// contracts they post and fulfil. Slice 0 ships the first six; the rest are the documented
/// later-slice niches, named here so the taxonomy is complete.
/// </summary>
public enum ActorRole
{
    /// <summary>Producer / "Conjurer": makes goods, sells them as lots and answers wanted Sales (§4.2.3).</summary>
    Producer,

    /// <summary>Consumer / "Eater": posts wanted Sales for what it uses, the credit faucet (§4.2.3).</summary>
    Consumer,

    /// <summary>Owns a fleet: carries hauls and fills Sales by buying and carrying.</summary>
    ShippingCompany,

    /// <summary>One ship, scrappier than a company. The player archetype.</summary>
    IndependentPilot,

    /// <summary>Tax sink and (later) law enforcement.</summary>
    Government,

    /// <summary>Runs a station: docking, and storage rented to anyone (design §8.1).</summary>
    StationOperator,

    // ---- documented later-slice roles (not instantiated in slice 0) ----
    Factory,
    Pirate,
    BondingAgent,
    DataBroker,
    Multinational,
}
