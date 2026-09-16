using System.Windows;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfGradientStop = System.Windows.Media.GradientStop;
using WpfLinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;
using WpfPoint = System.Windows.Point;

namespace WallpaperScheduler.App;

public sealed class AppThemeManager(ISystemThemeService systemThemeService)
{
    private ApplicationThemeMode? _lastPreference;
    private SystemThemeMode? _lastResolved;

    public void RegisterWindow(Window window)
    {
        void ApplyToWindow()
        {
            AdoptThemeResources(window);
            Apply(_lastPreference ?? ApplicationThemeMode.FollowSystem, force: true);
        }

        // DataTemplates do scheduler são materializados depois do carregamento da
        // configuração. O adaptador antigo percorria a árvore somente uma vez e,
        // por isso, cards criados depois continuavam literalmente brancos no tema
        // escuro. Reaplicamos apenas no subtree que acabou de ser carregado.
        window.AddHandler(
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((_, args) =>
            {
                if (args.OriginalSource is DependencyObject loaded)
                    AdoptThemeResources(loaded);
            }),
            handledEventsToo: true);

        if (window.IsLoaded)
            ApplyToWindow();
        else
            window.Loaded += (_, _) => ApplyToWindow();
    }

    public void Apply(ApplicationThemeMode preference) => Apply(preference, force: false);

    private void Apply(ApplicationThemeMode preference, bool force)
    {
        var resolved = preference switch
        {
            ApplicationThemeMode.Dark => SystemThemeMode.Dark,
            ApplicationThemeMode.Light => SystemThemeMode.Light,
            _ => systemThemeService.GetCurrentMode()
        };

        if (!force && _lastPreference == preference && _lastResolved == resolved)
            return;

        void ApplyCore()
        {
            var app = System.Windows.Application.Current;
            ApplyPalette(app.Resources, resolved);
            foreach (Window window in app.Windows)
                AdoptThemeResources(window);
            _lastPreference = preference;
            _lastResolved = resolved;
        }

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
            ApplyCore();
        else
            dispatcher.Invoke(ApplyCore);
    }

    // A maior parte da interface usa DynamicResource. Este adaptador cobre os
    // poucos brushes literais herdados do layout anterior, inclusive elementos
    // materializados depois do carregamento inicial.
    private static void AdoptThemeResources(DependencyObject root)
    {
        if (root is WpfBorder border)
        {
            if (TryColor(border.Background, out var background))
            {
                var key = background switch
                {
                    "#FFFFFFFF" => "SurfaceBrush",
                    "#FFFFF0C8" => "ComfortBadgeBrush",
                    _ => null
                };
                if (key is not null)
                    border.SetResourceReference(WpfBorder.BackgroundProperty, key);
            }

            if (TryColor(border.BorderBrush, out var borderColor))
            {
                var key = borderColor switch
                {
                    "#FFD6E8FF" => "SidebarCalloutBorderBrush",
                    "#FFD9E8F8" => "MonitorCardBorderBrush",
                    "#FFDDD8F4" => "ScheduleCardBorderBrush",
                    "#FFECE8F9" => "ScheduleDividerBrush",
                    "#FF94C4FF" or "#FFA9CDF8" => "DropZoneBorderBrush",
                    _ => null
                };
                if (key is not null)
                    border.SetResourceReference(WpfBorder.BorderBrushProperty, key);
            }
        }
        else if (root is WpfButton button && TryColor(button.BorderBrush, out var buttonBorder) && buttonBorder == "#FFF6C9CC")
        {
            button.SetResourceReference(WpfControl.BorderBrushProperty, "DangerBorderBrush");
        }

        var count = WpfVisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            AdoptThemeResources(WpfVisualTreeHelper.GetChild(root, i));
    }

