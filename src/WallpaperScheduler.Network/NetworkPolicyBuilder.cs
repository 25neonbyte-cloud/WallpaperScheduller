using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Network;

public sealed class NetworkPolicyBuilder(IConfigStore configStore, IAppLogger logger)
{
    public const string AssetSchemePrefix = "network-asset://";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, CachedHash> _hashCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<BuiltNetworkPolicy> BuildAsync(string group, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var config = await configStore.LoadAsync(cancellationToken);
            var rules = Clone(config.Rules);
            var comfort = Clone(config.VisualComfort);
            var assets = new Dictionary<string, NetworkAssetDescriptor>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            comfort.Temperature.PerMonitorMethods.Clear();

            foreach (var rule in rules)
            {
                if (rule.Scope == WallpaperScope.PerMonitor)
                {
                    if (rule.Source is null || rule.Source.Items.Count == 0)
                    {
                        rule.Enabled = false;
                        logger.Warning($"Rede: regra '{rule.Name}' usa apenas fontes Por monitor. Ela foi desativada para agentes remotos; configure uma Fonte global para distribuição.");
                    }
                    rule.Scope = WallpaperScope.AllMonitors;
                    rule.PerMonitorProfiles.Clear();
                    rule.PerMonitor.Clear();
                }

                if (rule.Source is not null)
                    rule.Source = await MaterializeSourceAsync(rule.Source, assets, paths, cancellationToken);
            }

            var descriptorList = assets.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var revisionMaterial = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Rules = rules,
                VisualComfort = comfort,
                Assets = descriptorList
            }, _json);

            var revision = Convert.ToHexString(SHA256.HashData(revisionMaterial)).ToLowerInvariant();
            var envelope = new NetworkPolicyEnvelope
            {
                Revision = revision,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                ControllerNodeId = config.Network.NodeId,
                Group = NormalizeGroup(group),
                Rules = rules,
                VisualComfort = comfort,
                Assets = descriptorList
            };

            return new(envelope, paths);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WallpaperSource> MaterializeSourceAsync(
        WallpaperSource source,
        IDictionary<string, NetworkAssetDescriptor> assets,
        IDictionary<string, string> paths,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();

        foreach (var item in source.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Kind == WallpaperSourceKind.File)
            {
                if (WallpaperFileSupport.IsSupportedExistingFile(item.Path))
                    files.Add(Path.GetFullPath(item.Path));
                continue;
            }

            if (!Directory.Exists(item.Path)) continue;
            var option = source.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            try
            {
                files.AddRange(Directory.EnumerateFiles(item.Path, "*.*", option)
                    .Where(WallpaperFileSupport.IsSupportedExistingFile)
                    .Select(Path.GetFullPath));
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        var result = new WallpaperSource { IncludeSubfolders = false };
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(file);
            var id = await GetHashAsync(info, cancellationToken);
            var fileName = SanitizeFileName(info.Name);
            assets[id] = new(id, fileName, info.Length);
            paths[id] = info.FullName;
            result.Items.Add(new()
            {
                Kind = WallpaperSourceKind.File,
                Path = $"{AssetSchemePrefix}{id}/{Uri.EscapeDataString(fileName)}"
            });
        }

        return result;
    }

    private async Task<string> GetHashAsync(FileInfo info, CancellationToken cancellationToken)
    {
        if (_hashCache.TryGetValue(info.FullName, out var cached) &&
            cached.Length == info.Length &&
            cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
            return cached.Hash;

        await using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        _hashCache[info.FullName] = new(info.Length, info.LastWriteTimeUtc.Ticks, hash);
        return hash;
    }

    private T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, _json), _json)
        ?? throw new InvalidDataException("Falha ao clonar política para distribuição em rede.");

    private static string NormalizeGroup(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "wallpaper.bin" : value;
    }

    private sealed record CachedHash(long Length, long LastWriteUtcTicks, string Hash);
}
