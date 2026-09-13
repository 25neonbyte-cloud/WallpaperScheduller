using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly IConfigStore _configStore;
    private AppConfig _config = new();
    private string _status = "Carregando configuração...";
    private string _monitorDiagnostics = "Detectando monitores...";
    private bool _busy;

    public MainViewModel(
        WallpaperOrchestrator orchestrator,
        IMonitorService monitorService,
        IConfigStore configStore)
    {
        _orchestrator = orchestrator;
        _monitorService = monitorService;
        _configStore = configStore;

        ApplyNowCommand = new AsyncCommand(ApplyNowAsync, () => !Busy);
        RefreshMonitorsCommand = new RelayCommand(RefreshMonitors, () => !Busy);
        SaveCommand = new AsyncCommand(SaveAsync, () => !Busy);
        AddPeriodCommand = new RelayCommand(AddPeriod, () => !Busy);
        RemovePeriodCommand = new RelayCommand<RuleEditorItem>(RemovePeriod, item => !Busy && item is not null);

        RefreshMonitors();
        _ = LoadAsync();
    }

    public ObservableCollection<RuleEditorItem> Periods { get; } = [];
    public Array RotationModes { get; } = Enum.GetValues<WallpaperRotationMode>();
    public Array WallpaperStyles { get; } = Enum.GetValues<WallpaperStyle>();

    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public string MonitorDiagnostics { get => _monitorDiagnostics; private set { _monitorDiagnostics = value; OnPropertyChanged(); } }

    public bool Busy
    {
        get => _busy;
        private set
        {
            _busy = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public ICommand ApplyNowCommand { get; }
    public ICommand RefreshMonitorsCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddPeriodCommand { get; }
    public ICommand RemovePeriodCommand { get; }

    public void AddDroppedSources(RuleEditorItem item, IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var kind = Directory.Exists(path)
                ? WallpaperSourceKind.Folder
                : File.Exists(path)
                    ? WallpaperSourceKind.File
                    : (WallpaperSourceKind?)null;

            if (kind is null) continue;
            if (item.Sources.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) continue;

            item.Sources.Add(new SourceEditorItem(kind.Value, path));
            added++;
        }

        Status = added > 0
            ? $"{added} fonte(s) adicionada(s) a '{item.Name}'. Salve para persistir."
            : "Nenhuma fonte válida foi adicionada.";
    }

    public void RemoveSource(RuleEditorItem item, SourceEditorItem source)
    {
        item.Sources.Remove(source);
        Status = "Fonte removida. Salve para persistir.";
    }

    private async Task LoadAsync()
    {
        try
        {
            Busy = true;
            _config = await _configStore.LoadAsync();
            Periods.Clear();

            foreach (var rule in _config.Rules.OrderBy(r => r.Order))
                Periods.Add(RuleEditorItem.FromRule(rule));

            Status = Periods.Count == 0
                ? "Nenhum período configurado. Use '+ Novo período'."
                : $"{Periods.Count} período(s) carregado(s).";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao carregar configuração: {ex.Message}";
        }
        finally { Busy = false; }
    }

    private async Task SaveAsync()
    {
        try
        {
            Busy = true;
            var rules = new List<WallpaperRule>();

            for (var i = 0; i < Periods.Count; i++)
            {
                var editor = Periods[i];
                if (!TimeOnly.TryParse(editor.StartText, out var start))
                    throw new InvalidOperationException($"Horário inicial inválido em '{editor.Name}'. Use HH:mm.");
                if (!TimeOnly.TryParse(editor.EndText, out var end))
                    throw new InvalidOperationException($"Horário final inválido em '{editor.Name}'. Use HH:mm.");
                if (start == end)
                    throw new InvalidOperationException($"'{editor.Name}' não pode iniciar e terminar no mesmo horário.");

                rules.Add(editor.ToRule(start, end, i * 10));
            }

            _config.Version = 2;
            _config.Rules = rules;
            await _configStore.SaveAsync(_config);
            Status = "Configuração salva.";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao salvar: {ex.Message}";
        }
        finally { Busy = false; }
    }

    private void AddPeriod()
    {
        var item = new RuleEditorItem
        {
            Id = Guid.NewGuid(),
            Name = "Novo período",
            StartText = "12:00",
            EndText = "13:00",
            Enabled = true,
            Priority = 100,
            RotationMode = WallpaperRotationMode.Sequential,
            Style = WallpaperStyle.Fill,
            DaysOfWeek = new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>())
        };
        Periods.Add(item);
        Status = "Novo período criado. Ajuste e salve.";
    }

    private void RemovePeriod(RuleEditorItem? item)
    {
        if (item is null) return;
        Periods.Remove(item);
        Status = $"Período '{item.Name}' removido. Salve para persistir.";
    }

    private void RefreshMonitors()
    {
        try
        {
            var monitors = _monitorService.GetActiveMonitors();
            if (monitors.Count == 0)
            {
                MonitorDiagnostics = "Nenhum monitor ativo detectado.";
                return;
            }

            var text = new StringBuilder();
            text.Append($"Monitores ativos: {monitors.Count}");
            foreach (var monitor in monitors)
                text.Append($"  •  {monitor.Width}×{monitor.Height}");
            MonitorDiagnostics = text.ToString();
        }
        catch (Exception ex)
        {
            MonitorDiagnostics = $"Erro ao detectar monitores: {ex.Message}";
        }
    }

    private async Task ApplyNowAsync()
    {
        try
        {
            Busy = true;
            await SaveAsync();
            if (Status.StartsWith("Erro", StringComparison.OrdinalIgnoreCase)) return;

            RefreshMonitors();
            var result = await _orchestrator.ApplyCurrentAsync();
            Status = result.Applied ? $"Aplicado: {result.Message}" : result.Message;
        }
        catch (Exception ex) { Status = $"Erro: {ex.Message}"; }
        finally { Busy = false; }
    }

    private void RaiseCommandStates()
    {
        (ApplyNowCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (SaveCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (RefreshMonitorsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddPeriodCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemovePeriodCommand as RelayCommand<RuleEditorItem>)?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class RuleEditorItem : INotifyPropertyChanged
{
    private string _name = "Período";
    private string _startText = "00:00";
    private string _endText = "01:00";
    private bool _enabled = true;
    private int _priority = 100;
    private int? _rotationIntervalMinutes;
    private WallpaperRotationMode _rotationMode;
    private WallpaperStyle _style = WallpaperStyle.Fill;
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
    public bool IncludeSubfolders { get => _includeSubfolders; set { _includeSubfolders = value; OnPropertyChanged(); } }
    public HashSet<DayOfWeek> DaysOfWeek { get; set; } = [];
    public ObservableCollection<SourceEditorItem> Sources { get; } = [];

    public static RuleEditorItem FromRule(WallpaperRule rule)
    {
        var item = new RuleEditorItem
        {
            Id = rule.Id,
            Name = rule.Name,
            StartText = rule.Start.ToString("HH:mm"),
            EndText = rule.End.ToString("HH:mm"),
            Enabled = rule.Enabled,
            Priority = rule.Priority,
            RotationIntervalMinutes = rule.RotationIntervalMinutes,
            RotationMode = rule.RotationMode,
            Style = rule.Style,
            DaysOfWeek = new HashSet<DayOfWeek>(rule.DaysOfWeek),
            IncludeSubfolders = rule.Source?.IncludeSubfolders ?? false
        };

        if (rule.Source is not null)
            foreach (var source in rule.Source.Items)
                item.Sources.Add(new(source.Kind, source.Path));

        if (!string.IsNullOrWhiteSpace(rule.Image) && item.Sources.Count == 0)
            item.Sources.Add(new(WallpaperSourceKind.File, rule.Image));

        return item;
    }

    public WallpaperRule ToRule(TimeOnly start, TimeOnly end, int order) => new()
    {
        Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
        Name = string.IsNullOrWhiteSpace(Name) ? "Período" : Name.Trim(),
        Enabled = Enabled,
        Priority = Priority,
        Order = order,
        DaysOfWeek = DaysOfWeek.Count == 0
            ? new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>())
            : new HashSet<DayOfWeek>(DaysOfWeek),
        Start = start,
        End = end,
        Style = Style,
        Scope = WallpaperScope.AllMonitors,
        RotationMode = RotationMode,
        RotationIntervalMinutes = RotationIntervalMinutes is > 0 ? RotationIntervalMinutes : null,
        Source = new WallpaperSource
        {
            IncludeSubfolders = IncludeSubfolders,
            Items = Sources.Select(x => new WallpaperSourceItem { Kind = x.Kind, Path = x.Path }).ToList()
        }
    };

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
    public async void Execute(object? parameter)
    {
        if (_executing) return;
        try { _executing = true; RaiseCanExecuteChanged(); await execute(); }
        finally { _executing = false; RaiseCanExecuteChanged(); }
    }
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
