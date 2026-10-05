using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Network;

public sealed class NetworkAwareEffectiveConfigProvider(IConfigStore localStore, NetworkPolicyCacheStore cacheStore, IAppLogger logger) : IEffectiveConfigProvider
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private bool _loggedMissingCache;

    public async Task<AppConfig> LoadEffectiveAsync(CancellationToken cancellationToken = default)
    {
        var local = await localStore.LoadAsync(cancellationToken);
        if (local.Network.Role != NetworkNodeRole.Agent) return local;

        var policy = await cacheStore.LoadAsync(cancellationToken);
        if (policy is null || !string.Equals(policy.Group, local.Network.Group, StringComparison.OrdinalIgnoreCase))
        {
            if (!_loggedMissingCache)
            {
                logger.Warning("Rede: agente ainda não possui uma política central válida; configuração local permanece ativa até a primeira sincronização.");
                _loggedMissingCache = true;
            }
            return local;
        }

        _loggedMissingCache = false;
        var effective = Clone(local);
        effective.Rules = Clone(policy.Rules);

        var localMethods = new Dictionary<Guid, TemperatureApplicationMethod>(local.VisualComfort.Temperature.PerMonitorMethods);
        var forceSoftware = local.VisualComfort.Temperature.ForceSoftwareWhenExternalTransformDetected;
        effective.VisualComfort = Clone(policy.VisualComfort);
        effective.VisualComfort.Temperature.PerMonitorMethods = localMethods;
        effective.VisualComfort.Temperature.ForceSoftwareWhenExternalTransformDetected = forceSoftware;
        return effective;
    }

    private T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, _json), _json)
        ?? throw new InvalidDataException("Falha ao materializar configuração efetiva de rede.");
}
