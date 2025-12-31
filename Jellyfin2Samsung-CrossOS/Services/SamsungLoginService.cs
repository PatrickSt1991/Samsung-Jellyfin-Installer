using Avalonia.Controls;
using Jellyfin2Samsung.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Jellyfin2Samsung.Services
{
    public class SamsungLoginService
    {
        private IWebHost _callbackServer;
        private const string LoopbackHost = "localhost";
        private const int FixedPort = 4794;

        private string _callbackUrl => $"http://{LoopbackHost}:{FixedPort}/signin/callback";

        public Action<SamsungAuth> CallbackReceived;

        public static Task<SamsungAuth> PerformSamsungLoginAsync()
        {
            return PerformSamsungLoginAsync(CancellationToken.None);
        }

        public static async Task<SamsungAuth> PerformSamsungLoginAsync(CancellationToken cancellationToken)
        {
            var service = new SamsungLoginService();

            var tcs = new TaskCompletionSource<SamsungAuth>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            service.CallbackReceived += auth =>
            {
                tcs.TrySetResult(auth);
            };

            cancellationToken.Register(() =>
            {
                tcs.TrySetCanceled(cancellationToken);
            });

            await service.StartCallbackServer();

            string loginUrl =
                $"https://account.samsung.com/accounts/be1dce529476c1a6d407c4c7578c31bd/signInGate" +
                $"?locale=&clientId=v285zxnl3h" +
                $"&redirect_uri={HttpUtility.UrlEncode(service._callbackUrl)}" +
                $"&state=accountcheckdogeneratedstatetext" +
                $"&tokenType=TOKEN";

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

            try
            {
                return await tcs.Task;
            }
            finally
            {
                await service.StopCallbackServer();
            }
        }

        public async Task StartCallbackServer()
        {
            _callbackServer = new WebHostBuilder()
                .UseKestrel()
                .UseUrls($"http://{LoopbackHost}:{FixedPort}")
                .Configure(app =>
                {
                    app.Run(async context =>
                    {
                        if (context.Request.Path == "/signin/callback" && context.Request.Method == "POST")
                        {
                            string body = await new StreamReader(context.Request.Body).ReadToEndAsync();

                            string state = null;
                            string codeEncoded = null;

                            var parts = body.Split('&', StringSplitOptions.RemoveEmptyEntries);

                            foreach (var part in parts)
                            {
                                var kv = part.Split('=', 2);
                                if (kv.Length != 2) continue;

                                if (kv[0] == "state")
                                    state = WebUtility.UrlDecode(kv[1]);

                                if (kv[0] == "code")
                                    codeEncoded = Uri.UnescapeDataString(kv[1]);
                            }

                            if (string.IsNullOrWhiteSpace(codeEncoded))
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                                await context.Response.WriteAsync("Invalid login response (missing code).");
                                return;
                            }

                            try
                            {
                                var auth = JsonConvert.DeserializeObject<SamsungAuth>(codeEncoded);
                                if (auth != null)
                                {
                                    auth.state = state;
                                    CallbackReceived?.Invoke(auth);

                                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                                    await context.Response.WriteAsync("Login successful. You can close this window.");
                                    return;
                                }
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                                await context.Response.WriteAsync(
                                    $"[CallbackServer] JSON parse error: {ex}\n\nDecoded JSON:\n{codeEncoded}");
                                return;
                            }

                            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                            await context.Response.WriteAsync("Invalid login response.");
                            return;
                        }

                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        await context.Response.WriteAsync("Not Found");
                    });
                })
                .Build();

            await _callbackServer.StartAsync();

            System.Diagnostics.Trace.WriteLine(
                $"[SamsungLoginService] Bound to http://{LoopbackHost}:{FixedPort}");
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