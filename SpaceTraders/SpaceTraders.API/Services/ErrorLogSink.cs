using System.Globalization;
using Serilog.Core;
using Serilog.Events;
using SpaceTraders.Application.Health;

namespace SpaceTraders.API.Services;

/// <summary>
/// Hands every warning and error the bot logs to <see cref="ErrorLog"/>, for the <c>RepeatingError</c>
/// health rule, by log statement: its source and message template. Journal lines (with an
/// <c>EventKind</c>) stay out: what they report, other rules watch.
/// </summary>
/// <remarks>Serilog calls it on the thread that logs; it never throws.</remarks>
public sealed class ErrorLogSink(ErrorLog errors) : ILogEventSink
{
    private const string EventKindProperty = "EventKind";
    private const string SourceContextProperty = "SourceContext";

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning || logEvent.Properties.ContainsKey(EventKindProperty))
        {
            return;
        }

        errors.Record(Statement(logEvent), logEvent.RenderMessage(CultureInfo.InvariantCulture), logEvent.Timestamp);
    }

    /// <summary>
    /// The statement a line came from: the class that logged it and its template, such as
    /// <c>GameLoopService: Error in GameLoopService tick.</c>. Most templates here start with their class
    /// already, which isn't repeated.
    /// </summary>
    internal static string Statement(LogEvent logEvent)
    {
        var template = logEvent.MessageTemplate.Text;
        if (!logEvent.Properties.TryGetValue(SourceContextProperty, out var value)
            || value is not ScalarValue { Value: string sourceContext })
        {
            return template;
        }

        var source = sourceContext[(sourceContext.LastIndexOf('.') + 1)..];
        return template.StartsWith(source, StringComparison.Ordinal) ? template : $"{source}: {template}";
    }
}
