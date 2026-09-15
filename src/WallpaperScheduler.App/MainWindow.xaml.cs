using System.Windows;
using WpfButton = System.Windows.Controls.Button;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace WallpaperScheduler.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
            if (current is FrameworkElement element && element.DataContext is RuleEditorItem rule)
                return rule;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static MonitorSourceEditorItem? FindMonitorSourceEditor(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is MonitorSourceEditorItem monitorSource)
                return monitorSource;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
