using System;
using System.Collections.Generic;

namespace DatumPrikker.ApiService.Domain;

public sealed class PollOption
{
    public Guid Id { get; set; }

    public Guid PollId { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Poll Poll { get; set; } = default!;

    public List<PollResponse> Responses { get; set; } = [];
}
