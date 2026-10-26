using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>One answer to a decision: what it is called and what it does, from the terms.</summary>
public sealed record DecisionOption(DecisionChoice Choice, string Label, string Note);

/// <summary>
/// A decision the terms handed the player, as the prompt that stops the clock (design §8.3): what happened, what is on
/// its way, and the answers with what each one does. Nothing here is a recommendation; each option says only what the
/// terms make of it.
/// </summary>
public sealed record DecisionPrompt(PlayerDecision Decision, ContractId Contract, string Title, string Text, IReadOnlyList<DecisionOption> Options);

public static class DecisionView
{
    public static IReadOnlyList<DecisionPrompt> Build(GameSession session)
    {
        var prompts = new List<DecisionPrompt>();
        foreach (PlayerDecision decision in session.Decisions)
        {
            prompts.Add(Prompt(session, decision));
        }

        return prompts;
    }

    private static DecisionPrompt Prompt(GameSession session, PlayerDecision decision)
    {
        SimulationContext context = session.Context;
        Contract contract = context.Contract(decision.Contract);
        string performer = context.Actor(contract.Performer!.Value).Name;
        (GoodType good, decimal quantity, LocationId place, LocationId destination) = contract switch
        {
            HaulContract h => (h.Good, h.Quantity, h.Destination, h.Destination),
            SaleContract s => (s.Good, s.Quantity, s.Place, s.Place),
            _ => (GoodType.Food, 0m, contract.Places[0], contract.Places[0]),
        };
        var facts = new List<(string Name, string Value)>
        {
            ("performer", performer),
            ("quantity", Format.Quantity(decision.Kind == DecisionKind.Inspection ? decision.Quantity : quantity)),
            ("good", Templates.Good(good)),
            ("place", context.Location(place).Name),
            ("destination", context.Location(destination).Name),
            ("when", Format.Date(contract.GraceEndsTick)),
            ("bond", Format.Money(contract.LockedBond)),
            ("escrow", Format.Money(contract.EscrowHeld - contract.LockedBond)),
        };

        var options = new List<DecisionOption>();
        string text;
        if (decision.Kind == DecisionKind.CancelForDefault)
        {
            text = Templates.Fill("Decision.CancelForDefault." + contract.Type, facts.ToArray());
            text += " " + (Market.ExpectedArrival(context, contract) is { } eta
                ? Templates.Fill("Decision.UnderWay", ("eta", Format.Relative(eta, session.Now)))
                : Templates.Get("Decision.NothingComing"));
            PenaltyOnLateDelivery? late = contract.LatePenalty;
            options.Add(new DecisionOption(
                DecisionChoice.Cancel, Templates.Get("Decision.Cancel"),
                Templates.Fill(contract.UsesEscrow ? "Decision.Cancel.Note" : "Decision.Cancel.Note.Frontier", facts.ToArray())));
            options.Add(new DecisionOption(
                DecisionChoice.Wait, Templates.Get("Decision.Wait"),
                Templates.Fill("Decision.Wait.Note", ("perHour", Format.MoneyCents(late?.PenaltyPerTickLate ?? 0m)), ("cap", Format.Money(late?.MaxPenalty ?? Credits.Zero)))));
        }
        else
        {
            QualityWarranty? warranty = contract.Warranty;
            SettlementTerms terms = Settlement.Due(context, contract, quantity, decision.Quantity, decision.Quality, 0);
            facts.Add(("quality", Format.Percent(decision.Quality)));
            facts.Add(("floor", Format.Percent(warranty?.RejectBelow ?? 0m)));
            facts.Add(("payment", Format.Money(terms.Payment)));
            facts.Add(("refund", Format.Money(terms.QualityRefund)));
            text = Templates.Fill("Decision.Inspection", facts.ToArray());
            options.Add(new DecisionOption(DecisionChoice.Accept, Templates.Get("Decision.Accept"), Templates.Fill("Decision.Accept.Note", facts.ToArray())));
            options.Add(new DecisionOption(
                DecisionChoice.Reject, Templates.Get("Decision.Reject"),
                Templates.Fill(contract.UsesEscrow ? "Decision.Reject.Note" : "Decision.Reject.Note.Frontier", facts.ToArray())));
        }

        return new DecisionPrompt(decision, contract.Id, Templates.Get("Decision.Title"), text, options);
    }
}
