using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Network;

public sealed class AgentSyncService(
    NetworkPolicyCacheStore cacheStore,
    NetworkClockState clockState,
    IAppLogger logger) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private NetworkSettings? _settings;

    public NetworkRuntimeStatus Status { get; private set; } =
        new(NetworkNodeRole.Agent, false, false, "Agente parado.");

    public void Start(NetworkSettings settings)
    {
        if (_loop is not null) return;
        _settings = settings;
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Status = new(NetworkNodeRole.Agent, true, false, "Aguardando Controller.");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Status = new(NetworkNodeRole.Agent, true, false, $"Controller indisponível: {ex.Message}", Status.PolicyRevision, Status.LastContactUtc);
                logger.Warning($"Rede: falha de sincronização do Agent. A última política em cache continuará ativa. {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_settings!.SyncIntervalSeconds, 3, 3600)), cancellationToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        var settings = _settings ?? throw new InvalidOperationException("Agente não configurado.");
        if (string.IsNullOrWhiteSpace(settings.ControllerUrl))
            throw new InvalidOperationException("Endereço do Controller não configurado.");
        if (string.IsNullOrWhiteSpace(settings.SharedSecret))
            throw new InvalidOperationException("Chave de pareamento não configurada.");

        var baseUrl = settings.ControllerUrl.TrimEnd('/');
        var cached = await cacheStore.LoadAsync(cancellationToken);
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        using (var heartbeatRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/v1/heartbeat"))
        {
            heartbeatRequest.Headers.TryAddWithoutValidation("X-WS-Key", settings.SharedSecret);
            heartbeatRequest.Content = JsonContent.Create(new AgentHeartbeat(
                settings.NodeId,
                settings.NodeName,
                settings.Group,
                version,
                cached?.Revision,
                DateTimeOffset.UtcNow), options: _json);
            using var heartbeatResponse = await _http.SendAsync(heartbeatRequest, cancellationToken);
            heartbeatResponse.EnsureSuccessStatusCode();
        }

        var policyUrl = baseUrl + "/api/v1/policy?group=" + Uri.EscapeDataString(settings.Group);
        using var policyRequest = new HttpRequestMessage(HttpMethod.Get, policyUrl);
        policyRequest.Headers.TryAddWithoutValidation("X-WS-Key", settings.SharedSecret);
        using var policyResponse = await _http.SendAsync(policyRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        policyResponse.EnsureSuccessStatusCode();

        var policy = await policyResponse.Content.ReadFromJsonAsync<NetworkPolicyEnvelope>(_json, cancellationToken)
                     ?? throw new InvalidDataException("Controller retornou uma política vazia.");
        if (policy.ProtocolVersion != 1)
            throw new InvalidDataException($"Protocolo de rede não suportado: {policy.ProtocolVersion}.");

        clockState.Update(policy.GeneratedAtUtc);

        if (cached is not null && string.Equals(cached.Revision, policy.Revision, StringComparison.OrdinalIgnoreCase))
        {
            Status = new(NetworkNodeRole.Agent, true, true, "Sincronizado.", policy.Revision, DateTimeOffset.UtcNow);
            return;
        }

        foreach (var asset in policy.Assets)
        {
            if (await cacheStore.HasVerifiedAssetAsync(asset, cancellationToken)) continue;

            var assetUrl = baseUrl + "/api/v1/assets/" + Uri.EscapeDataString(asset.Id) +
                           "?group=" + Uri.EscapeDataString(settings.Group);
            using var assetRequest = new HttpRequestMessage(HttpMethod.Get, assetUrl);
            assetRequest.Headers.TryAddWithoutValidation("X-WS-Key", settings.SharedSecret);
            using var response = await _http.SendAsync(assetRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await cacheStore.StoreAssetAsync(asset, stream, cancellationToken);
        }

        LocalizeAssets(policy);
        await cacheStore.SaveAsync(policy, cancellationToken);
        Status = new(NetworkNodeRole.Agent, true, true, "Política central aplicada ao cache local.", policy.Revision, DateTimeOffset.UtcNow);
        logger.Info($"Rede: nova política {policy.Revision[..Math.Min(12, policy.Revision.Length)]} recebida do Controller; {policy.Assets.Count} asset(s) reconciliado(s).");
    }

    private void LocalizeAssets(NetworkPolicyEnvelope policy)
    {
        var byId = policy.Assets.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in policy.Rules)
        {
            if (rule.Source is not null)
                LocalizeSource(rule.Source, byId);
            foreach (var source in rule.PerMonitorProfiles.Values)
                LocalizeSource(source, byId);
        }
    }

    private void LocalizeSource(WallpaperSource source, IReadOnlyDictionary<string, NetworkAssetDescriptor> assets)
    {
        foreach (var item in source.Items)
        {
            if (!item.Path.StartsWith(NetworkPolicyBuilder.AssetSchemePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = item.Path[NetworkPolicyBuilder.AssetSchemePrefix.Length..];
            var slash = rest.IndexOf('/');
            var id = slash >= 0 ? rest[..slash] : rest;
            if (assets.TryGetValue(id, out var asset))
                item.Path = cacheStore.GetAssetPath(asset);
        }
    }

    public void Dispose()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            try { _loop?.Wait(TimeSpan.FromSeconds(3)); } catch { }
            _cts.Dispose();
        }
        _http.Dispose();
    }
}
