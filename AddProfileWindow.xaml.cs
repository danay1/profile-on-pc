using System.Windows;
using System.Windows.Input;

namespace ProfileOnPC;

public partial class AddProfileWindow : Window
{
    public string ProfileName => NameBox.Text.Trim();

    public AddProfileWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Create_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            MessageBox.Show(
                this,
                "Введите имя профиля.",
                "ProfileOnPC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void NameBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Create_OnClick(sender, e);
    }
}
