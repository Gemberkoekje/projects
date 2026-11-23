namespace Klaverjas.application.Commands;

/// <summary>Marker for a command whose handler returns a plain <see cref="Result"/>.</summary>
public interface ICommand
{
}

/// <summary>Marker for a command whose handler returns a <see cref="Result{T}"/> of <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TResult">What the command answers with.</typeparam>
public interface ICommand<TResult>
{
}
