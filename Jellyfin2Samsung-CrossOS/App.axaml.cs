using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Jellyfin2Samsung.Extensions;
using Jellyfin2Samsung.Helpers;
using Jellyfin2Samsung.Helpers.API;
using Jellyfin2Samsung.Helpers.Core;
using Jellyfin2Samsung.Helpers.Jellyfin;
using Jellyfin2Samsung.Helpers.Jellyfin.Plugins;
using Jellyfin2Samsung.Helpers.Tizen.Certificate;
using Jellyfin2Samsung.Helpers.Tizen.Devices;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Services;
using Jellyfin2Samsung.ViewModels;
using Jellyfin2Samsung.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Net.Http;

namespace Jellyfin2Samsung
{
    public partial class App : Application
    {
        private IServiceProvider _serviceProvider;

        public static IServiceProvider Services { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            ConfigureServices();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                DisableAvaloniaDataAnnotationValidation();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                });
            }

            RequestedThemeVariant = ThemeVariant.Light;
            base.OnFrameworkInitializationCompleted();
        }

        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            var settings = AppSettings.Load();

            // --------------------
            // Core services
            // --------------------
            services.AddSingleton(settings);
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<INetworkService, NetworkService>();
            services.AddSingleton<ITizenCertificateService, TizenCertificateService>();
            services.AddSingleton<ITizenInstallerService, TizenInstallerService>();

            // HttpClient (configured ONCE)
            services.AddSingleton(sp =>
            {
                var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };

                client.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.1");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");


                return client;
            });

            services.AddSingleton<SamsungLoginService>();
            services.AddSingleton<JellyfinApiClient>();
            services.AddSingleton<TizenApiClient>();
            services.AddSingleton<PluginManager>();
            services.AddSingleton<JellyfinWebPackagePatcher>();

            // --------------------
            // Helpers
            // --------------------
            services.AddSingleton<DeviceHelper>();
            services.AddSingleton<PackageHelper>();
            services.AddSingleton<CertificateHelper>();
            services.AddSingleton<FileHelper>();
            services.AddSingleton<ProcessHelper>();
            services.AddSingleton<TvLogService>();

            // --------------------
            // ViewModels
            // --------------------
            services.AddTransient<MainWindowViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddTransient<InstallationCompleteViewModel>();
            services.AddTransient<InstallingWindowViewModel>();
            services.AddTransient<TvLogsViewModel>();
            services.AddTransient<JellyfinConfigViewModel>();

            // --------------------
            // Views
            // --------------------
            services.AddSingleton(provider =>
            {
                var vm = provider.GetRequiredService<MainWindowViewModel>();

                var window = new MainWindow
                {
                    DataContext = vm
                };

                // IMPORTANT: prevent memory leak
                window.Closed += (_, _) =>
                {
                    if (vm is IDisposable d)
                        d.Dispose();
                };

                return window;
            });

            services.AddTransient(provider =>
            {
                var vm = provider.GetRequiredService<JellyfinConfigViewModel>();
                return new JellyfinConfigView(vm);
            });

            services.AddTransient(provider =>
            {
                var vm = provider.GetRequiredService<InstallingWindowViewModel>();
                return new InstallingWindow
                {
                    DataContext = vm
                };
            });

            services.AddTransient(provider =>
            {
                var vm = provider.GetRequiredService<InstallationCompleteViewModel>();
                return new InstallationCompleteWindow(vm);
            });

            // --------------------
            // Build provider
            // --------------------
            _serviceProvider = services.BuildServiceProvider();
            Services = _serviceProvider;

            // Localization bootstrap
            var localizationService = _serviceProvider.GetRequiredService<ILocalizationService>();
            LocalizationExtensions.SetLocalizationService(localizationService);
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators
                    .OfType<DataAnnotationsValidationPlugin>()
                    .ToArray();

            foreach (var plugin in dataValidationPluginsToRemove)
                BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
