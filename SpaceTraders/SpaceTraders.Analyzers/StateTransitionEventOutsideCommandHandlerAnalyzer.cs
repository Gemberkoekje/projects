using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SpaceTraders.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StateTransitionEventOutsideCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ST0001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "State-transition events must be emitted only from command handlers",
        "State-transition event '{0}' must only be emitted from command handler classes in SpaceTraders.Application.Commands.*",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensures post-POST state-transition events are emitted in a single place: command handlers.");

    /// <summary>
    /// The flat list of state-transition events that may only be constructed inside command handlers or test code.
    /// Phase 7g: updated from the old chain-routing set to the full set of plain factual ship state events.
    /// Phase 13b: ShipDockedEvent and ShipInOrbitEvent removed (deleted as Tier 3 events).
    /// </summary>
    private static readonly ImmutableHashSet<string> StateTransitionEvents =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ShipInTransitEvent",
            "ShipStateMismatchEvent");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ObjectCreationExpressionSyntax creation)
        {
            return;
        }

        var createdType = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type as INamedTypeSymbol;
        if (createdType is null)
        {
            return;
        }

        if (!createdType.ContainingNamespace.ToDisplayString().Equals("SpaceTraders.Domain.Events.Ships", StringComparison.Ordinal)
            || !StateTransitionEvents.Contains(createdType.Name))
        {
            return;
        }

        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType is null)
        {
            return;
        }

        var containingNamespace = containingType.ContainingNamespace.ToDisplayString();

        // Only enforce within the SpaceTraders.Application.* namespace; API services and test projects are excluded.
        if (!containingNamespace.StartsWith("SpaceTraders.Application.", StringComparison.Ordinal))
        {
            return;
        }

        // Allow test projects (namespace segment contains "Tests").
        if (containingNamespace.Contains("Tests", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Allow any class inside the Commands namespace (handlers, helpers, publishers, etc.).
        if (IsAllowedCommandsNamespace(containingType))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(Rule, creation.GetLocation(), createdType.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsAllowedCommandsNamespace(INamedTypeSymbol containingType)
    {
        var namespaceName = containingType.ContainingNamespace.ToDisplayString();
        return namespaceName.StartsWith("SpaceTraders.Application.Commands.", StringComparison.Ordinal)
            || namespaceName.Equals("SpaceTraders.Application.Commands", StringComparison.Ordinal);
    }
}
