using Kluskat.core.Validators;
using Qowaiv.DomainModel;

namespace Kluskat.core.Models;

/// <summary>
/// The one aggregate of this app: a household chore that comes back every so many days. Every
/// mutation returns a <see cref="Result{T}"/>; nothing throws for a business failure.
/// </summary>
public sealed class Klus(KlusId aggregateId) : Aggregate<Klus, KlusId>(aggregateId, new KlusValidator())
{
    /// <summary>Where in the house, e.g. "Keuken".</summary>
    public string Ruimte { get; private set; } = string.Empty;

    /// <summary>What to do, e.g. "Stofzuigen".</summary>
    public string Naam { get; private set; } = string.Empty;

    /// <summary>How many days the klus stays away after it is done.</summary>
    public int IntervalDagen { get; private set; }

    /// <summary>Roughly how long it takes.</summary>
    public Grootte Grootte { get; private set; }

    /// <summary>When it was last done (UTC).</summary>
    public DateTime LaatstGedaan { get; private set; }

    /// <summary>
    /// The <see cref="LaatstGedaan"/> from before the latest completion. Kept so a mis-tapped
    /// "Gedaan" can be taken back exactly, once.
    /// </summary>
    public DateTime VorigeLaatstGedaan { get; private set; }

    /// <summary>Whether the latest completion can still be taken back.</summary>
    public bool KanTerugdraaien { get; private set; }

    /// <summary>When the klus was last skipped (UTC); phase 2 reads it for the bravery bonus.</summary>
    public DateTime LaatstOvergeslagen { get; private set; }

    /// <summary>Whether the klus left the list for good.</summary>
    public bool Geschrapt { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Klus"/> class with a fresh id; the repository
    /// uses this to replay a stream.
    /// </summary>
    public Klus()
        : this(KlusId.Next())
    {
    }

    /// <summary>Adds a klus to the list.</summary>
    /// <param name="data">What, where, how often and how big.</param>
    /// <param name="laatstGedaan">When it was last done (UTC); chooses when it is first due.</param>
    /// <returns>The new klus, or the validation messages that stopped it.</returns>
    public static Result<Klus> Toevoegen(Data data, DateTime laatstGedaan)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new Klus(KlusId.Next()).ApplyEvent(new KlusToegevoegd
        {
            Ruimte = (data.Ruimte ?? string.Empty).Trim(),
            Naam = (data.Naam ?? string.Empty).Trim(),
            IntervalDagen = data.IntervalDagen,
            Grootte = data.Grootte,
            LaatstGedaan = laatstGedaan,
        });
    }

    /// <summary>Edits the descriptive fields.</summary>
    /// <param name="data">The new values.</param>
    /// <returns>This klus, unchanged when nothing differs, or the messages that stopped it.</returns>
    public Result<Klus> Aanpassen(Data data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (Geschrapt)
        {
            return IsGeschrapt();
        }

        var ruimte = (data.Ruimte ?? string.Empty).Trim();
        var naam = (data.Naam ?? string.Empty).Trim();

        if (string.Equals(Ruimte, ruimte, StringComparison.Ordinal)
            && string.Equals(Naam, naam, StringComparison.Ordinal)
            && IntervalDagen == data.IntervalDagen
            && Grootte == data.Grootte)
        {
            return Result.For(this);
        }

        return ApplyEvent(new KlusAangepast
        {
            Ruimte = ruimte,
            Naam = naam,
            IntervalDagen = data.IntervalDagen,
            Grootte = data.Grootte,
        });
    }

    /// <summary>Ticks the klus off: it goes away for <see cref="IntervalDagen"/> days.</summary>
    /// <param name="op">When it was done (UTC).</param>
    /// <param name="viaBeheer">Whether this is filled in afterwards on the beheer page.</param>
    /// <returns>This klus, or the messages that stopped it.</returns>
    public Result<Klus> Gedaan(DateTime op, bool viaBeheer)
    {
        if (Geschrapt)
        {
            return IsGeschrapt();
        }

        // A date filled in afterwards can only move the klus forward: "I did it on Tuesday" must not
        // undo a completion from Thursday that the app already knows about.
        if (op < LaatstGedaan)
        {
            return Result.WithMessages<Klus>(ValidationMessage.Error("Die datum ligt vóór de vorige keer dat deze klus gedaan is."));
        }

        if (op > Clock.UtcNow().AddMinutes(5))
        {
            return Result.WithMessages<Klus>(ValidationMessage.Error("Een klus kan niet in de toekomst gedaan zijn."));
        }

        return ApplyEvent(new KlusGedaan { GedaanOp = op, ViaBeheer = viaBeheer });
    }

