using Microsoft.Extensions.Options;
using OrderPoller.Worker.Abstractions;
using OrderPoller.Worker.Models.Options;
using System.Net.Http.Headers;

namespace OrderPoller.Worker.Services;

public sealed class OrdersClient : IOrdersClient
{
    private readonly HttpClient _http;
    private readonly ApiOptions _api;
    private readonly ITokenProvider _tokens;

    public OrdersClient(HttpClient http, IOptions<ApiOptions> api, ITokenProvider tokens)
    {
        _http = http;
        _api = api.Value;
        _tokens = tokens;
    }

    public async Task<string> GetOrdersAsync(CancellationToken ct = default)
    {
        var (type, token) = await _tokens.GetTokenAsync(ct);

        using var req = new HttpRequestMessage(HttpMethod.Get, _api.OrdersPath);
        req.Headers.Authorization = new AuthenticationHeaderValue(type, token);

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}