using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2Extractor.Annotation.Providers;

internal sealed class LlmRateLimitSettings
{
    public int RequestsPerMinute { get; init; }

    public int InputTokensPerMinute { get; init; }

    public int OutputTokensPerMinute { get; init; }
}

internal sealed class LlmRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly object _sync = new object();
    private readonly int _requestsPerMinute;
    private readonly int _inputTokensPerMinute;
    private readonly int _outputTokensPerMinute;
    private readonly Queue<DateTimeOffset> _requestTimes;
    private readonly Queue<TokenReservation> _inputReservations;
    private readonly Queue<TokenReservation> _outputReservations;

    private int _inputTokensInWindow;
    private int _outputTokensInWindow;

    public LlmRateLimiter(LlmRateLimitSettings settings)
    {
        _requestsPerMinute = settings.RequestsPerMinute;
        _inputTokensPerMinute = settings.InputTokensPerMinute;
        _outputTokensPerMinute = settings.OutputTokensPerMinute;
        _requestTimes = new Queue<DateTimeOffset>();
        _inputReservations = new Queue<TokenReservation>();
        _outputReservations = new Queue<TokenReservation>();
    }

    public bool IsEnabled => _requestsPerMinute > 0 || _inputTokensPerMinute > 0 || _outputTokensPerMinute > 0;

    public async Task<TimeSpan> ReserveAsync(int estimatedInputTokens, int estimatedOutputTokens, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return TimeSpan.Zero;
        }

        DateTimeOffset start = DateTimeOffset.UtcNow;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan wait;
            lock (_sync)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                TrimWindow(now);

                wait = ComputeWait(now, estimatedInputTokens, estimatedOutputTokens);
                if (wait <= TimeSpan.Zero)
                {
                    Reserve(now, estimatedInputTokens, estimatedOutputTokens);
                    return now - start;
                }
            }

            await Task.Delay(wait, cancellationToken);
        }
    }

    private TimeSpan ComputeWait(DateTimeOffset now, int estimatedInputTokens, int estimatedOutputTokens)
    {
        TimeSpan wait = TimeSpan.Zero;

        if (_requestsPerMinute > 0 && _requestTimes.Count >= _requestsPerMinute)
        {
            DateTimeOffset nextRequestSlot = _requestTimes.Peek().Add(Window);
            TimeSpan requestWait = nextRequestSlot - now;
            if (requestWait > wait)
            {
                wait = requestWait;
            }
        }

        if (_inputTokensPerMinute > 0 && estimatedInputTokens > 0 && estimatedInputTokens <= _inputTokensPerMinute)
        {
            TimeSpan inputWait = ComputeTokenWait(_inputReservations, _inputTokensInWindow, _inputTokensPerMinute, estimatedInputTokens, now);
            if (inputWait > wait)
            {
                wait = inputWait;
            }
        }

        if (_outputTokensPerMinute > 0 && estimatedOutputTokens > 0 && estimatedOutputTokens <= _outputTokensPerMinute)
        {
            TimeSpan outputWait = ComputeTokenWait(_outputReservations, _outputTokensInWindow, _outputTokensPerMinute, estimatedOutputTokens, now);
            if (outputWait > wait)
            {
                wait = outputWait;
            }
        }

        return wait;
    }

    private static TimeSpan ComputeTokenWait(
        Queue<TokenReservation> reservations,
        int tokensInWindow,
        int limit,
        int requestedTokens,
        DateTimeOffset now)
    {
        if (tokensInWindow + requestedTokens <= limit)
        {
            return TimeSpan.Zero;
        }

        int requiredToExpire = (tokensInWindow + requestedTokens) - limit;
        int released = 0;
        foreach (TokenReservation reservation in reservations)
        {
            released += reservation.Tokens;
            if (released >= requiredToExpire)
            {
                DateTimeOffset availableAt = reservation.Timestamp.Add(Window);
                TimeSpan wait = availableAt - now;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }
        }

        return TimeSpan.Zero;
    }

    private void Reserve(DateTimeOffset now, int estimatedInputTokens, int estimatedOutputTokens)
    {
        if (_requestsPerMinute > 0)
        {
            _requestTimes.Enqueue(now);
        }

        if (_inputTokensPerMinute > 0 && estimatedInputTokens > 0 && estimatedInputTokens <= _inputTokensPerMinute)
        {
            _inputReservations.Enqueue(new TokenReservation(now, estimatedInputTokens));
            _inputTokensInWindow += estimatedInputTokens;
        }

        if (_outputTokensPerMinute > 0 && estimatedOutputTokens > 0 && estimatedOutputTokens <= _outputTokensPerMinute)
        {
            _outputReservations.Enqueue(new TokenReservation(now, estimatedOutputTokens));
            _outputTokensInWindow += estimatedOutputTokens;
        }
    }

    private void TrimWindow(DateTimeOffset now)
    {
        DateTimeOffset threshold = now - Window;

        while (_requestTimes.Count > 0 && _requestTimes.Peek() <= threshold)
        {
            _requestTimes.Dequeue();
        }

        while (_inputReservations.Count > 0 && _inputReservations.Peek().Timestamp <= threshold)
        {
            TokenReservation reservation = _inputReservations.Dequeue();
            _inputTokensInWindow -= reservation.Tokens;
        }

        while (_outputReservations.Count > 0 && _outputReservations.Peek().Timestamp <= threshold)
        {
            TokenReservation reservation = _outputReservations.Dequeue();
            _outputTokensInWindow -= reservation.Tokens;
        }
    }

    private readonly struct TokenReservation
    {
        public TokenReservation(DateTimeOffset timestamp, int tokens)
        {
            Timestamp = timestamp;
            Tokens = tokens;
        }

        public DateTimeOffset Timestamp { get; }

        public int Tokens { get; }
    }
}
