using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly WallpaperOrchestrator _orchestrator;
    private readonly IMonitorService _monitorService;
    private string _status = "Pronto.";
    private string _monitorDiagnostics = "Detectando monitores...";
    private bool _busy;

    public MainViewModel(WallpaperOrchestrator orchestrator, IMonitorService monitorService)
    {
        _orchestrator = orchestrator;
        _monitorService = monitorService;
        ApplyNowCommand = new AsyncCommand(ApplyNowAsync, () => !Busy);
        RefreshMonitorsCommand = new RelayCommand(RefreshMonitors, () => !Busy);
        RefreshMonitors();
    }

    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public string MonitorDiagnostics { get => _monitorDiagnostics; private set { _monitorDiagnostics = value; OnPropertyChanged(); } }
    public bool Busy
    {
        get => _busy;
        private set
        {
            _busy = value;
            OnPropertyChanged();
            (ApplyNowCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (RefreshMonitorsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand ApplyNowCommand { get; }
    public ICommand RefreshMonitorsCommand { get; }

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
            text.AppendLine($"Monitores ativos: {monitors.Count}");
            text.AppendLine();

            for (var i = 0; i < monitors.Count; i++)
            {
                var monitor = monitors[i];
                text.AppendLine($"Monitor {i + 1}: {monitor.Width} × {monitor.Height}");
                text.AppendLine($"ID: {monitor.Id}");
                if (i < monitors.Count - 1) text.AppendLine();
            }

            MonitorDiagnostics = text.ToString().TrimEnd();
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
            RefreshMonitors();
            var result = await _orchestrator.ApplyCurrentAsync();
            Status = result.Applied ? $"Aplicado: {result.Message}" : result.Message;
        }
        catch (Exception ex) { Status = $"Erro: {ex.Message}"; }
        finally { Busy = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

internal sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) => await execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
