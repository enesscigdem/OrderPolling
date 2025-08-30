using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderPoller.Worker.Abstractions;
using OrderPoller.Worker.Models.Options;
using OrderPoller.Worker.Services;
using System.Net.Http.Headers;

// Benim yazdığım Worker: 5 dakikada bir siparişleri çeker, token'ı önbellekte tutar ve süresi bitmeden yeniler.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Api"));
builder.Services.Configure<PollingOptions>(builder.Configuration.GetSection("Polling"));

builder.Services.AddHttpClient<IOrdersClient, OrdersClient>((sp, http) =>
{
    var api = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
    http.BaseAddress = new Uri(api.BaseUrl);
    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddSingleton<ITokenProvider, TokenProvider>();
builder.Services.AddHostedService<OrdersPoller>();

var app = builder.Build();
await app.RunAsync();