using System.Net;
using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

namespace SpaceTraders.Application.Tests.RateLimiting;

public sealed class RetryHandlerTests
{
    private static RetryHandler CreateHandler(IApiAvailabilityState? availabilityState = null)
    {
        availabilityState ??= Substitute.For<IApiAvailabilityState>();
        return new RetryHandler(availabilityState);
    }

    [Fact]
    public async Task Handle_502Once_RetriesAndSucceeds()
    {
        var callCount = 0;
        var innerHandler = new CallbackMessageHandler(req =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var handler = CreateHandler();
        handler.InnerHandler = innerHandler;
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_Persistent502_ReturnsLastBadGateway()
    {
        var innerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var handler = CreateHandler();
        handler.InnerHandler = innerHandler;
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Handle_Persistent502_MarksApiUnavailable()
    {
        var availability = Substitute.For<IApiAvailabilityState>();
        var innerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var handler = CreateHandler(availability);
        handler.InnerHandler = innerHandler;
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        availability.Received().MarkUnavailable();
    }

    [Fact]
    public async Task Handle_200Ok_DoesNotRetry()
    {
        var callCount = 0;
        var innerHandler = new CallbackMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var handler = CreateHandler();
        handler.InnerHandler = innerHandler;
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SuccessfulRequest_MarksApiAvailable()
    {
        var availability = Substitute.For<IApiAvailabilityState>();
        var innerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var handler = CreateHandler(availability);
        handler.InnerHandler = innerHandler;
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        availability.Received().MarkAvailable();
    }
}

public sealed class RateLimitResponseHandlerTests
{
    [Fact]
    public async Task Handle_NonThrottled_ReturnsResponse()
    {
        var status = new RateLimitStatus();
        var innerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var handler = new RateLimitResponseHandler(status) { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        status.ThrottledCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_429Response_IncrementsThrottledCount()
    {
        var status = new RateLimitStatus();
        var callCount = 0;
        var innerHandler = new CallbackMessageHandler(_ =>
        {
            callCount++;
            // First call: 429; second call: 200
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var handler = new RateLimitResponseHandler(status) { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        status.ThrottledCount.Should().Be(1);
    }
}

internal sealed class CallbackMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(callback(request));
}
