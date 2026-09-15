using System.Windows;
using System.Windows.Media;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.App;

public sealed class AppThemeManager(ISystemThemeService systemThemeService)
{
    private ApplicationThemeMode? _lastPreference;
    private SystemThemeMode? _lastResolved;

    public void Apply(ApplicationThemeMode preference)
    {
        var resolved = preference switch
        {
            ApplicationThemeMode.Dark => SystemThemeMode.Dark,
            ApplicationThemeMode.Light => SystemThemeMode.Light,
            _ => systemThemeService.GetCurrentMode()
        };

        if (_lastPreference == preference && _lastResolved == resolved)
            return;

        void ApplyCore()
        {
            var resources = System.Windows.Application.Current.Resources;
            ApplyPalette(resources, resolved);
            _lastPreference = preference;
            _lastResolved = resolved;
        }

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
            ApplyCore();
        else
            dispatcher.Invoke(ApplyCore);
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
        resources["TextSecondaryBrush"] = Solid(dark ? "#A7B4C7" : "#60708D");
        resources["TextMutedBrush"] = Solid(dark ? "#74849A" : "#8794AA");
        resources["BorderBrush"] = Solid(dark ? "#2A3748" : "#DDE6F2");
        resources["BorderStrongBrush"] = Solid(dark ? "#394A60" : "#C9D6E8");

        resources["AccentBrush"] = Solid(dark ? "#4B9CFF" : "#1177F4");
        resources["AccentHoverBrush"] = Solid(dark ? "#6AACFF" : "#0868E6");
        resources["AccentSoftBrush"] = Solid(dark ? "#142A44" : "#E9F3FF");
        resources["AccentSofterBrush"] = Solid(dark ? "#111F31" : "#F4F9FF");
        resources["SuccessBrush"] = Solid(dark ? "#42C777" : "#149447");
        resources["SuccessSoftBrush"] = Solid(dark ? "#102A1B" : "#EAF8EF");
        resources["DangerBrush"] = Solid(dark ? "#FF6D73" : "#DC3F46");
        resources["DangerSoftBrush"] = Solid(dark ? "#32171A" : "#FFF0F1");

        resources["MonitorGroupBrush"] = Gradient(dark ? "#0F1D2B" : "#F1F8FF", dark ? "#12253A" : "#E8F3FF", horizontal: true);
        resources["MonitorGroupBorderBrush"] = Solid(dark ? "#24476B" : "#C9E2FF");
        resources["MonitorAccentBrush"] = Solid(dark ? "#55A8FF" : "#1677E8");
        resources["MonitorIconBrush"] = Solid(dark ? "#153554" : "#E1F0FF");

        resources["ComfortGroupBrush"] = Gradient(dark ? "#241D10" : "#FFFBF2", dark ? "#2A2010" : "#FFF4E2", horizontal: true);
        resources["ComfortGroupBorderBrush"] = Solid(dark ? "#5A4421" : "#F2D59A");
        resources["ComfortAccentBrush"] = Solid(dark ? "#FFB534" : "#D98B00");
        resources["ComfortIconBrush"] = Solid(dark ? "#3B2B10" : "#FFF0C8");
        resources["ComfortCardBrush"] = Solid(dark ? "#211A0F" : "#FFFCF6");

        resources["ScheduleGroupBrush"] = Gradient(dark ? "#191528" : "#F8F6FF", dark ? "#211A35" : "#F1EEFF", horizontal: true);
        resources["ScheduleGroupBorderBrush"] = Solid(dark ? "#493D72" : "#D9D0FF");
        resources["ScheduleAccentBrush"] = Solid(dark ? "#A88BFF" : "#7457E8");
        resources["ScheduleIconBrush"] = Solid(dark ? "#2C2347" : "#ECE7FF");

        resources["MonitorCardBrush"] = Solid(dark ? "#141D27" : "#FFFFFF");
        resources["MonitorCardBorderBrush"] = Solid(dark ? "#28405A" : "#D9E8F8");
        resources["ScheduleCardBrush"] = Solid(dark ? "#171420" : "#FFFFFF");
        resources["ScheduleCardBorderBrush"] = Solid(dark ? "#3A315A" : "#DDD8F4");
        resources["ScheduleDividerBrush"] = Solid(dark ? "#302849" : "#ECE8F9");
        resources["DropZoneBrush"] = Solid(dark ? "#151E28" : "#FFFFFF");
        resources["DropZoneBorderBrush"] = Solid(dark ? "#335D88" : "#94C4FF");
        resources["SidebarCalloutBorderBrush"] = Solid(dark ? "#294561" : "#D6E8FF");
        resources["ComfortBadgeBrush"] = Solid(dark ? "#3B2B10" : "#FFF0C8");
        resources["ComfortStatusBrush"] = Solid(dark ? "#2B2112" : "#FFF8E6");
        resources["DangerBorderBrush"] = Solid(dark ? "#5B2B2F" : "#F6C9CC");
        resources["ToggleTrackBrush"] = Solid(dark ? "#3A4758" : "#DCE4EF");
        resources["ToggleThumbBrush"] = Solid("#FFFFFF");
    }

    private static SolidColorBrush Solid(string color) =>
        new((Color)ColorConverter.ConvertFromString(color)!);

    private static LinearGradientBrush Gradient(string start, string end, bool horizontal)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = horizontal ? new Point(1, 1) : new Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(start)!, 0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(end)!, 1));
        return brush;
    }
}