    private static bool TryColor(WpfBrush? brush, out string value)
    {
        if (brush is WpfSolidColorBrush solid)
        {
            value = solid.Color.ToString().ToUpperInvariant();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void ApplyPalette(ResourceDictionary resources, SystemThemeMode mode)
    {
        var dark = mode == SystemThemeMode.Dark;

        resources["AppBackgroundBrush"] = Gradient(dark ? "#0E131A" : "#F7FAFE", dark ? "#111821" : "#F2F6FC", horizontal: true);
        resources["SidebarBrush"] = Gradient(dark ? "#0C1118" : "#FBFDFF", dark ? "#121A24" : "#EDF5FF", horizontal: false);
        resources["SurfaceBrush"] = Solid(dark ? "#161E29" : "#FFFFFFFF");
        resources["SurfaceMutedBrush"] = Solid(dark ? "#111821" : "#F8FAFD");
        resources["SurfaceHoverBrush"] = Solid(dark ? "#1B2633" : "#F2F6FC");
        resources["TextPrimaryBrush"] = Solid(dark ? "#E9F0FA" : "#0D1B3A");
        resources["TextSecondaryBrush"] = Solid(dark ? "#B8C4D5" : "#60708D");
        resources["TextMutedBrush"] = Solid(dark ? "#91A0B5" : "#8794AA");
        resources["BorderBrush"] = Solid(dark ? "#334255" : "#DDE6F2");
        resources["BorderStrongBrush"] = Solid(dark ? "#4A5E77" : "#C9D6E8");

        resources["AccentBrush"] = Solid(dark ? "#4B9CFF" : "#1177F4");
        resources["AccentHoverBrush"] = Solid(dark ? "#6AACFF" : "#0868E6");
        resources["AccentSoftBrush"] = Solid(dark ? "#142A44" : "#E9F3FF");
        resources["AccentSofterBrush"] = Solid(dark ? "#111F31" : "#F4F9FF");
        resources["SuccessBrush"] = Solid(dark ? "#57D98A" : "#149447");
        resources["SuccessSoftBrush"] = Solid(dark ? "#102A1B" : "#EAF8EF");
        resources["DangerBrush"] = Solid(dark ? "#FF7C82" : "#DC3F46");
        resources["DangerSoftBrush"] = Solid(dark ? "#32171A" : "#FFF0F1");

        resources["MonitorGroupBrush"] = Gradient(dark ? "#0F1D2B" : "#F1F8FF", dark ? "#12253A" : "#E8F3FF", horizontal: true);
        resources["MonitorGroupBorderBrush"] = Solid(dark ? "#2F5A84" : "#C9E2FF");
        resources["MonitorAccentBrush"] = Solid(dark ? "#66B2FF" : "#1677E8");
        resources["MonitorIconBrush"] = Solid(dark ? "#153554" : "#E1F0FF");

        resources["ComfortGroupBrush"] = Gradient(dark ? "#241D10" : "#FFFBF2", dark ? "#2A2010" : "#FFF4E2", horizontal: true);
        resources["ComfortGroupBorderBrush"] = Solid(dark ? "#70542A" : "#F2D59A");
        resources["ComfortAccentBrush"] = Solid(dark ? "#FFC04A" : "#D98B00");
        resources["ComfortIconBrush"] = Solid(dark ? "#3B2B10" : "#FFF0C8");
        resources["ComfortCardBrush"] = Solid(dark ? "#211A0F" : "#FFFCF6");

        resources["ScheduleGroupBrush"] = Gradient(dark ? "#191528" : "#F8F6FF", dark ? "#211A35" : "#F1EEFF", horizontal: true);
        resources["ScheduleGroupBorderBrush"] = Solid(dark ? "#5A4A88" : "#D9D0FF");
        resources["ScheduleAccentBrush"] = Solid(dark ? "#B49AFF" : "#7457E8");
        resources["ScheduleIconBrush"] = Solid(dark ? "#2C2347" : "#ECE7FF");

        resources["MonitorCardBrush"] = Solid(dark ? "#141D27" : "#FFFFFF");
        resources["MonitorCardBorderBrush"] = Solid(dark ? "#34506C" : "#D9E8F8");
        resources["ScheduleCardBrush"] = Solid(dark ? "#171420" : "#FFFFFF");
        resources["ScheduleCardBorderBrush"] = Solid(dark ? "#4C416D" : "#DDD8F4");
        resources["ScheduleDividerBrush"] = Solid(dark ? "#3A3154" : "#ECE8F9");
        resources["DropZoneBrush"] = Solid(dark ? "#12243A" : "#FFFFFF");
        resources["DropZoneBorderBrush"] = Solid(dark ? "#4A80B5" : "#94C4FF");
        resources["SidebarCalloutBorderBrush"] = Solid(dark ? "#365873" : "#D6E8FF");
        resources["ComfortBadgeBrush"] = Solid(dark ? "#3B2B10" : "#FFF0C8");
        resources["ComfortStatusBrush"] = Solid(dark ? "#2B2112" : "#FFF8E6");
        resources["DangerBorderBrush"] = Solid(dark ? "#734047" : "#F6C9CC");
        resources["ToggleTrackBrush"] = Solid(dark ? "#4A596D" : "#DCE4EF");
        resources["ToggleThumbBrush"] = Solid("#FFFFFF");
    }

    private static WpfSolidColorBrush Solid(string color) =>
        new((WpfColor)WpfColorConverter.ConvertFromString(color)!);

    private static WpfLinearGradientBrush Gradient(string start, string end, bool horizontal)
    {
        var brush = new WpfLinearGradientBrush
        {
            StartPoint = new WpfPoint(0, 0),
            EndPoint = horizontal ? new WpfPoint(1, 1) : new WpfPoint(0, 1)
        };
        brush.GradientStops.Add(new WpfGradientStop((WpfColor)WpfColorConverter.ConvertFromString(start)!, 0));
        brush.GradientStops.Add(new WpfGradientStop((WpfColor)WpfColorConverter.ConvertFromString(end)!, 1));
        return brush;
    }
}
