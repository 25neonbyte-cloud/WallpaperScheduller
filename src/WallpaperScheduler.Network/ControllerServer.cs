using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Network;

public sealed class ControllerServer(NetworkPolicyBuilder policyBuilder, IAppLogger logger) : IDisposable
{
    private readonly ConcurrentDictionary<Guid, AgentPresence> _agents = new();
    private WebApplication? _app;
    private string _sharedSecret = string.Empty;

    public bool IsRunning => _app is not null;

    public async Task StartAsync(NetworkSettings settings, CancellationToken cancellationToken = default)
    {
        if (_app is not null) return;
        _sharedSecret = settings.SharedSecret;

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{settings.ControllerPort}");
        var app = builder.Build();

        app.MapGet("/api/v1/health", () => Results.Json(new
        {
            product = "Wallpaper Scheduler",
            role = "controller",
            protocol = 1,
            nodeId = settings.NodeId,
            utc = DateTimeOffset.UtcNow
        }));

        app.MapGet("/api/v1/policy", async (HttpContext context) =>
        {
            if (!Authorize(context)) return Results.Unauthorized();
            var group = context.Request.Query["group"].ToString();
            var built = await policyBuilder.BuildAsync(group, context.RequestAborted);
            context.Response.Headers["X-WS-Revision"] = built.Envelope.Revision;
            return Results.Json(built.Envelope);
        });

        app.MapGet("/api/v1/assets/{id}", async (HttpContext context, string id) =>
        {
            if (!Authorize(context)) return Results.Unauthorized();
            var group = context.Request.Query["group"].ToString();
            var built = await policyBuilder.BuildAsync(group, context.RequestAborted);
            if (!built.AssetPaths.TryGetValue(id, out var path) || !File.Exists(path))
                return Results.NotFound();

            return Results.File(path, "application/octet-stream", enableRangeProcessing: false);
        });

        app.MapPost("/api/v1/heartbeat", async (HttpContext context) =>
        {
            if (!Authorize(context)) return Results.Unauthorized();
            var heartbeat = await context.Request.ReadFromJsonAsync<AgentHeartbeat>(cancellationToken: context.RequestAborted);
            if (heartbeat is null) return Results.BadRequest();

            _agents[heartbeat.NodeId] = new(
                heartbeat.NodeId,
                heartbeat.NodeName,
                heartbeat.Group,
                heartbeat.AppVersion,
                heartbeat.PolicyRevision,
                heartbeat.TimestampUtc,
                DateTimeOffset.UtcNow,
                context.Connection.RemoteIpAddress?.ToString());

            return Results.Json(new { ok = true, utc = DateTimeOffset.UtcNow });
        });

        app.MapGet("/api/v1/agents", (HttpContext context) =>
        {
            if (!Authorize(context)) return Results.Unauthorized();
            return Results.Json(_agents.Values.OrderBy(x => x.NodeName).ToArray());
        });

        await app.StartAsync(cancellationToken);
        _app = app;
        logger.Info($"Rede: Controller iniciado na porta {settings.ControllerPort} para o grupo '{settings.Group}'.");
    }

    private bool Authorize(HttpContext context)
    {
        var supplied = context.Request.Headers["X-WS-Key"].ToString();
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(_sharedSecret))
            return false;

        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(_sharedSecret);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public void Dispose()
    {
        if (_app is null) return;
        try { _app.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
        catch { }
        try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch { }
        _app = null;
    }
}
