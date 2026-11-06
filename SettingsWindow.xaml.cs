using System.Windows;
using ProfileOnPC.Helpers;
using ProfileOnPC.Models;
using ProfileOnPC.Services;

namespace ProfileOnPC;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;
    private bool _initialized;

    public AppSettings Settings { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _original = settings.Clone();
        Settings = settings.Clone();

        ComboBoxHelper.SelectByTag(ThemeBox, Settings.Theme);
        ComboBoxHelper.SelectByTag(AccentBox, Settings.AccentColor);
        AutorunCheck.IsChecked = Settings.StartWithWindows;
        ComboBoxHelper.SelectByTag(CloseActionBox, Settings.CloseAction);
        ApplyOnStartupCheck.IsChecked = Settings.ApplyProfileOnStartup;
        HotkeysCheck.IsChecked = Settings.GlobalHotkeysEnabled;
        TriggersCheck.IsChecked = Settings.ProcessTriggersEnabled;
        ToastCheck.IsChecked = Settings.ActivationToastEnabled;
        _initialized = true;

        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this, Settings.Theme);
    }

    private void Appearance_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_initialized)
            return;

        ReadControls();
        ThemeService.Apply(Settings.Theme, Settings.AccentColor);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        ReadControls();
        ThemeService.Apply(Settings.Theme, Settings.AccentColor);
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        ThemeService.Apply(_original.Theme, _original.AccentColor);
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DialogResult != true)
            ThemeService.Apply(_original.Theme, _original.AccentColor);
        base.OnClosed(e);
    }

    private void ReadControls()
    {
        Settings.Theme = ComboBoxHelper.GetTag(ThemeBox, "Dark");
        Settings.AccentColor = ComboBoxHelper.GetTag(AccentBox, "#2563A6");
        Settings.StartWithWindows = AutorunCheck.IsChecked == true;
        Settings.CloseAction = ComboBoxHelper.GetTag(CloseActionBox, CloseActions.Tray);
        Settings.MinimizeToTray = Settings.CloseAction == CloseActions.Tray;
        Settings.ApplyProfileOnStartup = ApplyOnStartupCheck.IsChecked == true;
        Settings.GlobalHotkeysEnabled = HotkeysCheck.IsChecked == true;
        Settings.ProcessTriggersEnabled = TriggersCheck.IsChecked == true;
        Settings.ActivationToastEnabled = ToastCheck.IsChecked == true;
    }
}
