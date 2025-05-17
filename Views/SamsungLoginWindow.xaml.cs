using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class SamsungLoginWindow : Window
    {
        public string AuthorizationCode { get; private set; }
        public string State { get; private set; }
        private readonly string _callbackUrl;
        private readonly string _stateValue;

        public SamsungLoginWindow(string callbackUrl, string stateValue)
        {
            _callbackUrl = callbackUrl;
            _stateValue = stateValue;

            InitializeComponent();
            InitializeWebView2Async();
        }

        private async void InitializeWebView2Async()
        {
            // Initialize WebView2 environment
            var env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);

            // Handle navigation events
            webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;
        }

        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var currentUrl = webView.Source?.ToString();
            if (currentUrl?.StartsWith(_callbackUrl) == true)
            {
                var uri = new Uri(currentUrl);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

                var state = query["state"];
                var code = query["code"];

                if (state == _stateValue && !string.IsNullOrEmpty(code))
                {
                    Dispatcher.Invoke(() =>
                    {
                        State = state;
                        AuthorizationCode = code;
                        DialogResult = true;
                        Close();
                    });
                }
            }
        }

        public void StartLogin(string loginUrl)
        {
            webView.Source = new Uri(loginUrl);
        }
    }
}
