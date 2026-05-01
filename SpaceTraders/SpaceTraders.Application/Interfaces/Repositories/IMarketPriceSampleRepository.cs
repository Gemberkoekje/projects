namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IMarketPriceSampleRepository
{
    Task AppendSamplesAsync(string waypointSymbol, string tradeGoodsJson, CancellationToken cancellationToken = default);
}
