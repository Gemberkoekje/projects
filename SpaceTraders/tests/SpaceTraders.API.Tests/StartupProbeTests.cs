using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.API.Services;

namespace SpaceTraders.API.Tests;

/// <summary>
/// B39: Kubernetes' startup probe reads <c>/health/startup</c> (PLAN.md slice 4.2, for B23). It
/// answered 200 while the startup chain was still running, because the health check reports that as
/// Degraded and Degraded answers 200 by default: the probe passed before the chain had completed.
/// </summary>
/// <remarks>
/// The test host doesn't run the startup chain, so the state is only what each test sets.
/// </remarks>
public sealed class StartupProbeTests
{
    private const string ProbePath = "/health/startup";

    [Fact]
    public async Task TheStartupProbe_Fails_WhileTheStartupChainRuns()
    {
        await using var factory = new SpaceTradersApiFactory();
        using var client = factory.CreateClient();
        factory.Services.GetRequiredService<StartupInitializationState>().MarkRunning();

        using var response = await client.GetAsync(new Uri(ProbePath, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task TheStartupProbe_Succeeds_OnceTheStartupChainHasCompleted()
    {
        await using var factory = new SpaceTradersApiFactory();
        using var client = factory.CreateClient();
        factory.Services.GetRequiredService<StartupInitializationState>().MarkCompleted();

        using var response = await client.GetAsync(new Uri(ProbePath, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheStartupProbe_Fails_WhenTheStartupChainFailed()
    {
        await using var factory = new SpaceTradersApiFactory();
        using var client = factory.CreateClient();
        factory.Services.GetRequiredService<StartupInitializationState>().MarkFailed(new InvalidOperationException("Agent bootstrap failed."));

        using var response = await client.GetAsync(new Uri(ProbePath, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
