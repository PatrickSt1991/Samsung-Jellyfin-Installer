using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Jellyfin2SamsungCrossOS.Extensions;
using Jellyfin2SamsungCrossOS.Helpers;
using Jellyfin2SamsungCrossOS.Services;
using Jellyfin2SamsungCrossOS.ViewModels;
using Jellyfin2SamsungCrossOS.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Net.Http;

namespace Jellyfin2SamsungCrossOS
{
    public partial class App : Application
    {
        private IServiceProvider _serviceProvider;

        // Static property to access services from anywhere
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
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Services
            services.AddSingleton<AppSettings>(AppSettings.Default);
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<INetworkService, NetworkService>();
            services.AddSingleton<ITizenCertificateService, TizenCertificateService>();
            services.AddSingleton<ITizenInstallerService, TizenInstallerService>();
            services.AddSingleton<SamsungLoginService>();
            services.AddSingleton<HttpClient>();

            services.AddSingleton<DeviceHelper>();
            services.AddSingleton<PackageHelper>();
            services.AddSingleton<JellyfinHelper>();
            services.AddSingleton<CertificateHelper>();
            services.AddSingleton<FileHelper>();
            services.AddSingleton<OperatingSystemHelper>();
            services.AddSingleton<ProcessHelper>();


            // ViewModels
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddTransient<InstallationCompleteViewModel>();
            services.AddTransient<InstallingWindowViewModel>();

            // JellyfinConfigViewModel requires JellyfinHelper
            services.AddTransient<JellyfinConfigViewModel>(provider =>
            {
                var helper = provider.GetRequiredService<JellyfinHelper>();
                var localization = provider.GetRequiredService<ILocalizationService>();
                return new JellyfinConfigViewModel(helper, localization);
            });

            // Views
            services.AddSingleton<MainWindow>(provider =>
            {
                return new MainWindow
                {
                    DataContext = provider.GetRequiredService<MainWindowViewModel>()
                };
            });

            services.AddTransient<JellyfinConfigView>(provider =>
            {
                var vm = provider.GetRequiredService<JellyfinConfigViewModel>();
                return new JellyfinConfigView(vm);
            });

            services.AddTransient<InstallingWindow>(provider =>
            {
                var vm = provider.GetRequiredService<InstallingWindowViewModel>();
                return new InstallingWindow
                {
                    DataContext = vm
                };
            });

            services.AddTransient<InstallationCompleteWindow>(provider =>
            {
                var vm = provider.GetRequiredService<InstallationCompleteViewModel>();
                return new InstallationCompleteWindow(vm);
            });

            // Build and assign service provider
            _serviceProvider = services.BuildServiceProvider();
            Services = _serviceProvider;

            // Set localization service globally
            var localizationService = _serviceProvider.GetRequiredService<ILocalizationService>();
            LocalizationExtensions.SetLocalizationService(localizationService);
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            foreach (var plugin in dataValidationPluginsToRemove)
                BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
