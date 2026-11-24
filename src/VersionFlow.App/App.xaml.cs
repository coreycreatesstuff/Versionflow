using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VersionFlow.AdobeIntegration;
using VersionFlow.App.ViewModels;
using VersionFlow.Compatibility;
using VersionFlow.Conversion;
using VersionFlow.Core.Interfaces;
using VersionFlow.Formats;
using VersionFlow.Infrastructure;
using VersionFlow.Validation;

namespace VersionFlow.App;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var splash = new SplashWindow();
        splash.Show();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        var logger = Services.GetRequiredService<LocalFileLogger>();
        logger.Log("INFO", "VersionFlow 1.0.0 starting up.");

        var settingsService = Services.GetRequiredService<ISettingsService>();
        splash.SetStatus("Loading local configuration...");
        var settings = await settingsService.LoadAsync();
        ApplyTheme(settings.Theme);

        DispatcherUnhandledException += (_, args) =>
        {
            logger.Log("ERROR", $"Unhandled UI exception: {args.Exception}", "VF-APP-ERR");
            MessageBox.Show(
                $"An unexpected interface error occurred:\n\n{args.Exception.Message}",
                "VersionFlow",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        splash.SetStatus("Preparing workspace...");
        var mainWindow = Services.GetRequiredService<MainWindow>();
        var vm = Services.GetRequiredService<MainViewModel>();
        await vm.WarmUpAsync();

        // Ensure splash shows long enough for smooth branding feel without slowing startup
        await Task.Delay(320);

        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        mainWindow.Opacity = 0;
        mainWindow.Show();
        mainWindow.ApplyTitleBarTheme(settings.Theme);
        mainWindow.AnimateWindowEntrance();

        await splash.FadeOutAndCloseAsync();
    }

    public static void ApplyTheme(string themeMode)
    {
        bool useLight = themeMode switch
        {
            "Light" => true,
            "System" => IsWindowsLightTheme(),
            _ => false
        };

        var uri = new Uri(useLight ? "Themes/LightTheme.xaml" : "Themes/DarkTheme.xaml", UriKind.Relative);
        var dicts = Current.Resources.MergedDictionaries;
        if (dicts.Count > 0)
            dicts[0] = new ResourceDictionary { Source = uri };

        if (Current.MainWindow is MainWindow mw)
            mw.ApplyTitleBarTheme(themeMode);
    }

    public static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<LocalFileLogger>();
        services.AddSingleton<IConversionHistoryService, JsonConversionHistoryService>();
        services.AddSingleton<IAssetResolver, LocalAssetResolver>();
        services.AddSingleton<IAdobeInstallationService, AdobeInstallationService>();
        services.AddSingleton<AdobeAssistedValidator>();

        services.AddSingleton<IVersionDetector, VersionDetector>();
        services.AddSingleton<IProjectParser, CompositeProjectParser>();
        services.AddSingleton<ICompatibilityAnalyzer, CompatibilityAnalyzer>();
        services.AddSingleton<IConversionPlanner, ConversionPlanner>();
        services.AddSingleton<IProjectValidator, StructuralValidator>();
        services.AddSingleton<ConversionEngine>(sp => new ConversionEngine(
            sp.GetRequiredService<IProjectParser>(),
            sp.GetRequiredService<ICompatibilityAnalyzer>(),
            sp.GetRequiredService<IConversionPlanner>(),
            sp.GetRequiredService<IProjectValidator>(),
            tempRoot: sp.GetRequiredService<ISettingsService>().Current.CustomTempDirectory));
        services.AddSingleton<IConversionEngine>(sp => new WorkerConversionEngine(
            sp.GetRequiredService<ConversionEngine>(),
            sp.GetRequiredService<LocalFileLogger>()));

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
