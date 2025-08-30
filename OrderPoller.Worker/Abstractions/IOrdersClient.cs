namespace OrderPoller.Worker.Abstractions;

public interface IOrdersClient
{
    Task<string> GetOrdersAsync(CancellationToken ct = default);
}