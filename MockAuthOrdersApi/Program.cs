using System.Collections.Concurrent;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Swagger ayarları
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Mock Auth & Orders API",
        Version = "v1",
        Description = "Demo: /oauth/token ile token ver, /orders için Bearer zorunlu."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Sadece access_token giriniz."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Kolay test için sabit port kullandım. Gerçekte config'ten gelir.
builder.WebHost.UseUrls("http://localhost:5299");

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger"));

// === Demo için kullandığım sabitler ===
const string CLIENT_ID  = "sample-client";
const string CLIENT_SECRET = "sample-secret";
const int    TOKEN_TTL_SEC = 300;              // 5 dk
const int    MAX_TOKEN_REQ_PER_HOUR = 5;       // Saatte en fazla 5 token

// In-memory depolama 
var tokens = new ConcurrentDictionary<string, DateTimeOffset>();            // token -> expiry
var tokenCalls = new ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>>(); // client_id -> çağrı zamanları


app.MapGet("/oauth/token/quick", () =>
    {
        // Saatlik limit kontrolü
        var now = DateTimeOffset.UtcNow;
        var q = tokenCalls.GetOrAdd(CLIENT_ID, _ => new ConcurrentQueue<DateTimeOffset>());
        while (q.TryPeek(out var t) && t < now.AddHours(-1)) q.TryDequeue(out _);
        if (q.Count >= MAX_TOKEN_REQ_PER_HOUR)
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);

        q.Enqueue(now);

        var token = Guid.NewGuid().ToString("N");
        tokens[token] = now.AddSeconds(TOKEN_TTL_SEC);

        return Results.Ok(new {
            token_type = "Bearer",
            expires_in = TOKEN_TTL_SEC,
            access_token = token
        });
    })
    .WithTags("Auth")
    .WithOpenApi(op =>
    {
        op.Summary = "Hızlı token üret (demo)";
        op.Description = "Form göndermeden tek tıkla demo token döner.";
        return op;
    });

// === /oauth/token ===
app.MapPost("/oauth/token", async (HttpContext ctx, ILoggerFactory lf) =>
    {
        var log = lf.CreateLogger("Token");

        try
        {
            if (!ctx.Request.HasFormContentType)
                return Results.BadRequest(new { error = "unsupported_content_type" });

            var form = await ctx.Request.ReadFormAsync();
            var grantType = form["grant_type"].ToString();
            var clientId  = form["client_id"].ToString();
            var clientSec = form["client_secret"].ToString();

            if (!string.Equals(grantType, "client_credentials", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "unsupported_grant_type" });

            if (clientId != CLIENT_ID || clientSec != CLIENT_SECRET)
                return Results.Unauthorized();

            // Saatlik 5 çağrı limiti kontrolü yapıyorum
            var now = DateTimeOffset.UtcNow;
            var q = tokenCalls.GetOrAdd(clientId, _ => new ConcurrentQueue<DateTimeOffset>());
            while (q.TryPeek(out var t) && t < now.AddHours(-1))
                q.TryDequeue(out _);
            if (q.Count >= MAX_TOKEN_REQ_PER_HOUR)
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            q.Enqueue(now);

            var token = Guid.NewGuid().ToString("N");
            tokens[token] = now.AddSeconds(TOKEN_TTL_SEC);

            return Results.Ok(new
            {
                token_type = "Bearer",
                expires_in = TOKEN_TTL_SEC,
                access_token = token
            });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Token endpoint exception");
            return Results.Problem("internal_error");
        }
    })
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status429TooManyRequests)
    .WithTags("Auth");


// === /orders ===
app.MapGet("/orders", (HttpRequest req) =>
{
    if (!req.Headers.TryGetValue("Authorization", out var auth))
        return Results.Unauthorized();

    var parts = auth.ToString().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2 || !parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();

    var token = parts[1];
    if (!tokens.TryGetValue(token, out var exp) || DateTimeOffset.UtcNow >= exp)
        return Results.Unauthorized();

    var orders = new[]
    {
        new { id = 101, code = "ORD-101", total = 123.45m, createdAt = DateTime.UtcNow.AddMinutes(-30) },
        new { id = 102, code = "ORD-102", total =  87.00m, createdAt = DateTime.UtcNow.AddMinutes(-10) }
    };

    return Results.Ok(orders);
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.WithTags("Orders")
.WithOpenApi(op =>
{
    op.Summary = "Sipariş listesini döner (Bearer zorunlu)";
    op.Description =
        "Önce /oauth/token ile token alınmalı; Sonrasında swagger’da **Authorize** ile token girilmeli; sonra bu endpoint çalıştırmalı.";
    return op;
});

app.Run();