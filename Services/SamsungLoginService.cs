using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Web.WebView2.Core;
using Samsung_Jellyfin_Installer.Models;
using Samsung_Jellyfin_Installer.Views;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Web;
using System.Windows;

public class SamsungLoginService
{
    private IWebHost _callbackServer;
    private const string CallbackUrl = "http://localhost:4794/signin/callback";
    private const string StateValue = "accountcheckdogeneratedstatetext";

    public Action<SamsungAuth> CallbackReceived;

    public static bool IsWebView2RuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch
        {
            return false;
        }
    }

    public static async Task<SamsungAuth> PerformSamsungLoginAsync()
    {
        if (!IsWebView2RuntimeAvailable())
        {
            var result = MessageBox.Show(
                "Microsoft Edge WebView2 Runtime is required. Install now?",
                "Runtime Missing",
                MessageBoxButton.YesNo);

            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                    UseShellExecute = true
                });
            }

            throw new Exception("WebView2 Runtime not installed");
        }

        string loginUrl =
            $"https://account.samsung.com/accounts/be1dce529476c1a6d407c4c7578c31bd/signInGate?locale=&clientId=v285zxnl3h&redirect_uri={HttpUtility.UrlEncode(CallbackUrl)}&state={StateValue}&tokenType=TOKEN";

        SamsungAuth authResult = null;
        SamsungLoginWindow loginWindow = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            loginWindow = new SamsungLoginWindow(CallbackUrl, StateValue);
            loginWindow.StartLogin(loginUrl);
        });

        var service = new SamsungLoginService();
        await service.StartCallbackServer();

        service.CallbackReceived = auth =>
        {
            authResult = auth;
            loginWindow?.OnExternalCallback(auth.state, auth.access_token);
        };

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            loginWindow.ShowDialog();
        });

        await service.StopCallbackServer();

        return authResult;
    }

    public async Task StartCallbackServer()
    {
        _callbackServer = new WebHostBuilder()
            .UseKestrel()
            .UseUrls("http://localhost:4794")
            .Configure(app =>
            {
                app.Run(async context =>
                {
                    if (context.Request.Path == "/signin/callback" && context.Request.Method == "POST")
                    {
                        var form = await context.Request.ReadFormAsync();
                        var state = form["state"];
                        var codeJson = form["code"];

                        if (!string.IsNullOrEmpty(codeJson))
                        {
                            try
                            {
                                var auth = JsonSerializer.Deserialize<SamsungAuth>(codeJson);
                                if (auth != null)
                                {
                                    auth.state = state; // Inject state manually

                                    CallbackReceived?.Invoke(auth);

                                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                                    await context.Response.WriteAsync("Login successful. You can close this window.");
                                    return;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[CallbackServer] JSON parse error: {ex.Message}");
                            }
                        }

                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        await context.Response.WriteAsync("Invalid login response.");
                    }
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        await context.Response.WriteAsync("Not Found");
                    }
                });
            })
            .Build();

        await _callbackServer.StartAsync();
    }

    public async Task StopCallbackServer()
    {
        if (_callbackServer != null)
        {
            await _callbackServer.StopAsync();
            _callbackServer.Dispose();
            _callbackServer = null;
        }
    }
}
