using FluentValidation;
using Qowaiv.Validation.Fluent;

namespace Kluskat.core.Validators;

internal sealed class KlusValidator : ModelValidator<Klus>
{
    /// <summary>A decade: anything longer is a typo, not a chore.</summary>
    internal const int MaxIntervalDagen = 3650;

    public KlusValidator()
    {
        RuleFor(k => k.Ruimte).NotEmpty().WithMessage("Een klus heeft een ruimte nodig, bijvoorbeeld 'Keuken'.");
        RuleFor(k => k.Naam).NotEmpty().WithMessage("Een klus heeft een naam nodig, bijvoorbeeld 'Stofzuigen'.");
        RuleFor(k => k.IntervalDagen)
            .InclusiveBetween(1, MaxIntervalDagen)
            .WithMessage($"Om de hoeveel dagen? Kies tussen 1 en {MaxIntervalDagen}.");
        RuleFor(k => k.Grootte).NotEqual(Grootte.None).WithMessage("Kies een grootte: klein, middel of groot.");
    }
}
