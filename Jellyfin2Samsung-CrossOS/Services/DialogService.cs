using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Services
{
    public class DialogService : IDialogService
    {
        private Window? GetMainWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;

            return null;
        }

        private Window CreateStyledDialog(
            string title,
            Control content,
            bool showButtons = false,
            TaskCompletionSource<bool>? tcs = null,
            string yesText = "Yes",
            string noText = "No")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 250,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.White,
                CornerRadius = new CornerRadius(12)
            };

            var mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                Margin = new Thickness(20)
            };

            mainPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            mainPanel.Children.Add(content);

            if (showButtons && tcs != null)
            {
                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10,
                    Margin = new Thickness(0, 15, 0, 0)
                };

                var yesButton = new Button
                {
                    Content = yesText,
                    Width = 90,
                    Height = 35,
                    Background = new SolidColorBrush(Color.Parse("#2563eb")),
                    Foreground = Brushes.White,
                    CornerRadius = new CornerRadius(8),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                var noButton = new Button
                {
                    Content = noText,
                    Width = 90,
                    Height = 35,
                    Background = new SolidColorBrush(Color.Parse("#9ca3af")),
                    Foreground = Brushes.White,
                    CornerRadius = new CornerRadius(8),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                yesButton.Click += (_, _) => { tcs.SetResult(true); dialog.Close(); };
                noButton.Click += (_, _) => { tcs.SetResult(false); dialog.Close(); };

                buttons.Children.Add(yesButton);
                buttons.Children.Add(noButton);

                mainPanel.Children.Add(buttons);
            }

            dialog.Content = mainPanel;
            return dialog;
        }

        public async Task ShowMessageAsync(string message)
        {
            var window = GetMainWindow();
            var dialog = CreateStyledDialog("Message", new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Margin = new Thickness(0, 5, 0, 0)
            });

            if (window != null)
                await dialog.ShowDialog(window);
        }

        public async Task ShowErrorAsync(string message)
        {
            var window = GetMainWindow();
            var dialog = CreateStyledDialog("Error", new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Red,
                FontSize = 14,
                Margin = new Thickness(0, 5, 0, 0)
            });

            if (window != null)
                await dialog.ShowDialog(window);
        }

        public async Task<bool> ShowConfirmationAsync(string title, string message, string yesText = "Yes", string noText = "No")
        {
            var window = GetMainWindow();
            var tcs = new TaskCompletionSource<bool>();

            var dialog = CreateStyledDialog(title, new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Margin = new Thickness(0, 5, 0, 0)
            }, showButtons: true, tcs: tcs, yesText: yesText, noText: noText);

            if (window != null)
                await dialog.ShowDialog(window);

            return await tcs.Task;
        }

        public async Task<string?> PromptForIpAsync()
        {
            var window = GetMainWindow();
            var dialog = new IpInputDialog();

            if (window != null)
                return await dialog.ShowDialog<string?>(window);

            return null;
        }
    }
}
