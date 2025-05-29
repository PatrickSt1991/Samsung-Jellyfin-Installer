using Microsoft.Extensions.DependencyInjection;
using Samsung_Jellyfin_Installer.Services;
using Samsung_Jellyfin_Installer.ViewModels;
using System.Net.Http;
using System.Windows;
using System.Configuration;
using System.Diagnostics;

namespace Samsung_Jellyfin_Installer
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Register HttpClient with custom configuration
            services.AddSingleton(provider =>
            {
                var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
                client.Timeout = TimeSpan.FromSeconds(30);
                return client;
            });

            // Register services
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<ITizenInstallerService, TizenInstallerService>();
            services.AddSingleton<INetworkService, NetworkService>();

            // Register ViewModels
            services.AddSingleton<MainWindowViewModel>();

            // Register Views
            services.AddTransient<MainWindow>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {   
            base.OnStartup(e);

            string savedLanguage = Config.Default.Language ?? "en";
            LocalizedStrings.Instance.ChangeLanguage(savedLanguage);

            try
            {
                var installer = _serviceProvider.GetRequiredService<ITizenInstallerService>();

                string CliPath = await installer.EnsureTizenCliAvailable();
                if (string.IsNullOrEmpty(CliPath))
                {
                    MessageBox.Show("Tizen tools are required for this application",
                                  "Error",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                    Shutdown();
                    return;
                }

                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application failed to start: {ex.Message}",
                              "Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
                Shutdown();
            }
        }
    }
}