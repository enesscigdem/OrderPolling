using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderPoller.Worker.Abstractions;
using OrderPoller.Worker.Models.Options;

namespace OrderPoller.Worker.Services;

// not: Bu servis sırayla çalışır, hata olsa bile bekler ve tekrar dener.
public sealed class OrdersPoller : BackgroundService
{
    private readonly ILogger<OrdersPoller> _log;
    private readonly IOrdersClient _client;
    private readonly PollingOptions _poll;

    public OrdersPoller(ILogger<OrdersPoller> log, IOrdersClient client, IOptions<PollingOptions> po)
    {
        _log = log;
        _client = client;
        _poll = po.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Order poller başladı. Periyot: {sec} sn", _poll.PeriodSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;

            try
            {
                var json = await _client.GetOrdersAsync(stoppingToken);
                _log.LogInformation("Siparişler çekildi. Karşıdan gelen uzunluk: {len}", json?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Sipariş çekerken hata oluştu");
            }

            var elapsed = DateTimeOffset.UtcNow - started;
            var wait = TimeSpan.FromSeconds(_poll.PeriodSeconds) - elapsed;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;

            await Task.Delay(wait, stoppingToken);
        }
    }
}