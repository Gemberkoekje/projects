namespace Kluskat.application.Queries;

/// <summary>Marker for a query whose handler returns a <see cref="Result{T}"/> of <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TResult">What the query answers with.</typeparam>
public interface IQuery<TResult>
{
}
