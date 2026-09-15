using System.Globalization;
using System.Windows.Data;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.App;

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        WallpaperRotationMode.Sequential => "Sequencial",
        WallpaperRotationMode.Random => "Aleatório",
        WallpaperStyle.Fill => "Preencher",
        WallpaperStyle.Fit => "Ajustar",
        WallpaperStyle.Span => "Estender",
        WallpaperStyle.Center => "Centralizar",
        WallpaperStyle.Stretch => "Esticar",
        WallpaperStyle.Tile => "Lado a lado",
        WallpaperScope.AllMonitors => "Todos os monitores",
        WallpaperScope.PerMonitor => "Por monitor",
        SystemThemeMode.Light => "Claro",
        SystemThemeMode.Dark => "Escuro",
        VisualControlMode.Manual => "Manual",
        VisualControlMode.Scheduled => "Automático por horário",
        ApplicationThemeMode.FollowSystem => "Seguir Windows",
        ApplicationThemeMode.Light => "Claro",
        ApplicationThemeMode.Dark => "Escuro",
        TemperatureApplicationMethod.Automatic => "Automático → Software",
        TemperatureApplicationMethod.Software => "Software",
        TemperatureApplicationMethod.DdcCi => "Hardware (DDC/CI)",
        VisualRoutineThemeTarget.Manual => "Usar controle base",
        VisualRoutineThemeTarget.Light => "Claro",
        VisualRoutineThemeTarget.Dark => "Escuro",
        null => string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
