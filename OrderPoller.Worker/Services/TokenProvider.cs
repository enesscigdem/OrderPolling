using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderPoller.Worker.Abstractions;
using OrderPoller.Worker.Models.Options;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OrderPoller.Worker.Services;

public sealed class TokenProvider : ITokenProvider
{
    private readonly HttpClient _http;
    private readonly AuthOptions _auth;
    private readonly PollingOptions _poll;
    private readonly ILogger<TokenProvider> _log;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentQueue<DateTimeOffset> _calls = new(); // saatlik limit takibi

    private string? _type, _token;
    private DateTimeOffset _exp = DateTimeOffset.MinValue;

    public TokenProvider(IHttpClientFactory factory, IOptions<AuthOptions> ao, IOptions<PollingOptions> po, ILogger<TokenProvider> log)
    {
        _http = factory.CreateClient();
        _auth = ao.Value;
        _poll = po.Value;
        _log  = log;
    }

    public async Task<(string type, string token)> GetTokenAsync(CancellationToken ct = default)
    {
        if (Valid()) return (_type!, _token!);

        await _gate.WaitAsync(ct);
        try
        {
            if (Valid()) return (_type!, _token!);

            PruneOldCalls();

            if (_calls.Count >= _poll.MaxTokenRequestsPerHour)
                throw new InvalidOperationException("Saatlik token limiti aşıldı.");

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _auth.ClientId,
                ["client_secret"] = _auth.ClientSecret
            };
            if (!string.IsNullOrWhiteSpace(_auth.Scope))
                form["scope"] = _auth.Scope!;

            using var resp = await _http.PostAsync(_auth.TokenUrl, new FormUrlEncodedContent(form), ct);
            if ((int)resp.StatusCode == 429)
                throw new InvalidOperationException("Yetkili sunucu 429 döndü (token oran sınırı).");

            resp.EnsureSuccessStatusCode();

            var tr = await resp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                     ?? throw new InvalidOperationException("Token cevabı okunamadı.");

            _type = tr.TokenType;
            _token = tr.AccessToken;

            // Ben token'ı süresi bitmeden biraz önce yeniliyorum.
            var refreshAt = Math.Max(1, tr.ExpiresIn - _poll.TokenRefreshSkewSeconds);
            _exp = DateTimeOffset.UtcNow.AddSeconds(refreshAt);

            _calls.Enqueue(DateTimeOffset.UtcNow);
            _log.LogInformation("Token alındı, {sec} sn önce yenilenecek.", _poll.TokenRefreshSkewSeconds);
            return (_type!, _token!);
        }
        finally
        {
            _gate.Release();
        }
    }

    bool Valid() => !string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow < _exp;

    void PruneOldCalls()
    {
        var border = DateTimeOffset.UtcNow.AddHours(-1);
        while (_calls.TryPeek(out var t) && t < border)
            _calls.TryDequeue(out _);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("access_token")] string AccessToken
    );
}
