using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WallpaperScheduler.Application;

namespace WallpaperScheduler.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly WallpaperOrchestrator _orchestrator;
    private string _status = "Pronto.";
    private bool _busy;

    public MainViewModel(WallpaperOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        ApplyNowCommand = new AsyncCommand(ApplyNowAsync, () => !Busy);
    }

    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public bool Busy { get => _busy; private set { _busy = value; OnPropertyChanged(); (ApplyNowCommand as AsyncCommand)?.RaiseCanExecuteChanged(); } }
    public ICommand ApplyNowCommand { get; }

    private async Task ApplyNowAsync()
    {
        try
        {
            Busy = true;
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
