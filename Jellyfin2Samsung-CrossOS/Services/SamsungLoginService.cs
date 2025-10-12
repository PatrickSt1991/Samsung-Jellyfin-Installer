using Avalonia.Controls;
using Jellyfin2Samsung.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace Jellyfin2Samsung.Services
{
    public class SamsungLoginService
    {
        private IWebHost _callbackServer;
        private const string CallbackUrl = "http://localhost:4794/signin/callback";
        private const string StateValue = "accountcheckdogeneratedstatetext";

        public Action<SamsungAuth> CallbackReceived;

        public static async Task<SamsungAuth> PerformSamsungLoginAsync()
        {
            string loginUrl =
                $"https://account.samsung.com/accounts/be1dce529476c1a6d407c4c7578c31bd/signInGate?locale=&clientId=v285zxnl3h&redirect_uri={HttpUtility.UrlEncode(CallbackUrl)}&state={StateValue}&tokenType=TOKEN";

            // Open the system browser
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = loginUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to open system browser.", ex);
            }

            SamsungAuth authResult = null;
            var service = new SamsungLoginService();
            await service.StartCallbackServer();

            // Wait for CallbackReceived
            var tcs = new TaskCompletionSource<SamsungAuth>();
            service.CallbackReceived += auth =>
            {
                tcs.SetResult(auth);
            };

            authResult = await tcs.Task;

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
                                    var auth = JsonConvert.DeserializeObject<SamsungAuth>(codeJson);
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
                                    await context.Response.WriteAsync($"[CallbackServer] JSON parse error: {ex.Message}");
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
}
