using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WallpaperScheduler.App;

internal static class RenderingBootstrap
{
    private static bool _configured;

    public static void Configure()
    {
        if (_configured) return;
        _configured = true;

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));

        EventManager.RegisterClassHandler(
            typeof(Border),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnBorderLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;

        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;

        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(window, TextHintingMode.Fixed);
        RenderOptions.SetClearTypeHint(window, ClearTypeHint.Enabled);

        // A seção já existe no layout validado; apenas substituímos o conteúdo placeholder
        // pelo painel funcional, preservando geometria, cores e navegação da janela principal.
        if (window is MainWindow mainWindow &&
            mainWindow.FindName("ComfortSection") is Border comfortSection &&
            comfortSection.Child is not VisualComfortPanel)
        {
            comfortSection.Child = new VisualComfortPanel();
        }
    }

    private static void OnBorderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border) return;

        border.SnapsToDevicePixels = true;

        // Effects create an intermediate render target. WPF normally disables ClearType
        // in this situation. Our card surfaces are opaque, so explicitly mark only those
        // effect-backed borders as safe for ClearType while preserving the visual shadow.
        if (border.Effect is not null && border.Background is not null)
            RenderOptions.SetClearTypeHint(border, ClearTypeHint.Enabled);
    }
}