    /// <summary>Takes back the latest "Gedaan", for when it was a mis-tap.</summary>
    /// <returns>This klus, back to its previous last-done moment, or the messages that stopped it.</returns>
    public Result<Klus> Terugdraaien()
    {
        if (!KanTerugdraaien)
        {
            return Result.WithMessages<Klus>(ValidationMessage.Error("Er is niets om terug te draaien."));
        }

        return ApplyEvent(new KlusGedaanTeruggedraaid { LaatstGedaan = VorigeLaatstGedaan });
    }

    /// <summary>Records that the klus was on screen and the user asked for another one.</summary>
    /// <param name="reden">Which of the two skip buttons.</param>
    /// <returns>This klus, or the messages that stopped it.</returns>
    public Result<Klus> Overslaan(OverslaanReden reden)
    {
        if (Geschrapt)
        {
            return IsGeschrapt();
        }

        if (reden == OverslaanReden.None)
        {
            return Result.WithMessages<Klus>(ValidationMessage.Error("Overslaan heeft een reden nodig: geen zin of geen tijd."));
        }

        return ApplyEvent(new KlusOvergeslagen { Reden = reden });
    }

    /// <summary>Takes the klus off the list for good.</summary>
    /// <returns>This klus.</returns>
    public Result<Klus> Schrappen()
        => Geschrapt ? Result.For(this) : ApplyEvent(new KlusGeschrapt());

    /// <summary>Marks every buffered event as committed, after the repository stored them.</summary>
    public void MarkAllAsCommitted() => Buffer = Buffer.MarkAllAsCommitted();

    internal void When(KlusToegevoegd @event)
    {
        Ruimte = @event.Ruimte;
        Naam = @event.Naam;
        IntervalDagen = @event.IntervalDagen;
        Grootte = @event.Grootte;
        LaatstGedaan = @event.LaatstGedaan;
    }

    internal void When(KlusAangepast @event)
    {
        Ruimte = @event.Ruimte;
        Naam = @event.Naam;
        IntervalDagen = @event.IntervalDagen;
        Grootte = @event.Grootte;
    }

    internal void When(KlusGedaan @event)
    {
        VorigeLaatstGedaan = LaatstGedaan;
        LaatstGedaan = @event.GedaanOp;
        KanTerugdraaien = true;
    }

    internal void When(KlusGedaanTeruggedraaid @event)
    {
        LaatstGedaan = @event.LaatstGedaan;
        KanTerugdraaien = false;
    }

    internal void When(KlusOvergeslagen @event)
    {
        // The skip window itself lives in the Sessie document; this is history.
        LaatstOvergeslagen = @event.When;
    }

    internal void When(KlusGeschrapt @event)
    {
        _ = @event;
        Geschrapt = true;
    }

    /// <summary>The single "this klus is gone" answer, shared by every guard.</summary>
    private static Result<Klus> IsGeschrapt()
        => Result.WithMessages<Klus>(ValidationMessage.Error("Deze klus is geschrapt."));

    /// <summary>The descriptive fields of a klus, as one bag, so seed, form and command share it.</summary>
    public sealed record Data
    {
        /// <summary>Where in the house.</summary>
        public string Ruimte { get; init; } = string.Empty;

        /// <summary>What to do.</summary>
        public string Naam { get; init; } = string.Empty;

        /// <summary>How many days the klus stays away after it is done.</summary>
        public int IntervalDagen { get; init; }

        /// <summary>Roughly how long it takes.</summary>
        public Grootte Grootte { get; init; }
    }
}
