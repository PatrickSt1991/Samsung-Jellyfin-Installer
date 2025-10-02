using System;
using Avalonia;
using Avalonia.Media;

namespace Jellyfin2SamsungCrossOS
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args) => 
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .With(new FontManagerOptions
                {
                    DefaultFamilyName = "Inter"
                })
                .LogToTrace();

            return builder;
        }
    }
}
