using TheCuratool.Application;
using TheCuratool.Domain;

namespace TheCuratool.Web.Api;

public sealed class ScriptUploadRequest
{
    public string RawJson { get; init; } = string.Empty;
}

public sealed class CreateSessionRequest
{
    public Guid ScriptId { get; init; }

    public int PlayerCount { get; init; }

    public IReadOnlyList<string> ActiveLorics { get; init; } = Array.Empty<string>();

    public bool UseMarionette { get; init; }
}

public sealed class CuratedOfferRequest
{
    public int PlayerSlot { get; init; }

    public IReadOnlyList<string> OfferedIds { get; init; } = Array.Empty<string>();
}

public sealed class AtheistCommitmentRequest
{
    public int PlayerSlot { get; init; }
}

public sealed class RecordChoiceRequest
{
    public int PlayerSlot { get; init; }

    public string ChosenCharacterId { get; init; } = string.Empty;

    public IReadOnlyList<string> OfferedIds { get; init; } = Array.Empty<string>();

    public HiddenFlagsRequest HiddenFlags { get; init; } = HiddenFlagsRequest.Empty;
}

public sealed class HiddenFlagsRequest
{
    public static readonly HiddenFlagsRequest Empty = new();

    public bool IsDrunk { get; init; }

    public bool IsLunatic { get; init; }
}

public sealed record ScriptApiResponse(
    Guid Id,
    string Name,
    string Author,
    int CharacterCount,
    IReadOnlyList<string> Warnings);

public sealed record CharacterApiResponse(
    string Id,
    string DisplayName,
    CharacterType Type,
    bool IsUnknown);

public sealed record ChoiceApiResponse(
    string State,
    string CharacterId,
    IReadOnlyList<string> OfferedIds,
    bool IsAtheistCommitmentConfirmed,
    HiddenFlags HiddenFlags);

public sealed record PlayerSlotApiResponse(
    int DraftOrder,
    Guid PlayerId,
    bool IsChosen,
    ChoiceApiResponse Choice);

public sealed record SetupCountsApiResponse(
    int Townsfolk,
    int Outsiders,
    int Minions,
    int Demons);

public sealed record MakeupSummaryApiResponse(
    SetupCountsApiResponse CurrentCounts,
    IReadOnlyList<SetupCountsApiResponse> TargetCounts,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ChosenCharactersByType,
    int RemainingSeats,
    IReadOnlyList<string> OutOfScriptCharacterIds);

public sealed record SessionApiResponse(
    Guid Id,
    string ScriptName,
    string ScriptAuthor,
    int PlayerCount,
    GameStatus Status,
    IReadOnlyList<string> ActiveLorics,
    bool UseMarionette,
    int CurrentPlayerSlot,
    IReadOnlyList<PlayerSlotApiResponse> Players,
    MakeupSummaryApiResponse MakeupSummary);

internal static class SessionApiMapper
{
    public static SessionApiResponse MapSession(GameSession session, MakeupSummary summary)
    {
        return new SessionApiResponse(
            session.Id,
            session.Script.Name,
            session.Script.Author,
            session.PlayerCount,
            session.Status,
            session.ActiveLoricIds,
            session.UseMarionette,
            session.Players.Where(slot => !slot.IsChosen).OrderBy(slot => slot.DraftOrder).Select(slot => slot.DraftOrder).FirstOrDefault(),
            session.Players.Select(MapPlayerSlot).ToList().AsReadOnly(),
            MapMakeupSummary(summary));
    }

    public static CharacterApiResponse MapCharacter(CharacterDefinition character)
    {
        return new CharacterApiResponse(character.Id, character.DisplayName, character.Type, character.IsUnknown);
    }

    private static PlayerSlotApiResponse MapPlayerSlot(PlayerSlot slot)
    {
        return new PlayerSlotApiResponse(slot.DraftOrder, slot.PlayerId, slot.IsChosen, MapChoice(slot.Choice));
    }

    private static ChoiceApiResponse MapChoice(PlayerChoice choice)
    {
        if (choice is PlayerChoice.ChosenChoice chosen)
        {
            return new ChoiceApiResponse("Chosen", chosen.CharacterId, chosen.OfferedIds, false, chosen.HiddenFlags);
        }

        var unchosen = (PlayerChoice.UnchosenChoice)choice;
        return new ChoiceApiResponse("Unchosen", string.Empty, unchosen.OfferedIds, unchosen.IsAtheistCommitmentConfirmed, new HiddenFlags(false, false));
    }

    private static MakeupSummaryApiResponse MapMakeupSummary(MakeupSummary summary)
    {
        return new MakeupSummaryApiResponse(
            MapSetupCounts(summary.CurrentCounts),
            summary.TargetCounts.Select(MapSetupCounts).ToList().AsReadOnly(),
            summary.ChosenCharactersByType.ToDictionary(
                pair => pair.Key.ToString(),
                pair => pair.Value,
                StringComparer.Ordinal),
            summary.RemainingSeats,
            summary.OutOfScriptCharacterIds);
    }

    private static SetupCountsApiResponse MapSetupCounts(SetupCounts counts)
    {
        return new SetupCountsApiResponse(counts.Townsfolk, counts.Outsiders, counts.Minions, counts.Demons);
    }
}
