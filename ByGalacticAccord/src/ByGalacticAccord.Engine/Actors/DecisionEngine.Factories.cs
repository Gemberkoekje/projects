using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

public static partial class DecisionEngine
{
    /// <summary>How long a factory waits for a missing input before it makes do without it, a grade lower (design §15.4).</summary>
    public const long MakeDoAfterHours = 72;

    /// <summary>A factory restocks an input when its stock, with what is on its way, falls below this share of its target.</summary>
    private const decimal InputRestockBelow = 0.6m;

    /// <summary>
    /// Factory heartbeat (design §4.1, §5.3, §15.4): finish the part it was making; start the next, first one somebody it can
    /// see has posted a wanted Sale for, else the part it has least of; buy inputs that run short with delivered wanted Sales;
    /// and sell its parts as a producer sells its goods: at its door, and delivered elsewhere with a Haul.
    /// </summary>
    public static void RunFactory(SimulationContext context, ActorState factory)
    {
        if (factory.Factory is not { } profile)
        {
            return;
        }

        FinishWork(context, factory);
        StartWork(context, factory, profile);
        BuyInputs(context, factory, profile);

        foreach (SaleContract wanted in context.OpenContracts.OfType<SaleContract>().Where(s => s.Side == SaleSide.Wanted && s.Place == factory.Home))
        {
            TryAnswer(context, factory, wanted);
        }

        foreach (GoodType good in factory.Products)
        {
            if (factory.Available(good) >= 1m)
            {
                SellDelivered(context, factory, profile.ProfileFor(good));
            }
        }

        ArrangeTransport(context, factory);
    }

    // A part is done once its hours are up: it goes into stock.
    private static void FinishWork(SimulationContext context, ActorState factory)
    {
        if (factory.Work is { } work && context.CurrentTick >= work.Finishes)
        {
            context.Produce(factory, ShipPart.GoodOf(work.Kind, work.Grade), 1m);
            context.NotePartMade(work.Grade);
            factory.Work = null;
        }
    }

    /// <summary>
    /// What a factory makes next, if it is free (design §5.3: output grade equals the factory's quality, lowered when it makes do
    /// without an input): a part somebody wants that it doesn't have, at the grade wanted; else the kind of which it holds
    /// least, against its cap, at its own grade. Short of inputs, it waits for them, unless the part is wanted, or it has none
    /// of it and has waited <see cref="MakeDoAfterHours"/>: then it makes it without what is missing, a grade lower for each.
    /// </summary>
    private static void StartWork(SimulationContext context, ActorState factory, FactoryProfile profile)
    {
        if (factory.Work is not null || NextPart(context, factory, profile) is not { } next)
        {
            return;
        }

        (PartRecipe recipe, Grade wanted, bool pulled) = next;
        var missing = recipe.Inputs.Where(i => factory.Available(i.Good) < i.Quantity).ToList();
        int allowed = Math.Max(0, ShipPart.StepsShort(profile.Quality, wanted));
        Grade grade = wanted;
        if (missing.Count > allowed)
        {
            bool waitedLongEnough = factory.ShortSince is { } since && context.CurrentTick - since >= MakeDoAfterHours
                                    && factory.Stock(ShipPart.GoodOf(recipe.Kind, profile.Quality)) <= 0m;
            int lowered = (int)profile.Quality + missing.Count;
            if (!(pulled || waitedLongEnough) || lowered > (int)Grade.C || missing.Count >= recipe.Inputs.Count)
            {
                factory.ShortSince ??= context.CurrentTick;
                return;
            }

            grade = (Grade)Math.Max(lowered, (int)wanted);
        }

        Credits labour = profile.Labour(recipe.Kind, grade);
        if (factory.Credits < labour)
        {
            return;
        }

        context.ApplySink(factory, labour);
        foreach (RecipeInput input in recipe.Inputs)
        {
            if (!missing.Contains(input))
            {
                context.Consume(factory, input.Good, input.Quantity);
            }
        }

        if (grade != profile.Quality)
        {
            context.NoteMadeDo();
        }

        factory.Work = new FactoryWork(recipe.Kind, grade, context.CurrentTick + recipe.Hours);
        factory.ShortSince = null;
    }

    // The part to make next: a wanted one first (the oldest wanted Sale it knows of, for a part it makes and doesn't hold, at a
    // grade it can make), else the kind it holds least of at its own grade, while under its cap.
    private static (PartRecipe Recipe, Grade Grade, bool Pulled)? NextPart(SimulationContext context, ActorState factory, FactoryProfile profile)
    {
        foreach (SaleContract wanted in Known<SaleContract>(context, factory))
        {
            if (wanted.Side != SaleSide.Wanted || !factory.Makes(wanted.Good) || factory.Available(wanted.Good) >= wanted.Quantity)
            {
                continue;
            }

            (PartKind kind, Grade grade) = ShipPart.Of(wanted.Good);
            if (grade >= profile.Quality && profile.RecipeFor(kind) is { } recipe)
            {
                return (recipe, grade, true);
            }
        }

        PartRecipe? least = null;
        decimal leastShare = 1m;
        foreach (PartRecipe recipe in profile.Recipes)
        {
            decimal share = factory.Stock(ShipPart.GoodOf(recipe.Kind, profile.Quality)) / Math.Max(1m, recipe.StockCap);
            if (share < leastShare)
            {
                least = recipe;
                leastShare = share;
            }
        }

        return least is null ? null : (least, profile.Quality, false);
    }

    /// <summary>
    /// A factory buys an input that runs short (design §4.1: factories post wanted Sales for inputs), delivered to its door,
    /// priced as a consumer prices what it wants: from the cheapest source it knows, worth more to it the emptier its stock.
    /// </summary>
    private static void BuyInputs(SimulationContext context, ActorState factory, FactoryProfile profile)
    {
        foreach (GoodType input in profile.Inputs)
        {
            decimal target = profile.InputTarget(input);
            decimal stock = factory.Stock(input);
            decimal inbound = Inbound(context, factory, input);
            if (stock + inbound >= target * InputRestockBelow)
            {
                continue;
            }

            decimal deficit = Math.Floor(Math.Min(MaxPostQuantity, target - stock - inbound));
            if (deficit < 1m)
            {
                continue;
            }

            PostWanted(context, factory, input, deficit, InputWorth(input, stock, target));
        }
    }

    /// <summary>What a unit of an input is worth to a factory: its base value 1.2 times with its stock full, up to twice with none.</summary>
    public static decimal InputWorth(GoodType input, decimal stock, decimal target)
    {
        decimal need = target <= 0m ? 0m : Math.Clamp((target - stock) / target, 0m, 1m);
        return Goods.Info(input).BaseValue * (1.2m + (0.8m * need));
    }

    /// <summary>The parts a factory has in stock, by good, in its products' order: what the interface lists as its lot.</summary>
    public static IEnumerable<(GoodType Good, decimal Quantity)> PartsInStock(ActorState factory) =>
        factory.Products.Where(g => factory.Stock(g) > 0m).Select(g => (g, factory.Stock(g)));
}
