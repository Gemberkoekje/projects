namespace Curator.Core.Events;

/// <summary>The patron greeted the curator and asked for something.</summary>
/// <param name="Greeting">The greeting.</param>
/// <param name="RequestText">The request.</param>
public sealed record RequestMade(string Greeting, string RequestText) : GameEvent;
