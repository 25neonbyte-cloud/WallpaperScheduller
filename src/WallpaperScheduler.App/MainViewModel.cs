using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly WallpaperOrchestrator _orchestrator;
    private readonly IMonitorService _monitorService;
    private readonly IMonitorProfileResolver _monitorProfileResolver;
    private readonly IMonitorBindingStore _monitorBindingStore;
    private readonly IConfigStore _configStore;
    private readonly IConfigTransferService _configTransferService;
    private readonly IAppLogger _logger;
    private AppConfig _config = new();
    private string _status = "Carregando configuração...";
    private string _monitorDiagnostics = "Detectando monitores...";
    private bool _busy;

    public MainViewModel(
        WallpaperOrchestrator orchestrator,
        IMonitorService monitorService,
        IMonitorProfileResolver monitorProfileResolver,
        IMonitorBindingStore monitorBindingStore,
        IConfigStore configStore,
        IConfigTransferService configTransferService,
        IAppLogger logger)
    {
        _orchestrator = orchestrator;
        _monitorService = monitorService;
        _monitorProfileResolver = monitorProfileResolver;
        _monitorBindingStore = monitorBindingStore;
        _configStore = configStore;
        _configTransferService = configTransferService;
        _logger = logger;

        ApplyNowCommand = new AsyncCommand(ApplyNowAsync, () => !Busy);
        RefreshMonitorsCommand = new AsyncCommand(RefreshMonitorsAsync, () => !Busy);
        SaveCommand = new AsyncCommand(SaveAsync, () => !Busy);
        AddPeriodCommand = new RelayCommand(AddPeriod, () => !Busy);
        RemovePeriodCommand = new RelayCommand<RuleEditorItem>(RemovePeriod, item => !Busy && item is not null);

        _ = LoadAsync();
    }

    public ObservableCollection<RuleEditorItem> Periods { get; } = [];
    public ObservableCollection<MonitorProfileEditorItem> MonitorProfiles { get; } = [];
    public Array RotationModes { get; } = Enum.GetValues<WallpaperRotationMode>();
    public Array WallpaperStyles { get; } = Enum.GetValues<WallpaperStyle>();
    public Array WallpaperScopes { get; } = Enum.GetValues<WallpaperScope>();

    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public string MonitorDiagnostics { get => _monitorDiagnostics; private set { _monitorDiagnostics = value; OnPropertyChanged(); } }

    public bool Busy
    {
        get => _busy;
        private set { _busy = value; OnPropertyChanged(); RaiseCommandStates(); }
    }

    public ICommand ApplyNowCommand { get; }
    public ICommand RefreshMonitorsCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddPeriodCommand { get; }
    public ICommand RemovePeriodCommand { get; }

    public void AddDroppedSources(RuleEditorItem item, IEnumerable<string> paths)
    {
        item.Scope = WallpaperScope.AllMonitors;
        AddSources(item.Sources, paths, item.Name);
    }

    public void AddDroppedMonitorSources(MonitorSourceEditorItem item, IEnumerable<string> paths)
    {
        var owner = Periods.FirstOrDefault(x => x.MonitorSources.Contains(item));
        if (owner is not null)
            owner.Scope = WallpaperScope.PerMonitor;
        AddSources(item.Sources, paths, item.ProfileName);
    }

    private void AddSources(ObservableCollection<SourceEditorItem> target, IEnumerable<string> paths, string label)
    {
        var added = 0;
        var rejected = 0;

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            WallpaperSourceKind? kind = null;
            if (Directory.Exists(path))
                kind = WallpaperSourceKind.Folder;
            else if (File.Exists(path))
            {
                if (!WallpaperFileSupport.IsSupportedExtension(path))
                {
                    rejected++;
                    continue;
                }
                kind = WallpaperSourceKind.File;
            }

            if (kind is null || target.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            target.Add(new SourceEditorItem(kind.Value, path));
            added++;
        }

        if (added > 0)
            Status = rejected > 0
                ? $"{added} fonte(s) adicionada(s) a '{label}'; {rejected} arquivo(s) ignorado(s). Formatos: {WallpaperFileSupport.DisplayNames}."
                : $"{added} fonte(s) adicionada(s) a '{label}'. Salve para persistir.";
        else if (rejected > 0)
            Status = $"Arquivos ignorados: formatos suportados são {WallpaperFileSupport.DisplayNames}.";
        else
            Status = "Nenhuma fonte válida foi adicionada.";
    }

    public void RemoveSource(RuleEditorItem item, SourceEditorItem source) { item.Sources.Remove(source); Status = "Fonte removida. Salve para persistir."; }
    public void RemoveMonitorSource(MonitorSourceEditorItem item, SourceEditorItem source) { item.Sources.Remove(source); Status = "Fonte do monitor removida. Salve para persistir."; }

    public async Task ForgetMonitorAsync(MonitorProfileEditorItem item)
    {
        if (Busy) return;
        try
        {
            Busy = true;
            _config.MonitorProfiles.RemoveAll(x => x.Id == item.Id);
            foreach (var rule in _config.Rules)
                rule.PerMonitorProfiles.Remove(item.Id);
            foreach (var editor in Periods)
                editor.RemoveMonitorProfile(item.Id);

            await _monitorBindingStore.RemoveAsync(item.Id);
            await _configStore.SaveAsync(_config);
            MonitorProfiles.Remove(item);
            MonitorDiagnostics = BuildMonitorDiagnostics();
            Status = $"Perfil '{item.Name}' esquecido neste computador.";
            _logger.Info($"Perfil lógico de monitor removido: {item.Name}.");
        }
        catch (Exception ex)
        {
            Status = $"Erro ao esquecer monitor: {ex.Message}";
            _logger.Error("Erro ao esquecer perfil de monitor.", ex);
        }
        finally { Busy = false; }
    }

    public async Task ExportConfigAsync(string filePath)
    {
        if (Busy) return;
        try
        {
            Busy = true;
            await SaveChangesCoreAsync();
            await _configTransferService.ExportAsync(_config, filePath);
            Status = $"Configuração exportada para '{filePath}'.";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao exportar: {ex.Message}";
            _logger.Error("Erro ao exportar configuração.", ex);
        }
        finally { Busy = false; }
    }

    public async Task ImportConfigAsync(string filePath)
    {
        if (Busy) return;
        try
        {
            Busy = true;
            var keepStartupPreference = _config.Scheduler.StartWithWindows;
            var imported = await _configTransferService.ImportAsync(filePath);
            imported.Scheduler.StartWithWindows = keepStartupPreference;

            _config = imported;
            await _configStore.SaveAsync(_config);
            await ReconcileMonitorsAsync();
            LoadEditors();
            Status = $"Configuração importada. {Periods.Count} período(s) carregado(s).";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao importar: {ex.Message}";
            _logger.Error("Erro ao importar configuração.", ex);
        }
        finally { Busy = false; }
    }

    public void OpenDiagnosticsFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(_logger.LogFilePath) ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            Status = $"Pasta de diagnóstico: {folder}";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao abrir diagnósticos: {ex.Message}";
            _logger.Error("Erro ao abrir pasta de diagnósticos.", ex);
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            Busy = true;
            _config = await _configStore.LoadAsync();
            await ReconcileMonitorsAsync();
            LoadEditors();
            Status = Periods.Count == 0 ? "Nenhum período configurado. Use '+ Novo período'." : $"{Periods.Count} período(s) carregado(s).";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao carregar configuração: {ex.Message}";
            _logger.Error("Erro ao carregar configuração na interface.", ex);
        }
        finally { Busy = false; }
    }

    private IReadOnlyList<MonitorProfile> ActiveProfiles() =>
        _config.MonitorProfiles.Where(p => MonitorProfiles.Any(m => m.Id == p.Id)).ToList();

    private void LoadEditors()
    {
        Periods.Clear();
        var activeProfiles = ActiveProfiles();
        foreach (var rule in _config.Rules.OrderBy(r => r.Order))
            Periods.Add(RuleEditorItem.FromRule(rule, activeProfiles));
    }

    private async Task ReconcileMonitorsAsync()
    {
        var monitors = _monitorService.GetActiveMonitors();
        var resolutions = await _monitorProfileResolver.ResolveAsync(_config, monitors);

        MonitorProfiles.Clear();
        foreach (var resolution in resolutions.Where(x => x.Monitor is not null))
        {
            var profile = _config.MonitorProfiles.First(x => x.Id == resolution.ProfileId);
            MonitorProfiles.Add(new(profile.Id, profile.Name, resolution.Status));
        }

        MonitorDiagnostics = BuildMonitorDiagnostics();
    }

    private string BuildMonitorDiagnostics()
    {
        var text = new StringBuilder($"Monitores ativos: {MonitorProfiles.Count}");
        foreach (var item in MonitorProfiles)
            text.Append($"  •  {item.Name}");
        return text.ToString();
    }

    private async Task RefreshMonitorsAsync()
    {
        try
        {
            Busy = true;
            await ReconcileMonitorsAsync();
            SyncMonitorSourcesAcrossRules();
            Status = "Monitores reconciliados.";
        }
        catch (Exception ex)
        {
            MonitorDiagnostics = $"Erro ao detectar monitores: {ex.Message}";
            _logger.Error("Erro ao reconciliar monitores pela interface.", ex);
        }
        finally { Busy = false; }
    }

    private void SyncMonitorSourcesAcrossRules()
    {
        var active = ActiveProfiles();
        foreach (var rule in Periods)
            rule.SetActiveMonitorProfiles(active);
    }

    private async Task SaveAsync()
    {
        try
        {
            Busy = true;
            await SaveChangesCoreAsync();
            Status = "Configuração salva.";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao salvar: {ex.Message}";
            _logger.Error("Erro ao salvar configuração pela interface.", ex);
        }
        finally { Busy = false; }
    }

    private async Task SaveChangesCoreAsync()
    {
        var rules = new List<WallpaperRule>();
        for (var i = 0; i < Periods.Count; i++)
        {
            var editor = Periods[i];
            if (!TimeOnly.TryParse(editor.StartText, out var start)) throw new InvalidOperationException($"Horário inicial inválido em '{editor.Name}'. Use HH:mm.");
            if (!TimeOnly.TryParse(editor.EndText, out var end)) throw new InvalidOperationException($"Horário final inválido em '{editor.Name}'. Use HH:mm.");
            if (start == end) throw new InvalidOperationException($"'{editor.Name}' não pode iniciar e terminar no mesmo horário.");
            rules.Add(editor.ToRule(start, end, i * 10));
        }

        foreach (var profileEditor in MonitorProfiles)
        {
            var profile = _config.MonitorProfiles.First(x => x.Id == profileEditor.Id);
            profile.Name = string.IsNullOrWhiteSpace(profileEditor.Name) ? "Monitor" : profileEditor.Name.Trim();
        }

        _config.Version = 2;
        _config.Rules = rules;
        await _configStore.SaveAsync(_config);
    }

    private void AddPeriod()
    {
        var item = new RuleEditorItem
        {
            Id = Guid.NewGuid(), Name = "Novo período", StartText = "12:00", EndText = "13:00", Enabled = true,
            Priority = 100, RotationMode = WallpaperRotationMode.Sequential, Style = WallpaperStyle.Fill,
            Scope = WallpaperScope.AllMonitors, DaysOfWeek = new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>())
        };
        item.SetActiveMonitorProfiles(ActiveProfiles());
        Periods.Add(item);
        Status = "Novo período criado. Ajuste e salve.";
    }

    private void RemovePeriod(RuleEditorItem? item)
    {
        if (item is null) return;
        Periods.Remove(item);
        Status = $"Período '{item.Name}' removido. Salve para persistir.";
    }

    private async Task ApplyNowAsync()
    {
        try
        {
            Busy = true;
            await SaveChangesCoreAsync();
            await ReconcileMonitorsAsync();
            var result = await _orchestrator.ReapplyCurrentAsync();
            Status = result.Applied ? $"Aplicado: {result.Message}" : result.Message;
        }
        catch (Exception ex)
        {
            Status = $"Erro: {ex.Message}";
            _logger.Error("Erro em Aplicar agora.", ex);
        }
        finally { Busy = false; }
    }

    private void RaiseCommandStates()
    {
        (ApplyNowCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (SaveCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (RefreshMonitorsCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (AddPeriodCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemovePeriodCommand as RelayCommand<RuleEditorItem>)?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class RuleEditorItem : INotifyPropertyChanged
{
    private readonly Dictionary<Guid, WallpaperSource> _retainedMonitorSources = [];
    private string _name = "Período";
    private string _startText = "00:00";
    private string _endText = "01:00";
    private bool _enabled = true;
    private int _priority = 100;
    private int? _rotationIntervalMinutes;
    private WallpaperRotationMode _rotationMode;
    private WallpaperStyle _style = WallpaperStyle.Fill;
    private WallpaperScope _scope = WallpaperScope.AllMonitors;
    private bool _includeSubfolders;

    public Guid Id { get; set; }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string StartText { get => _startText; set { _startText = value; OnPropertyChanged(); } }
    public string EndText { get => _endText; set { _endText = value; OnPropertyChanged(); } }
    public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
    public int Priority { get => _priority; set { _priority = value; OnPropertyChanged(); } }
    public int? RotationIntervalMinutes { get => _rotationIntervalMinutes; set { _rotationIntervalMinutes = value; OnPropertyChanged(); } }
    public WallpaperRotationMode RotationMode { get => _rotationMode; set { _rotationMode = value; OnPropertyChanged(); } }
    public WallpaperStyle Style { get => _style; set { _style = value; OnPropertyChanged(); } }
    public WallpaperScope Scope
    {
        get => _scope;
        set
        {
            if (_scope == value) return;
            _scope = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAllMonitors));
            OnPropertyChanged(nameof(IsPerMonitor));
        }
    }
    public bool IsAllMonitors => Scope == WallpaperScope.AllMonitors;
    public bool IsPerMonitor => Scope == WallpaperScope.PerMonitor;
    public bool IncludeSubfolders { get => _includeSubfolders; set { _includeSubfolders = value; OnPropertyChanged(); } }
    public HashSet<DayOfWeek> DaysOfWeek { get; set; } = [];
    public ObservableCollection<SourceEditorItem> Sources { get; } = [];
    public ObservableCollection<MonitorSourceEditorItem> MonitorSources { get; } = [];

    public static RuleEditorItem FromRule(WallpaperRule rule, IReadOnlyCollection<MonitorProfile> activeProfiles)
    {
        var item = new RuleEditorItem
        {
            Id = rule.Id, Name = rule.Name, StartText = rule.Start.ToString("HH:mm"), EndText = rule.End.ToString("HH:mm"),
            Enabled = rule.Enabled, Priority = rule.Priority, RotationIntervalMinutes = rule.RotationIntervalMinutes,
            RotationMode = rule.RotationMode, Style = rule.Style, Scope = rule.Scope,
            DaysOfWeek = new HashSet<DayOfWeek>(rule.DaysOfWeek), IncludeSubfolders = rule.Source?.IncludeSubfolders ?? false
        };

        if (rule.Source is not null)
            foreach (var source in rule.Source.Items) item.Sources.Add(new(source.Kind, source.Path));
        if (!string.IsNullOrWhiteSpace(rule.Image) && item.Sources.Count == 0)
            item.Sources.Add(new(WallpaperSourceKind.File, rule.Image));

        foreach (var pair in rule.PerMonitorProfiles)
            item._retainedMonitorSources[pair.Key] = Clone(pair.Value);
        item.SetActiveMonitorProfiles(activeProfiles);
        return item;
    }

    public void SetActiveMonitorProfiles(IReadOnlyCollection<MonitorProfile> activeProfiles)
    {
        foreach (var current in MonitorSources)
            _retainedMonitorSources[current.ProfileId] = current.ToSource();

        MonitorSources.Clear();
        foreach (var profile in activeProfiles)
        {
            var editor = new MonitorSourceEditorItem(profile.Id, profile.Name);
            if (_retainedMonitorSources.TryGetValue(profile.Id, out var source)) editor.Load(source);
            MonitorSources.Add(editor);
        }
    }

    public void RemoveMonitorProfile(Guid profileId)
    {
        _retainedMonitorSources.Remove(profileId);
        var item = MonitorSources.FirstOrDefault(x => x.ProfileId == profileId);
        if (item is not null) MonitorSources.Remove(item);
    }

    public WallpaperRule ToRule(TimeOnly start, TimeOnly end, int order)
    {
        foreach (var current in MonitorSources)
            _retainedMonitorSources[current.ProfileId] = current.ToSource();

        return new WallpaperRule
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
            Name = string.IsNullOrWhiteSpace(Name) ? "Período" : Name.Trim(), Enabled = Enabled, Priority = Priority, Order = order,
            DaysOfWeek = DaysOfWeek.Count == 0 ? new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>()) : new HashSet<DayOfWeek>(DaysOfWeek),
            Start = start, End = end, Style = Style, Scope = Scope, RotationMode = RotationMode,
            RotationIntervalMinutes = RotationIntervalMinutes is > 0 ? RotationIntervalMinutes : null,
            Source = new WallpaperSource { IncludeSubfolders = IncludeSubfolders, Items = Sources.Select(x => new WallpaperSourceItem { Kind = x.Kind, Path = x.Path }).ToList() },
            PerMonitorProfiles = _retainedMonitorSources.ToDictionary(x => x.Key, x => Clone(x.Value))
        };
    }

    private static WallpaperSource Clone(WallpaperSource source) => new()
    {
        IncludeSubfolders = source.IncludeSubfolders,
        Items = source.Items.Select(x => new WallpaperSourceItem { Kind = x.Kind, Path = x.Path }).ToList()
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class MonitorSourceEditorItem : INotifyPropertyChanged
{
    private string _profileName;
    private bool _includeSubfolders;
    public MonitorSourceEditorItem(Guid profileId, string profileName) { ProfileId = profileId; _profileName = profileName; }
    public Guid ProfileId { get; }
    public string ProfileName { get => _profileName; set { _profileName = value; OnPropertyChanged(); } }
    public bool IncludeSubfolders { get => _includeSubfolders; set { _includeSubfolders = value; OnPropertyChanged(); } }
    public ObservableCollection<SourceEditorItem> Sources { get; } = [];
    public void Load(WallpaperSource source) { IncludeSubfolders = source.IncludeSubfolders; Sources.Clear(); foreach (var item in source.Items) Sources.Add(new(item.Kind, item.Path)); }
    public WallpaperSource ToSource() => new() { IncludeSubfolders = IncludeSubfolders, Items = Sources.Select(x => new WallpaperSourceItem { Kind = x.Kind, Path = x.Path }).ToList() };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class MonitorProfileEditorItem : INotifyPropertyChanged
{
    private string _name;
    public MonitorProfileEditorItem(Guid id, string name, string status) { Id = id; _name = name; Status = status; }
    public Guid Id { get; }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Status { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record SourceEditorItem(WallpaperSourceKind Kind, string Path)
{
    public string Display => $"{(Kind == WallpaperSourceKind.Folder ? "Pasta" : "Arquivo")}: {Path}";
}

internal sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    private bool _executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && canExecute();
    public async void Execute(object? parameter) { if (_executing) return; try { _executing = true; RaiseCanExecuteChanged(); await execute(); } finally { _executing = false; RaiseCanExecuteChanged(); } }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class RelayCommand<T>(Action<T?> execute, Func<T?, bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute(parameter is T value ? value : default);
    public void Execute(object? parameter) => execute(parameter is T value ? value : default);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
