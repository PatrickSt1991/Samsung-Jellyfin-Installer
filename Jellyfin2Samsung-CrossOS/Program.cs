using System;
using Avalonia;

namespace Jellyfin2SamsungCrossOS
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args) => 
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
        {
            bool isMacIntel =
                OperatingSystem.IsMacOS() &&
                RuntimeInformation.ProcessArchitecture == Architecture.X64;

            return AppBuilder
                .Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .With(new SkiaOptions
                {
                    UseGpu = !isMacIntel
                })
                .LogToTrace();
        }
    }
}
