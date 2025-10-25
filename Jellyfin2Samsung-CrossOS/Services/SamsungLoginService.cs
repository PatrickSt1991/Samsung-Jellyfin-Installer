﻿using Avalonia.Controls;
using Jellyfin2Samsung.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace Jellyfin2Samsung.Services
{
    public class SamsungLoginService
    {
        private IWebHost _callbackServer;
        private const string LoopbackIp = "127.0.0.1";
        private const string StateValue = "accountcheckdogeneratedstatetext";
        private int _boundPort;
        private string _boundCallbackUrl => _boundPort > 0
            ? $"http://{LoopbackIp}:{_boundPort}/signin/callback"
            : throw new InvalidOperationException("Callback server not started.");

        public Action<SamsungAuth> CallbackReceived;

        public static async Task<SamsungAuth> PerformSamsungLoginAsync()
        {
            var service = new SamsungLoginService();
            var tcs = new TaskCompletionSource<SamsungAuth>();

            service.CallbackReceived += auth =>
            {
                tcs.TrySetResult(auth);
            };

            await service.StartCallbackServer();

            string loginUrl =
                $"https://account.samsung.com/accounts/be1dce529476c1a6d407c4c7578c31bd/signInGate?locale=&clientId=v285zxnl3h&redirect_uri={HttpUtility.UrlEncode(service._boundCallbackUrl)}&state={StateValue}&tokenType=TOKEN";

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
                await service.StopCallbackServer();
                throw new InvalidOperationException("Failed to open system browser.", ex);
            }

            var authResult = await tcs.Task;

            await service.StopCallbackServer();
            return authResult;
        }


        public async Task StartCallbackServer()
        {
            _callbackServer = new WebHostBuilder()
                .UseKestrel()
                .UseUrls($"http://{LoopbackIp}:0")
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

            var addressFeature = _callbackServer.ServerFeatures.Get<IServerAddressesFeature>();
            var address = addressFeature?.Addresses.FirstOrDefault();

            if (!string.IsNullOrEmpty(address))
            {
                var uri = new Uri(address);
                _boundPort = uri.Port;
                System.Diagnostics.Debug.WriteLine($"[SamsungLoginService] Bound successfully to IP {LoopbackIp} on port {_boundPort}");
            }
            else
            {
                await StopCallbackServer();
                throw new InvalidOperationException("Failed to determine bound port after Kestrel start.");
            }
        }

        public async Task StopCallbackServer()
        {
            if (_callbackServer != null)
            {
                await _callbackServer.StopAsync();
                _callbackServer.Dispose();
                _callbackServer = null;
            }

            _boundPort = 0;
        }
    }
}
