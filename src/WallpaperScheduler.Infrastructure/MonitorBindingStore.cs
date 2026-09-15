using System.Text.Json;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.Infrastructure;

public sealed class JsonMonitorBindingStore(string path) : IMonitorBindingStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<MonitorBinding>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<MonitorBinding>>(stream, Options, cancellationToken) ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<MonitorBinding> bindings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, bindings, Options, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}
