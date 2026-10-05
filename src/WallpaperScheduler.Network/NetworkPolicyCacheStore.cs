using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperScheduler.Network;

public sealed class NetworkPolicyCacheStore
{
    private readonly string _root;
    private readonly string _policyPath;
    private readonly string _assetsPath;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public NetworkPolicyCacheStore(string root)
    {
        _root = root;
        _policyPath = Path.Combine(root, "policy-cache.json");
        _assetsPath = Path.Combine(root, "assets");
    }

    public async Task<NetworkPolicyEnvelope?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_policyPath)) return null;
        try
        {
            await using var stream = new FileStream(_policyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<NetworkPolicyEnvelope>(stream, _json, cancellationToken);
        }
        catch { return null; }
    }

    public async Task SaveAsync(NetworkPolicyEnvelope policy, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        var temp = _policyPath + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, policy, _json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temp, _policyPath, overwrite: true);
    }

    public string GetAssetPath(NetworkAssetDescriptor asset)
    {
        var extension = Path.GetExtension(asset.FileName);
        if (extension.Length > 10) extension = string.Empty;
        return Path.Combine(_assetsPath, asset.Id + extension.ToLowerInvariant());
    }

    public async Task<bool> HasVerifiedAssetAsync(NetworkAssetDescriptor asset, CancellationToken cancellationToken = default)
    {
        var path = GetAssetPath(asset);
        if (!File.Exists(path)) return false;
        var info = new FileInfo(path);
        if (info.Length != asset.Size) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        return string.Equals(hash, asset.Id, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> StoreAssetAsync(NetworkAssetDescriptor asset, Stream source, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_assetsPath);
        var destination = GetAssetPath(asset);
        var temp = destination + ".tmp";
        await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            await source.CopyToAsync(file, cancellationToken);

        var info = new FileInfo(temp);
        if (info.Length != asset.Size)
        {
            File.Delete(temp);
            throw new InvalidDataException($"Asset '{asset.FileName}' chegou com tamanho inesperado.");
        }

        await using (var verify = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(hash, asset.Id, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                throw new InvalidDataException($"Asset '{asset.FileName}' falhou na validação SHA-256.");
            }
        }

        File.Move(temp, destination, overwrite: true);
        return destination;
    }
}
