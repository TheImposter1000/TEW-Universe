using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace TEWUniverse.Desktop;

internal sealed class PasswordDialog : Window
{
    private readonly PasswordBox _password;
    private PasswordDialog(Window owner)
    {
        Owner = owner;
        Title = "Open TEW IX Database";
        Width = 500; Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(17, 31, 48));
        Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock {
            Text = "Database password", FontSize = 19, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 9)
        });
        panel.Children.Add(new TextBlock {
            Text = "Enter the Access database password. It is only saved if you enable Remember password in Settings.",
            Foreground = new SolidColorBrush(Color.FromRgb(174, 194, 218)),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });
        _password = new PasswordBox {
            Height = 35, FontSize = 14, Padding = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromRgb(12, 23, 37)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 95, 127))
        };
        _password.KeyDown += (_, args) => { if (args.Key == Key.Enter) DialogResult = true; };
        panel.Children.Add(_password);
        var controls = new StackPanel {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var connect = new Button { Content = "Connect", Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(22, 120, 239)), Foreground = Brushes.White };
        connect.Click += (_, _) => DialogResult = true;
        controls.Children.Add(cancel);
        controls.Children.Add(connect);
        panel.Children.Add(controls);
        Content = panel;
        Loaded += (_, _) => _password.Focus();
    }
    internal static string? Ask(Window owner)
    {
        var dialog = new PasswordDialog(owner);
        return dialog.ShowDialog() == true ? dialog._password.Password : null;
    }
}
