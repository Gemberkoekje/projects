using System;
using System.Collections.Generic;

namespace DatumPrikker.ApiService.Domain;

public sealed class Poll
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public required string Description { get; set; }

    public required string OwnerIdentityId { get; set; }

    public required string ShareToken { get; set; }

    public DateTimeOffset? ClosesAtUtc { get; set; }

    public DateTimeOffset? ClosedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public List<PollOption> Options { get; set; } = [];
}
