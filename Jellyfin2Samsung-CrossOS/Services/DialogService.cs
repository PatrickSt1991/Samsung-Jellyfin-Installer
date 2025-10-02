using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;                 // ThemeVariant, FindResource keys
using Avalonia.Controls.Documents;      // TextElement (attached Foreground)
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Services
{
    public class DialogService : IDialogService
    {
        private Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;

            return null;
        }

        private static IBrush GetThemeBrush(string key, IBrush fallback)
        {
            var obj = Application.Current?.FindResource(key);
            if (obj is IBrush b) return b;
            if (obj is Color c) return new SolidColorBrush(c);
            return fallback;
        }

        private Window CreateStyledDialog(
            string title,
            Control content,
            bool showButtons = false,
            TaskCompletionSource<bool>? tcs = null,
            string yesText = "Yes",
            string noText = "No")
        {
            // Resolve theme brushes (work in both Light/Dark)
            var bg       = GetThemeBrush("ThemeBackgroundBrush", Brushes.White);
            var fg       = GetThemeBrush("ThemeForegroundBrush", Brushes.Black);
            var border   = GetThemeBrush("ThemeBorderBrush", new SolidColorBrush(Color.Parse("#E0E0E0")));
            // Accent for primary button (fallback to a readable blue)
            var accent   = GetThemeBrush("SystemAccentColor", new SolidColorBrush(Color.Parse("#2563eb")));
            var accentH  = GetThemeBrush("SystemAccentColorDark1", new SolidColorBrush(Color.Parse("#1e54c6")));

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 250,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                RequestedThemeVariant = ThemeVariant.Default, // follow app/OS theme
                Background = bg,
                CornerRadius = new CornerRadius(12)
            };

            var mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                Margin = new Thickness(20)
            };

            // Apply themed foreground to all text inside the dialog
            mainPanel.SetValue(TextElement.ForegroundProperty, fg);

            // Title inside the dialog content (not the window chrome)
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
                    Background = accent,
                    Foreground = Brushes.White,              // good contrast over accent
                    CornerRadius = new CornerRadius(8),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                yesButton.PointerEnter += (_, __) => yesButton.Background = accentH;
                yesButton.PointerLeave += (_, __) => yesButton.Background = accent;

                var noButton = new Button
                {
                    Content = noText,
                    Width = 90,
                    Height = 35,
                    Background = bg,                         // match dialog background
                    Foreground = fg,
                    BorderBrush = border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                yesButton.Click += (_, _) => { tcs.SetResult(true); dialog.Close(); };
                noButton.Click  += (_, _) => { tcs.SetResult(false); dialog.Close(); };

                buttons.Children.Add(yesButton);
                buttons.Children.Add(noButton);

                mainPanel.Children.Add(buttons);
            }

            dialog.Content = mainPanel;
            return dialog;
        }

        public async Task ShowMessageAsync(string title, string message)
        {
            var window = GetMainWindow();
            var dialog = CreateStyledDialog(title, new TextBlock
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
                Foreground = Brushes.Red, // stays readable on both themes
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
