using System.Globalization;
using System.Windows.Data;

namespace WallpaperScheduler.App;

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Sequential" => "Sequencial",
            "Random" => "Aleatório",
            "Fill" => "Preencher",
            "Fit" => "Ajustar",
            "Span" => "Estender",
            "Center" => "Centralizar",
            "Stretch" => "Esticar",
            "Tile" => "Lado a lado",
            "AllMonitors" => "Todos os monitores",
            "PerMonitor" => "Por monitor",
            "Light" => "Claro",
            "Dark" => "Escuro",
            "Automatic" => "Automático (Software)",
            "Software" => "Software",
            "DdcCi" => "Hardware (DDC/CI)",
            "Manual" => "Usar estado manual",
            null => string.Empty,
            var text => text
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
