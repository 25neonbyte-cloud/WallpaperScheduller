using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Infrastructure;

public sealed class JsonConfigStore(string path) : IConfigStore
{
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            var initial = new AppConfig();
            await SaveAsync(initial, cancellationToken);
            return initial;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AppConfig>(stream, _options, cancellationToken)
               ?? new AppConfig();
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, config, _options, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}
