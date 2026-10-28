using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using PowerIsland.Models;

namespace PowerIsland.Converters;

/// <summary>电量 → 颜色（低电量红色、偏低橙色、正常绿色）。</summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int? level = value as int?;
        bool tray = parameter as string == "tray";

        if (level == null) return System.Windows.Application.Current.TryFindResource("Brush.TextSoft") as MediaBrush ?? MediaBrushes.Gray;

        string key = level < 15 ? "Brush.Danger" : level < 30 ? "Brush.Warn" : "Brush.Accent";
        if (tray)
        {
            return level < 15 ? new SolidColorBrush(MediaColor.FromRgb(0xFF, 0x8B, 0x8B))
                 : level < 30 ? new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xC1, 0x6B))
                 : new SolidColorBrush(MediaColor.FromRgb(0xF2, 0xF4, 0xF6));
        }

        return System.Windows.Application.Current.TryFindResource(key) as MediaBrush ?? MediaBrushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class ArtTemplateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value as string ?? "ArtOther";
        return System.Windows.Application.Current.TryFindResource(key) as DataTemplate;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
