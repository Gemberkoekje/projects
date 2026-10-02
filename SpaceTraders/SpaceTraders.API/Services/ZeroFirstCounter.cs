using Prometheus;

namespace SpaceTraders.API.Services;

/// <summary>
/// A Prometheus counter whose every series is scraped at 0 before it counts anything (B43).
/// </summary>
/// <remarks>
/// <para>
/// Prometheus's <c>increase()</c> and <c>rate()</c> count what a series gains between two scrapes,
/// never the value it has when Prometheus first sees it. prometheus-net creates a labelled series on
/// its first increment, so the series reached Prometheus with that increment in it: the contract's
/// deposit and the first drone, booked before the pod's first scrape, never showed on the ledger
/// panels, and a single 429 or circuit breaker trip could never show at all.
/// </para>
/// <para>
/// Here a new series is published at 0, and what it counts waits until a scrape has exported that 0:
/// it is added just before the scrape after that, at most two scrape intervals late. From then on the
/// series counts at once. Without a scrape (no metrics server) nothing is added, and nothing reads it.
/// </para>
/// <para>Thread-safe: increments and scrapes may come from any thread.</para>
/// </remarks>
internal sealed class ZeroFirstCounter
{
    private const char LabelSeparator = '\u001F';

    private readonly Counter _counter;
    private readonly Dictionary<string, Series> _series = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private long _scrapes;

    /// <summary>Wraps <paramref name="counter"/>, which <paramref name="registry"/> exports.</summary>
    /// <param name="counter">The counter, without any series of its own yet.</param>
    /// <param name="registry">The registry that exports it: its collections are the scrapes.</param>
    public ZeroFirstCounter(Counter counter, CollectorRegistry registry)
    {
        _counter = counter;

        // A counter without labels is its own series, published at 0 from the start.
        if (counter.LabelNames.Length == 0)
        {
            _series[string.Empty] = new Series([], publishedAt: 0);
        }

        registry.AddBeforeCollectCallback(BeforeScrape);
    }

    /// <summary>Adds <paramref name="amount"/> to the series with <paramref name="labels"/>.</summary>
    /// <param name="amount">What to add; not negative.</param>
    /// <param name="labels">The series' label values, in the counter's label order.</param>
    public void Inc(double amount, params string[] labels)
    {
        var key = string.Join(LabelSeparator, labels);
        lock (_lock)
        {
            if (!_series.TryGetValue(key, out var series))
            {
                // Published at 0 now; the next scrape exports that 0.
                _ = Child(labels);
                series = new Series(labels, publishedAt: _scrapes);
                _series[key] = series;
            }

            if (!series.Counting)
            {
                series.Waiting += amount;
                return;
            }
        }

        Child(labels).Inc(amount);
    }

    /// <summary>
    /// Before each scrape: a series published before the previous scrape started has been exported at
    /// 0 by it, so what it waits for goes into this scrape.
    /// </summary>
    private void BeforeScrape()
    {
        lock (_lock)
        {
            _scrapes++;
            foreach (var series in _series.Values.Where(series => !series.Counting && _scrapes >= series.PublishedAt + 2))
            {
                Child(series.Labels).Inc(series.Waiting);
                series.Waiting = 0;
                series.Counting = true;
            }
        }
    }

    private ICounter Child(string[] labels) => labels.Length == 0 ? _counter : _counter.WithLabels(labels);

    /// <summary>One series: when it was published at 0 (in scrapes seen), and what it waits to add.</summary>
    [Mutable]
    private sealed class Series(string[] labels, long publishedAt)
    {
        public string[] Labels { get; } = labels;

        public long PublishedAt { get; } = publishedAt;

        public double Waiting { get; set; }

        public bool Counting { get; set; }
    }
}
