namespace TheCuratool.Domain;

/// <summary>
/// An availability constraint that restricts the Atheist to the very first pick of the draft.
/// If the Drunk flag is applied to the Atheist slot, this restriction is lifted.
/// </summary>
public sealed record AtheistFirstPickConstraint : IAvailabilityConstraint
{
    public bool IsAvailable(AvailabilityContext context)
    {
        if (context.IsDrunkFlagApplied)
        {
            return true;
        }

        return context.PicksMade == 0 && !context.HasAnyMinion && !context.HasAnyDemon;
    }
}
