using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Views
{
    public partial class HiddenWebViewWindow : Window
    {
        private TaskCompletionSource<string> _tcs;
        private string _html;
        private EventHandler<CoreWebView2WebMessageReceivedEventArgs> _handler;

        public HiddenWebViewWindow()
        {
            InitializeComponent();
        }

        public async Task<string> SolveCaptchaAsync(string html)
        {
            _tcs = new TaskCompletionSource<string>();
            _html = html;

            this.Show(); // Show invisibly

            return await _tcs.Task;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync();
                await webView.EnsureCoreWebView2Async(env);

                _handler = (s, args) =>
                {
                    string msg = args.TryGetWebMessageAsString();
                    if (msg.StartsWith("ERROR:"))
                        _tcs.TrySetException(new Exception(msg.Substring(6)));
                    else
                        _tcs.TrySetResult(msg);

                    CleanupAndClose();
                };

                webView.CoreWebView2.WebMessageReceived += _handler;
                webView.CoreWebView2.NavigateToString(_html);
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
                CleanupAndClose();
            }
        }

        private void CleanupAndClose()
        {
            if (webView?.CoreWebView2 != null && _handler != null)
            {
                webView.CoreWebView2.WebMessageReceived -= _handler;
            }

            this.Close();
        }
    }
}
