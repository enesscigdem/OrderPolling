namespace OrderPoller.Worker.Abstractions;

public interface ITokenProvider
{
    Task<(string type, string token)> GetTokenAsync(CancellationToken ct = default);
}