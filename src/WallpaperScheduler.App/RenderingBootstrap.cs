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

        // UI e layout permanecem intactos. Este handler atua somente na viewport
        // interna de edição dos campos para impedir recorte vertical de glifos.
        // O registro na classe TextBox também cobre controles derivados, como
        // Time24TextBox, sem exigir mudanças individuais de geometria no XAML.
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnTextBoxLoaded));
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

    private static void OnTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        // O template global transforma Padding em margem do PART_ContentHost.
        // Padding vertical reduz a viewport real em campos de altura fixa e pode
        // cortar topo/base de números e letras dependendo de DPI e font metrics.
        // Preservamos somente o espaçamento horizontal: nenhuma dimensão externa
        // ou distribuição da interface é modificada.
        var padding = textBox.Padding;
        if (padding.Top != 0 || padding.Bottom != 0)
            textBox.Padding = new Thickness(padding.Left, 0, padding.Right, 0);

        textBox.VerticalContentAlignment = VerticalAlignment.Center;
        textBox.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(textBox, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(textBox, TextHintingMode.Fixed);
    }
}
