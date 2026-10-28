using Qowaiv.Customization;

namespace Kluskat.core.Models;

/// <summary>Identity of a <see cref="Klus"/>. Random, not derived from anything.</summary>
[Id<UuidBehavior, Uuid>]
public readonly partial struct KlusId
{
}
