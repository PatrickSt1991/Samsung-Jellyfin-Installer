using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls.Documents;
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

        private static IBrush B(string key, IBrush fb) {
            var r = Application.Current?.FindResource(key);
            if (r is IBrush b) return b;
            if (r is Color c) return new SolidColorBrush(c);
            return fb;
        }

        private Window CreateStyledDialog(
            string title, Control content, bool showButtons = false,
            TaskCompletionSource<bool>? tcs = null, string yesText = "Yes", string noText = "No")
        {
            var bg     = B("ThemeBackgroundBrush", Brushes.White);
            var fg     = B("ThemeForegroundBrush", Brushes.Black);
            var border = B("ThemeBorderBrush",    new SolidColorBrush(Color.Parse("#E0E0E0")));
            var accent = B("SystemAccentColor",   new SolidColorBrush(Color.Parse("#2563eb")));
            var accent1= B("SystemAccentColorDark1", accent);
            var accent2= B("SystemAccentColorDark2", accent1);

            var dlg = new Window {
                Title = title,
                Width = 420, Height = 250,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                RequestedThemeVariant = ThemeVariant.Default,
                Background = bg,
                CornerRadius = new CornerRadius(12)
            };

            var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Margin = new Thickness(20) };
            root.SetValue(TextElement.ForegroundProperty, fg);

            root.Children.Add(new TextBlock {
                Text = title, FontSize = 18, FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,10)
            });

            root.Children.Add(content);

            if (showButtons && tcs != null)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10, Margin = new Thickness(0,15,0,0) };

                var yesBtn = new Button { Content = yesText, Width = 90, Height = 35, Background = accent, Foreground = Brushes.White,
                                          CornerRadius = new CornerRadius(8) };
                // style via pseudo-classes, not events
                yesBtn.Classes.Add("dialog-primary");

                var noBtn  = new Button { Content = noText, Width = 90, Height = 35, Background = bg, Foreground = fg,
                                          BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };

                yesBtn.Click += (_,__) => { tcs.SetResult(true);  dlg.Close(); };
                noBtn.Click  += (_,__) => { tcs.SetResult(false); dlg.Close(); };

                row.Children.Add(yesBtn);
                row.Children.Add(noBtn);
                root.Children.Add(row);

                // Local style so hover works without code-behind:
                root.Styles.Add(new Style(x => x.OfType<Button>().Class("dialog-primary")) {
                    Setters = {
                        new Setter(Button.BackgroundProperty, accent),
                        new Setter(Button.ForegroundProperty, Brushes.White)
                    }
                });
                root.Styles.Add(new Style(x => x.OfType<Button>().Class("dialog-primary").PointerOver()) {
                    Setters = { new Setter(Button.BackgroundProperty, accent1) }
                });
                root.Styles.Add(new Style(x => x.OfType<Button>().Class("dialog-primary").Pressed()) {
                    Setters = { new Setter(Button.BackgroundProperty, accent2) }
                });
            }

            dlg.Content = root;
            return dlg;
        }

        public async Task ShowMessageAsync(string title, string message)
        {
            var w = GetMainWindow();
            var dlg = CreateStyledDialog(title, new TextBlock {
                Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0,5,0,0)
            });
            if (w != null) await dlg.ShowDialog(w);
        }

        public async Task ShowErrorAsync(string message)
        {
            var w = GetMainWindow();
            var dlg = CreateStyledDialog("Error", new TextBlock {
                Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Red, FontSize = 14, Margin = new Thickness(0,5,0,0)
            });
            if (w != null) await dlg.ShowDialog(w);
        }

        public async Task<bool> ShowConfirmationAsync(string title, string message, string yesText="Yes", string noText="No")
        {
            var w = GetMainWindow();
            var tcs = new TaskCompletionSource<bool>();
            var dlg = CreateStyledDialog(title, new TextBlock {
                Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0,5,0,0)
            }, showButtons: true, tcs: tcs, yesText: yesText, noText: noText);
            if (w != null) await dlg.ShowDialog(w);
            return await tcs.Task;
        }

        public async Task<string?> PromptForIpAsync()
        {
            var w = GetMainWindow();
            var dlg = new IpInputDialog();
            if (w != null) return await dlg.ShowDialog<string?>(w);
            return null;
        }
    }
}
