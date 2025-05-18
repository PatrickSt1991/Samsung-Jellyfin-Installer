using Microsoft.Web.WebView2.Core;
using System;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class SamsungLoginWindow : Window
    {
        public Action<string, string> CallbackReceived;

        public SamsungLoginWindow(string callbackUrl, string stateValue)
        {
            InitializeComponent();
            InitializeWebView2Async();
        }

        private async void InitializeWebView2Async()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync();
                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                ShowLoading(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            ShowLoading(false);
        }

        public void StartLogin(string loginUrl)
        {
            if (webView.CoreWebView2 != null)
            {
                webView.Source = new Uri(loginUrl);
            }
            else
            {
                webView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        webView.Source = new Uri(loginUrl);
                    }
                    else
                    {
                        MessageBox.Show("WebView2 failed to initialize.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
            }
        }

        public void OnExternalCallback(string state, string code)
        {
            Dispatcher.Invoke(() =>
            {
                CallbackReceived?.Invoke(state, code);
                DialogResult = true;
                Close();
            });
        }

        private void ShowLoading(bool isLoading)
        {
            loadingSpinner.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            webView.Visibility = isLoading ? Visibility.Hidden : Visibility.Visible;
        }
    }
}
