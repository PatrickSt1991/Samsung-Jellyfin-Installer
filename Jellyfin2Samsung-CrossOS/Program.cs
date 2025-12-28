using Avalonia;
using Jellyfin2Samsung.Extensions;
using System;
using System.Diagnostics;
using System.IO;

namespace Jellyfin2Samsung
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Register trace listener BEFORE Avalonia starts
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var logFile = Path.Combine(logDir, $"debug_{timestamp}.log");

            Trace.Listeners.Add(new FileTraceListener(logFile));
            Trace.AutoFlush = true;


            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
