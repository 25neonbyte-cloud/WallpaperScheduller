using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WallpaperScheduler.Application;
using FormsScreen = System.Windows.Forms.Screen;
using WpfButton = System.Windows.Controls.Button;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfPlacementMode = System.Windows.Controls.Primitives.PlacementMode;

namespace WallpaperScheduler.App;

public partial class MainWindow : Window
{
    private readonly string _windowStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperScheduler",
        "window-state.json");

    private readonly IAppLogger _logger;
    private bool _restoreMaximized;

    public MainWindow(MainViewModel viewModel, IAppLogger logger, AppThemeManager appThemeManager)
    {
        _logger = logger;
        InitializeComponent();
        DataContext = viewModel;

        // O XAML principal ainda contém o cartão de apresentação histórico. O painel
        // funcional ocupa a mesma região sem duplicar scheduler nem criar outra janela.
        ComfortSection.Child = new VisualComfortPanel();
        appThemeManager.RegisterWindow(this);

        SourceInitialized += OnSourceInitialized;
        Closing += OnWindowClosingSaveState;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        DpiDiagnostics.Attach(this, _logger);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => DpiDiagnostics.LogWindow(this, _logger, "Loaded")));

        var saved = LoadWindowState();
        if (saved is not null && IsUsable(saved))
        {
            WindowState = WindowState.Normal;
            Left = saved.Left;
            Top = saved.Top;
            Width = saved.Width;
            Height = saved.Height;
            _restoreMaximized = string.Equals(saved.State, nameof(WindowState.Maximized), StringComparison.Ordinal);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ClampToCurrentWorkArea));
            return;
        }

        FitToCurrentWorkArea();
    }

    private void FitToCurrentWorkArea()
    {
        var area = GetCurrentWorkArea();
        UpdateDynamicMinimums(area);

        var preferredWidth = Math.Min(1480, area.Width * 0.90);
        var preferredHeight = Math.Min(960, area.Height * 0.90);

        Width = Math.Min(Math.Max(preferredWidth, MinWidth), area.Width);
        Height = Math.Min(Math.Max(preferredHeight, MinHeight), area.Height);
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    private void ClampToCurrentWorkArea()
    {
        var area = GetCurrentWorkArea();
        UpdateDynamicMinimums(area);

        var width = double.IsFinite(Width) && Width > 0 ? Width : area.Width * 0.90;
        var height = double.IsFinite(Height) && Height > 0 ? Height : area.Height * 0.90;

        width = Math.Min(Math.Max(width, MinWidth), area.Width);
        height = Math.Min(Math.Max(height, MinHeight), area.Height);

        var left = double.IsFinite(Left) ? Left : area.Left;
        var top = double.IsFinite(Top) ? Top : area.Top;

        Left = Math.Clamp(left, area.Left, area.Right - width);
        Top = Math.Clamp(top, area.Top, area.Bottom - height);
        Width = width;
        Height = height;

        if (_restoreMaximized)
        {
            _restoreMaximized = false;
            WindowState = WindowState.Maximized;
        }
    }

    private void UpdateDynamicMinimums(Rect area)
    {
        MinWidth = Math.Min(860, area.Width);
        MinHeight = Math.Min(560, area.Height);
    }

    private Rect GetCurrentWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var screen = FormsScreen.FromHandle(handle);
        var source = HwndSource.FromHwnd(handle);

        if (source?.CompositionTarget is null)
            return SystemParameters.WorkArea;

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new System.Windows.Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void OnWindowClosingSaveState(object? sender, CancelEventArgs e) => SaveWindowState();

    private void SaveWindowState()
    {
        try
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

            Directory.CreateDirectory(Path.GetDirectoryName(_windowStatePath)!);
            var state = new WindowVisualState
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                State = WindowState == WindowState.Maximized ? nameof(WindowState.Maximized) : nameof(WindowState.Normal)
            };
            File.WriteAllText(_windowStatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Estado visual é conveniência; nunca deve impedir o encerramento do aplicativo.
        }
    }

    private WindowVisualState? LoadWindowState()
    {
        try
        {
            if (!File.Exists(_windowStatePath)) return null;
            return JsonSerializer.Deserialize<WindowVisualState>(File.ReadAllText(_windowStatePath));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUsable(WindowVisualState state) =>
        double.IsFinite(state.Left) && double.IsFinite(state.Top) &&
        double.IsFinite(state.Width) && double.IsFinite(state.Height) &&
        state.Width > 200 && state.Height > 200;

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string destination) return;
        switch (destination)
        {
            case "home": MainScrollViewer.ScrollToTop(); break;
            case "monitors": MonitorsSection.BringIntoView(); break;
            case "comfort": ComfortSection.BringIntoView(); break;
            case "schedule": ScheduleSection.BringIntoView(); break;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = WpfPlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void PlannedFeature_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button) return;
        var feature = button.Tag?.ToString() ?? "Conforto visual";
        System.Windows.MessageBox.Show(
            this,
            $"O módulo “{feature}” já está reservado na nova arquitetura da interface. O motor de automação será implementado em uma etapa própria, sem alterar o scheduler já validado.",
            feature,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SetGlobalScope_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: RuleEditorItem item })
            item.Scope = WallpaperScheduler.Domain.WallpaperScope.AllMonitors;
    }

    private void SetPerMonitorScope_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: RuleEditorItem item })
            item.Scope = WallpaperScheduler.Domain.WallpaperScope.PerMonitor;
    }

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new WpfOpenFileDialog
        {
            Title = "Importar configuração do Wallpaper Scheduler",
            Filter = "Configuração JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        var confirmation = System.Windows.MessageBox.Show(
            this,
            "A importação substituirá os períodos e fontes atualmente salvos. A preferência local de inicialização com o Windows será preservada. Continuar?",
            "Importar configuração",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        await viewModel.ImportConfigAsync(dialog.FileName);
    }

    private async void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new WpfSaveFileDialog
        {
            Title = "Exportar configuração do Wallpaper Scheduler",
            Filter = "Configuração JSON (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"WallpaperScheduler-config-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        await viewModel.ExportConfigAsync(dialog.FileName);
    }

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.OpenDiagnosticsFolder();
    }

    private void WallpaperDropZone_Drop(object sender, WpfDragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not FrameworkElement element || element.Tag is not RuleEditorItem item) return;
        if (!TryGetDroppedPaths(e, out var paths)) return;
        viewModel.AddDroppedSources(item, paths);
        e.Handled = true;
    }

    private void MonitorWallpaperDropZone_Drop(object sender, WpfDragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not FrameworkElement element || element.Tag is not MonitorSourceEditorItem item) return;
        if (!TryGetDroppedPaths(e, out var paths)) return;
        viewModel.AddDroppedMonitorSources(item, paths);
        e.Handled = true;
    }

    private static bool TryGetDroppedPaths(WpfDragEventArgs e, out string[] paths)
    {
        paths = [];
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return false;
        paths = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[] ?? [];
        return paths.Length > 0;
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not WpfButton button || button.Tag is not SourceEditorItem source) return;
        var rule = FindRuleEditor(button);
        if (rule is null) return;
        viewModel.RemoveSource(rule, source);
    }

    private void RemoveMonitorSource_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not WpfButton button || button.Tag is not SourceEditorItem source) return;
        var monitorSource = FindMonitorSourceEditor(button);
        if (monitorSource is null) return;
        viewModel.RemoveMonitorSource(monitorSource, source);
    }

    private async void ForgetMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not WpfMenuItem menuItem || menuItem.DataContext is not MonitorProfileEditorItem profile) return;
        await viewModel.ForgetMonitorAsync(profile);
    }

    private static RuleEditorItem? FindRuleEditor(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is RuleEditorItem rule) return rule;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static MonitorSourceEditorItem? FindMonitorSourceEditor(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is MonitorSourceEditorItem monitorSource) return monitorSource;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private sealed class WindowVisualState
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string State { get; set; } = nameof(WindowState.Normal);
    }
}
