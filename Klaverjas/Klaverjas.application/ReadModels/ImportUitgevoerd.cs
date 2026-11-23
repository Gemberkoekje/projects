namespace Klaverjas.application.ReadModels;

/// <summary>What an applied import wrote, for the confirmation.</summary>
public sealed record ImportUitgevoerd
{
    /// <summary>Players added.</summary>
    public int NieuweSpelers { get; init; }

    /// <summary>Evenings written.</summary>
    public int Avonden { get; init; }

    /// <summary>Evenings without play added.</summary>
    public int GeenSpeelavonden { get; init; }
}
