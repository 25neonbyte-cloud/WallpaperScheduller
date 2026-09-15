using System.Windows;
using System.Windows.Controls;

namespace WallpaperScheduler.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void WallpaperDropZone_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not FrameworkElement element || element.Tag is not RuleEditorItem item) return;
        if (!TryGetDroppedPaths(e, out var paths)) return;

        viewModel.AddDroppedSources(item, paths);
        e.Handled = true;
    }

    private void MonitorWallpaperDropZone_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not FrameworkElement element || element.Tag is not MonitorSourceEditorItem item) return;
        if (!TryGetDroppedPaths(e, out var paths)) return;

        viewModel.AddDroppedMonitorSources(item, paths);
        e.Handled = true;
    }

    private static bool TryGetDroppedPaths(DragEventArgs e, out string[] paths)
    {
        paths = [];
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        return paths.Length > 0;
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not Button button || button.Tag is not SourceEditorItem source) return;

        var rule = FindRuleEditor(button);
        if (rule is null) return;
        viewModel.RemoveSource(rule, source);
    }

    private void RemoveMonitorSource_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not Button button || button.Tag is not SourceEditorItem source) return;

        var monitorSource = FindMonitorSourceEditor(button);
        if (monitorSource is null) return;
        viewModel.RemoveMonitorSource(monitorSource, source);
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
