using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.Infrastructure;

public sealed class JsonConfigStore(string path, IAppLogger? logger = null) : IConfigStore, IConfigTransferService
{
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private string BackupPath => path + ".bak";
    private string TempPath => path + ".tmp";

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            var initial = new AppConfig();
            await SaveAsync(initial, cancellationToken);
            logger?.Info("Configuração inicial criada.");
            return initial;
        }

        try
        {
            return await ReadConfigAsync(path, cancellationToken);
        }
        catch (Exception ex) when (IsRecoverableConfigException(ex))
        {
            logger?.Warning($"Falha ao ler config.json; tentativa de recuperação pelo backup. {ex.Message}");
            PreserveCorruptPrimary();

            if (File.Exists(BackupPath))
            {
                try
                {
                    var recovered = await ReadConfigAsync(BackupPath, cancellationToken);
                    await WritePrimaryWithoutBackupAsync(recovered, cancellationToken);
                    logger?.Warning("Configuração recuperada com sucesso a partir de config.json.bak.");
                    return recovered;
                }
                catch (Exception backupEx) when (IsRecoverableConfigException(backupEx))
                {
                    logger?.Error("O backup da configuração também está inválido.", backupEx);
                }
            }

            var fallback = new AppConfig();
            await WritePrimaryWithoutBackupAsync(fallback, cancellationToken);
            logger?.Warning("Nenhuma configuração válida pôde ser recuperada; um novo padrão foi criado.");
            return fallback;
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        ValidateConfig(config);
        Directory.CreateDirectory(GetConfigDirectory());

        try
        {
            await WriteConfigFileAsync(TempPath, config, cancellationToken);
            _ = await ReadConfigAsync(TempPath, cancellationToken);

            if (File.Exists(path))
                File.Copy(path, BackupPath, overwrite: true);

            File.Move(TempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(TempPath))
            {
                try { File.Delete(TempPath); }
                catch { }
            }
        }
    }

    public async Task ExportAsync(AppConfig config, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destino de exportação inválido.", nameof(destinationPath));

        ValidateConfig(config);
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await WriteConfigFileAsync(destinationPath, config, cancellationToken);
        logger?.Info($"Configuração exportada para '{destinationPath}'.");
    }

    public async Task<AppConfig> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Arquivo de configuração não encontrado.", sourcePath);

        var imported = await ReadConfigAsync(sourcePath, cancellationToken);
        logger?.Info($"Configuração importada de '{sourcePath}'.");
        return imported;
    }

    private async Task<AppConfig> ReadConfigAsync(string sourcePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, _options, cancellationToken)
                     ?? throw new InvalidDataException("O JSON não contém uma configuração válida.");
        ValidateConfig(config);
        return config;
    }

    private async Task WritePrimaryWithoutBackupAsync(AppConfig config, CancellationToken cancellationToken)
    {
        ValidateConfig(config);
        Directory.CreateDirectory(GetConfigDirectory());
        await WriteConfigFileAsync(TempPath, config, cancellationToken);
        File.Move(TempPath, path, overwrite: true);
    }

    private async Task WriteConfigFileAsync(string destinationPath, AppConfig config, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, config, _options, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void ValidateConfig(AppConfig config)
    {
        if (config.Version is < 1 or > 3)
            throw new InvalidDataException($"Versão de configuração não suportada: {config.Version}.");

        config.Scheduler ??= new SchedulerSettings();
        config.VisualComfort ??= new VisualComfortConfig();
        config.VisualComfort.SystemTheme ??= new VisualComfortModuleSettings();
        config.VisualComfort.ColorTone ??= new VisualComfortModuleSettings();
        config.VisualComfort.VisualRoutine ??= new VisualComfortModuleSettings();
        config.MonitorProfiles ??= [];
        config.Rules ??= [];

        if (config.Version < 3)
            config.Version = 3;

        foreach (var rule in config.Rules)
        {
            if (rule is null)
                throw new InvalidDataException("A configuração contém uma regra nula.");

            rule.DaysOfWeek ??= [];
            rule.PerMonitor ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            rule.PerMonitorProfiles ??= [];

            if (rule.Source is not null)
                rule.Source.Items ??= [];

            foreach (var source in rule.PerMonitorProfiles.Values)
            {
                if (source is null)
                    throw new InvalidDataException($"A regra '{rule.Name}' contém uma fonte de monitor inválida.");
                source.Items ??= [];
            }
        }
    }

    private string GetConfigDirectory() =>
        Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;

    private void PreserveCorruptPrimary()
    {
        try
        {
            if (!File.Exists(path)) return;
            var preserved = Path.Combine(
                GetConfigDirectory(),
                $"config.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.Copy(path, preserved, overwrite: false);
            logger?.Warning($"Configuração corrompida preservada em '{preserved}'.");
        }
        catch (Exception ex)
        {
            logger?.Error("Não foi possível preservar uma cópia da configuração corrompida.", ex);
        }
    }

    private static bool IsRecoverableConfigException(Exception ex) =>
        ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException;
}
