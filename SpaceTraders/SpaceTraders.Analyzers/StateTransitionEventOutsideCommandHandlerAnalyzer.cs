using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SpaceTraders.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StateTransitionEventOutsideCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ST0001";
    public const string TopLevelHandlerDiagnosticId = "ST0002";
    public const string NonChainOfCommandHandlerDiagnosticId = "ST0003";
    public const string HandlerNamingConventionDiagnosticId = "ST0004";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "State-transition events must be emitted only from command handlers",
        "State-transition event '{0}' must only be emitted from command handler classes in SpaceTraeders.Application.Commands.*",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensures post-POST state-transition events are emitted in a single place: command handlers.");

    private static readonly DiagnosticDescriptor TopLevelHandlerRule = new(
        TopLevelHandlerDiagnosticId,
        "Handlers must target top-level chain events",
        "Handler '{0}' targets derived event '{1}'. Handlers must target events that derive directly from 'ChainOfCommandEvent'.",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Prevents handlers for derived chain events. Handlers must implement IChainOfCommandEventHandler for events whose base type is directly ChainOfCommandEvent.");

    private static readonly DiagnosticDescriptor NonChainOfCommandHandlerRule = new(
        NonChainOfCommandHandlerDiagnosticId,
        "Event handler must be part of chain of command pattern",
        "Handler '{0}' for event '{1}' does not implement IChainOfCommandEventHandler<T>. Only chain of command handlers are allowed for events.",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensures all event handlers follow the chain of command pattern. Standalone event handlers are not allowed.");

    private static readonly DiagnosticDescriptor HandlerNamingConventionRule = new(
        HandlerNamingConventionDiagnosticId,
        "Handler naming convention violation",
        "Handler '{0}' does not follow naming convention. Expected: '{1}' or '{1}' with role suffix (e.g., 'ShipDockedMineEventHandler'). If this error is triggered, most likely event handlers need to be merged into a single generic handler.",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Handler class names must follow the pattern <TopLevelEventName>Handler or <TopLevelEventBaseName>[Role]EventHandler (e.g., ShipDockedEvent → ShipDockedEventHandler or ShipDockedMineEventHandler). The role segment is inserted between the event base name and 'EventHandler'.");

    private static readonly ImmutableHashSet<string> StateTransitionEvents =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ShipDockedEvent",
            "ShipUndockedEvent",
            "ShipInTransitEvent");

    private static readonly Dictionary<string, string> TopLevelEventNameMap = new(StringComparer.Ordinal)
    {
        { "ShipInOrbitEvent", "ShipInOrbitEvent" },
        { "ShipDockedEvent", "ShipDockedEvent" },
        { "ShipInTransitEvent", "ShipInTransitEvent" },
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule, TopLevelHandlerRule, NonChainOfCommandHandlerRule, HandlerNamingConventionRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterCompilationAction(AnalyzeCompilation);
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

        var syntaxTreePath = creation.SyntaxTree?.FilePath ?? string.Empty;
        if (!syntaxTreePath.Contains("Events\\Handlers", StringComparison.OrdinalIgnoreCase)
            && !syntaxTreePath.Contains("EventHandlers", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType is null)
        {
            return;
        }

        var containingNamespace = containingType.ContainingNamespace.ToDisplayString();
        if (!containingNamespace.StartsWith("SpaceTraders.Application.", StringComparison.Ordinal))
        {
            return;
        }

        if (IsAllowedCommandHandler(containingType))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(Rule, creation.GetLocation(), createdType.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol namedType || namedType.TypeKind != TypeKind.Class)
        {
            return;
        }

        foreach (var @interface in namedType.AllInterfaces)
        {
            if (!@interface.OriginalDefinition.ToDisplayString().Equals(
                    "SpaceTraders.Application.Events.Handlers.IChainOfCommandEventHandler<TEvent>",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var eventType = @interface.TypeArguments[0] as INamedTypeSymbol;
            if (eventType is null)
            {
                continue;
            }

            if (!IsChainEvent(eventType) || IsDirectlyDerivedFromChainOfCommandEvent(eventType))
            {
                continue;
            }

            var location = namedType.Locations.Length > 0 ? namedType.Locations[0] : Location.None;
            var diagnostic = Diagnostic.Create(TopLevelHandlerRule, location, namedType.Name, eventType.Name);
            context.ReportDiagnostic(diagnostic);
            return;
        }
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var chainOfCommandInterfaceName = "SpaceTraders.Application.Events.Handlers.IChainOfCommandEventHandler";

        foreach (var syntaxTree in context.Compilation.SyntaxTrees)
        {
            var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(context.CancellationToken);

            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDecl in classDeclarations)
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken);
                if (classSymbol is null)
                {
                    continue;
                }

                var isChainOfCommandHandler = false;
                string handledEventType = null;

                foreach (var @interface in classSymbol.AllInterfaces)
                {
                    if (@interface.OriginalDefinition.ToDisplayString().StartsWith(chainOfCommandInterfaceName, StringComparison.Ordinal))
                    {
                        isChainOfCommandHandler = true;
                        if (@interface.TypeArguments.Length == 1)
                        {
                            var eventType = @interface.TypeArguments[0] as INamedTypeSymbol;
                            if (eventType is not null && IsDirectlyDerivedFromChainOfCommandEvent(eventType))
                            {
                                handledEventType = eventType.Name;
                            }
                        }
                    }
                }

                // Check if it's a handler for ship events but doesn't implement chain of command
                if (ClassNameIndicatesEventHandler(classSymbol.Name))
                {
                    if (!isChainOfCommandHandler && classSymbol.ContainingNamespace.ToDisplayString().Contains("EventHandlers", StringComparison.OrdinalIgnoreCase)
                        && classSymbol.ContainingNamespace.ToDisplayString().Contains("Ships", StringComparison.OrdinalIgnoreCase))
                    {
                        // It's an event handler but not a chain of command handler - report it
                        var location = classSymbol.Locations.Length > 0 ? classSymbol.Locations[0] : Location.None;

                        var eventTypeName = "ChainOfCommandEvent";
                        var handleMethod = classSymbol.GetMembers("HandleAsync").FirstOrDefault() as IMethodSymbol;
                        if (handleMethod?.Parameters.Length > 0)
                        {
                            eventTypeName = handleMethod.Parameters[0].Type.Name;
                        }

                        var diagnostic = Diagnostic.Create(
                            NonChainOfCommandHandlerRule,
                            location,
                            classSymbol.Name,
                            eventTypeName);
                        context.ReportDiagnostic(diagnostic);
                    }
                    else if (isChainOfCommandHandler && handledEventType is not null && TopLevelEventNameMap.ContainsKey(handledEventType))
                    {
                        // Validate naming convention
                        var expectedEventName = TopLevelEventNameMap[handledEventType];
                        // Strip "Event" suffix to get base name for the convention check
                        var expectedHandlerBase = expectedEventName + "Handler"; // e.g., "ShipInOrbitEventHandler"

                        if (!MatchesNamingConvention(classSymbol.Name, expectedHandlerBase))
                        {
                            var location = classSymbol.Locations.Length > 0 ? classSymbol.Locations[0] : Location.None;
                            var diagnostic = Diagnostic.Create(
                                HandlerNamingConventionRule,
                                location,
                                classSymbol.Name,
                                expectedHandlerBase);
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }
            }
        }
    }

    private static bool MatchesNamingConvention(string handlerName, string expectedBase)
    {
        // All handlers follow the consistent pattern: Ship[State][Role]EventHandler
        // Generic form: ShipDockedEventHandler, ShipInOrbitEventHandler, ShipInTransitEventHandler
        // Role-specific form: ShipDocked[Role]EventHandler, ShipInOrbit[Role]EventHandler, ShipInTransit[Role]EventHandler
        // Examples: ShipDockedMineEventHandler, ShipInOrbitScoutEventHandler, ShipInTransitMinerEventHandler

        if (handlerName.Equals(expectedBase, StringComparison.Ordinal))
        {
            return true;
        }

        // Check if it matches expectedBase with [Role] infix pattern
        if (expectedBase.EndsWith("EventHandler", StringComparison.Ordinal))
        {
            var baseWithoutHandler = expectedBase.Substring(0, expectedBase.Length - "EventHandler".Length);
            var suffix = "EventHandler";

            if (handlerName.StartsWith(baseWithoutHandler, StringComparison.Ordinal)
                && handlerName.EndsWith(suffix, StringComparison.Ordinal))
            {
                var roleCandidate = handlerName.Substring(baseWithoutHandler.Length, handlerName.Length - baseWithoutHandler.Length - suffix.Length);
                // Role should be non-empty and PascalCase
                return roleCandidate.Length > 0 && char.IsUpper(roleCandidate[0]);
            }
        }

        return false;
    }

    private static bool ClassNameIndicatesEventHandler(string className)
    {
        return className.EndsWith("EventHandler", StringComparison.Ordinal) ||
               className.EndsWith("Handler", StringComparison.Ordinal);
    }

    private static bool IsChainEvent(INamedTypeSymbol eventType)
    {
        var current = eventType.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString().Equals("SpaceTraders.Domain.Events.ChainOfCommandEvent", StringComparison.Ordinal))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool IsDirectlyDerivedFromChainOfCommandEvent(INamedTypeSymbol eventType)
    {
        var baseType = eventType.BaseType;
        return baseType is not null
            && baseType.ToDisplayString().Equals("SpaceTraders.Domain.Events.ChainOfCommandEvent", StringComparison.Ordinal);
    }

    private static bool IsAllowedCommandHandler(INamedTypeSymbol containingType)
    {
        var namespaceName = containingType.ContainingNamespace.ToDisplayString();
        if (!namespaceName.StartsWith("SpaceTraders.Application.Commands.", StringComparison.Ordinal))
        {
            return false;
        }

        return containingType.Name.EndsWith("Handler", StringComparison.Ordinal);
    }
}
