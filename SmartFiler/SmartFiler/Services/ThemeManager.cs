using System;
using System.IO;
using System.Windows;

namespace SmartFiler.Services;

/// <summary>
/// Manages light/dark theme switching and persists the user's preference.
/// </summary>
public static class ThemeManager
{
    public enum Theme { Dark, Light }

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartFiler");

    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "theme.txt");

    public static Theme CurrentTheme { get; private set; } = Theme.Dark;

    /// <summary>
    /// Toggles between dark and light themes at runtime.
    /// </summary>
    public static void ToggleTheme()
    {
        var newTheme = CurrentTheme == Theme.Dark ? Theme.Light : Theme.Dark;
        ApplyTheme(newTheme);
        SavePreference(newTheme);
    }

    /// <summary>
    /// Loads the saved theme preference and applies it. Call once during startup.
    /// </summary>
    public static void LoadSavedTheme()
    {
        var theme = Theme.Dark; // default

        try
        {
            if (File.Exists(SettingsFile))
            {
                var text = File.ReadAllText(SettingsFile).Trim();
                if (Enum.TryParse<Theme>(text, ignoreCase: true, out var parsed))
                    theme = parsed;
            }
        }
        catch
        {
            // If the file is unreadable, fall back to dark
        }

        ApplyTheme(theme);
    }

    private static void ApplyTheme(Theme theme)
    {
        CurrentTheme = theme;

        var mergedDicts = System.Windows.Application.Current.Resources.MergedDictionaries;

        // Find and remove the current Colors dictionary (index 0 by convention)
        if (mergedDicts.Count > 0)
            mergedDicts.RemoveAt(0);

        // Build the pack URI for the chosen color dictionary
        var colorFile = theme == Theme.Light ? "LightColors.xaml" : "Colors.xaml";
        var uri = new Uri($"Core/Theme/{colorFile}", UriKind.Relative);
        var colorDict = new ResourceDictionary { Source = uri };

        // Insert at position 0 so Controls.xaml (position 1) can resolve color keys
        mergedDicts.Insert(0, colorDict);
    }

    private static void SavePreference(Theme theme)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsFile, theme.ToString());
        }
        catch
        {
            // Non-critical — silently ignore write failures
        }
    }
}
