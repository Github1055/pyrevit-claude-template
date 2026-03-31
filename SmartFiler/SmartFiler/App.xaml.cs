using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SmartFiler.Data;
using SmartFiler.Services;
using SmartFiler.ViewModels;

namespace SmartFiler;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Database
        services.AddSingleton<SmartFilerDb>();

        // Repositories
        services.AddSingleton<MoveHistoryRepo>();
        services.AddSingleton<ProjectIndexRepo>();
        services.AddSingleton<DeferredFileRepo>();
        services.AddSingleton<ScanStatsRepo>();
        services.AddSingleton<AliasRepo>();
        services.AddSingleton<BatchJournalRepo>();

        // Settings (loads scan sources, custom colors, presets)
        var settingsService = new SettingsService();
        services.AddSingleton(settingsService);

        // Services (FileScanner and DuplicateDetector get paths from settings)
        services.AddSingleton(new FileScanner(settingsService.Settings.ScanSources));
        services.AddSingleton(new DuplicateDetector(settingsService.Settings.ScanSources));
        services.AddSingleton<FuzzyMatcher>();
        services.AddSingleton<RevitLibraryMatcher>();
        services.AddSingleton<ProjectIndexer>();
        services.AddSingleton<SuggestionEngine>();
        services.AddSingleton<MoveService>();
        services.AddSingleton<StaleDetector>();

        // ViewModels
        services.AddTransient<MainViewModel>();

        Services = services.BuildServiceProvider();

        // Ensure database tables exist
        var db = Services.GetRequiredService<SmartFilerDb>();
        db.EnsureCreated();

        // Load saved theme preference (dark/light)
        ThemeManager.LoadSavedTheme();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is IDisposable disposable)
            disposable.Dispose();

        base.OnExit(e);
    }
}
