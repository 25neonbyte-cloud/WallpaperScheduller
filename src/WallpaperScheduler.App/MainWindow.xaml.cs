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
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths is null || paths.Length == 0) return;

        viewModel.AddDroppedSources(item, paths);
        e.Handled = true;
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (sender is not Button button || button.Tag is not SourceEditorItem source) return;
        if (button.DataContext is not SourceEditorItem) return;

        var rule = FindRuleEditor(button);
        if (rule is null) return;

        viewModel.RemoveSource(rule, source);
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
}
