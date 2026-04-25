using System;

namespace DatumPrikker.ApiService.Domain;

public sealed class PollResponse
{
    public Guid Id { get; set; }

    public Guid PollOptionId { get; set; }

    public required string RespondentKey { get; set; }

    public required string RespondentName { get; set; }

    public Availability Availability { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public PollOption PollOption { get; set; } = default!;
}
