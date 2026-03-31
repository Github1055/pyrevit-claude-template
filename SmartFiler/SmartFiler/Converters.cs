using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartFiler;

/// <summary>Converts file size in bytes to a human-readable string (KB, MB, GB).</summary>
public class FileSizeConverter : IValueConverter
{
    public static readonly FileSizeConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes) return "—";
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns Visible if value > 0, Collapsed otherwise.</summary>
public class VisibleIfPositiveConverter : IValueConverter
{
    public static readonly VisibleIfPositiveConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i) return i > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns Visible if value is not null, Collapsed otherwise.</summary>
public class VisibleIfNotNullConverter : IValueConverter
{
    public static readonly VisibleIfNotNullConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns Visible if string is not null or empty, Collapsed otherwise.</summary>
public class VisibleIfNotEmptyConverter : IValueConverter
{
    public static readonly VisibleIfNotEmptyConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts bool IsScanning to a status string.</summary>
public class ScanStatusConverter : IValueConverter
{
    public static readonly ScanStatusConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool scanning) return scanning ? "Scanning..." : "Ready";
        return "Ready";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts SelectedTabIndex to IsChecked for RadioButtons.</summary>
public class TabIndexConverter : IValueConverter
{
    public static readonly TabIndexConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int index && parameter is string paramStr && int.TryParse(paramStr, out var target))
            return index == target;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string paramStr && int.TryParse(paramStr, out var target))
            return target;
        return System.Windows.Data.Binding.DoNothing;
    }
}

/// <summary>Converts FileAction to a status icon: M=move, ⏸=deferred, X=delete, ·=pending.</summary>
public class ActionToIconConverter : IValueConverter
{
    public static readonly ActionToIconConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SmartFiler.Data.FileAction action)
            return action switch
            {
                SmartFiler.Data.FileAction.Approved => "M",       // Move pending
                SmartFiler.Data.FileAction.Deferred => "\u23F8",  // ⏸
                SmartFiler.Data.FileAction.Delete   => "X",       // Delete pending
                _ => "\u00B7"                                      // · not yet actioned
            };
        return "\u00B7";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts FileAction to a color: green approved, yellow deferred, red delete, dim pending.</summary>
public class ActionToColorConverter : IValueConverter
{
    public static readonly ActionToColorConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SmartFiler.Data.FileAction action)
        {
            var key = action switch
            {
                SmartFiler.Data.FileAction.Approved => "SuccessBrush",
                SmartFiler.Data.FileAction.Deferred => "WarningBrush",
                SmartFiler.Data.FileAction.Delete   => "DangerBrush",
                _ => "TextDimBrush"
            };
            return System.Windows.Application.Current.FindResource(key);
        }
        return System.Windows.Application.Current.FindResource("TextDimBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a hex color string (e.g. "#0EA5A9") to a SolidColorBrush.</summary>
public class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                // Fall through to transparent
            }
        }
        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns "Active" (green) if the folder path exists, "Missing" (red) if not.</summary>
public class FolderExistsToTextConverter : IValueConverter
{
    public static readonly FolderExistsToTextConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string path && Directory.Exists(path) ? "Active" : "Missing";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns SuccessBrush if the folder exists, DangerBrush if not.</summary>
public class FolderExistsToBrushConverter : IValueConverter
{
    public static readonly FolderExistsToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var exists = value is string path && Directory.Exists(path);
        var key = exists ? "SuccessBrush" : "DangerBrush";
        return System.Windows.Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
