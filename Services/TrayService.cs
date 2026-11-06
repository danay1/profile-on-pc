using System.Drawing;
using System.Windows.Threading;
using ProfileOnPC.Models;
using Forms = System.Windows.Forms;

namespace ProfileOnPC.Services;

public enum ToastKind
{
    Info,
    Warning,
    Error
}

/// <summary>Канал уведомлений приложения (реализуется значком в трее).</summary>
public interface IUserNotifier
{
    void ShowToast(string title, string message, ToastKind kind = ToastKind.Info);
}

/// <summary>
/// Значок в системном трее: меню профилей, уведомления (Toast) и команды окна.
/// Все вызовы можно делать из любого потока — они переносятся в UI-поток.
/// </summary>
public sealed class TrayService : IUserNotifier, IDisposable
{
    private const string DefaultTitle = "ProfileOnPC";
    private const int MaxTooltipLength = 63;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private Forms.NotifyIcon? _icon;
    private readonly Forms.ToolStripMenuItem _profilesRoot;
    private readonly Forms.ToolStripMenuItem _restoreWindowsItem;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _disposed;

    public TrayService()
    {
        var menu = new Forms.ContextMenuStrip();

        var showItem = new Forms.ToolStripMenuItem("Показать окно");
        showItem.Click += (_, _) => Raise(ShowRequested);

        _profilesRoot = new Forms.ToolStripMenuItem("Активировать профиль") { Enabled = false };

        _restoreWindowsItem = new Forms.ToolStripMenuItem("Восстановить скрытые окна") { Enabled = false };
        _restoreWindowsItem.Click += (_, _) => Raise(RestoreWindowsRequested);

        var exitItem = new Forms.ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => Raise(ExitRequested);

        menu.Items.Add(showItem);
        menu.Items.Add(_profilesRoot);
        menu.Items.Add(_restoreWindowsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _menu = menu;
    }

    /// <summary>
    /// Создаёт иконку в области уведомлений. Вызывается после показа окна:
    /// к этому моменту насос сообщений уже работает.
    /// </summary>
    public void Initialize()
    {
        if (_icon is not null || _disposed)
            return;

        AppLog.Info("TrayService: создаю иконку в области уведомлений…");

        try
        {
            _icon = new Forms.NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = DefaultTitle,
                ContextMenuStrip = _menu,
                Visible = true
            };

            _icon.DoubleClick += (_, _) => Raise(ShowRequested);
            _icon.BalloonTipClicked += (_, _) => Raise(ShowRequested);

            AppLog.Info($"TrayService: иконка создана (Visible={_icon.Visible}, текст «{_icon.Text}»)");
        }
        catch (Exception exception)
        {
            AppLog.Error("TrayService: не удалось создать иконку", exception);
        }
    }


    public event Action? ShowRequested;

    public event Action? ExitRequested;

    public event Action? RestoreWindowsRequested;

    public event Action<ProfileModel>? ProfileRequested;

    /// <summary>Перестраивает подменю профилей и отмечает активный.</summary>
    public void UpdateProfiles(IEnumerable<ProfileModel> profiles, string? activeProfileId)
    {
        if (_disposed)
            return;

        Invoke(() =>
        {
            _profilesRoot.DropDownItems.Clear();

            var any = false;
            foreach (var profile in profiles)
            {
                var item = new Forms.ToolStripMenuItem(profile.Name)
                {
                    Tag = profile,
                    Checked = !string.IsNullOrEmpty(activeProfileId)
                              && string.Equals(profile.Id, activeProfileId, StringComparison.Ordinal)
                };

                item.Click += (_, _) =>
                {
                    if (item.Tag is ProfileModel selected)
                        RaiseProfile(selected);
                };

                _profilesRoot.DropDownItems.Add(item);
                any = true;
            }

            _profilesRoot.Enabled = any;
        });
    }

    public void SetActiveProfile(string? profileName)
    {
        if (_disposed || _icon is null)
            return;

        Invoke(() =>
        {
            var text = string.IsNullOrWhiteSpace(profileName)
                ? DefaultTitle
                : $"{DefaultTitle} · {profileName}";

            _icon.Text = text.Length <= MaxTooltipLength ? text : text[..(MaxTooltipLength - 1)] + "…";
        });
    }

    public void SetRestoreWindowsAvailable(bool available)
    {
        if (_disposed)
            return;

        Invoke(() => _restoreWindowsItem.Enabled = available);
    }

    /// <summary>Всплывающее уведомление в трее (Toast).</summary>
    public void ShowToast(string title, string message, ToastKind kind = ToastKind.Info)
    {
        Invoke(() =>
        {
            if (_disposed || _icon is null || !_icon.Visible)
                return;

            // Windows 11 показывает такие уведомления как обычные toast-сообщения.
            _icon.ShowBalloonTip(4000, title, message, ToToolTipIcon(kind));
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Invoke(() =>
        {
            if (_icon is null)
                return;

            _icon.Visible = false;
            _icon.Dispose();
        });
    }

    private static Forms.ToolTipIcon ToToolTipIcon(ToastKind kind) => kind switch
    {
        ToastKind.Error => Forms.ToolTipIcon.Error,
        ToastKind.Warning => Forms.ToolTipIcon.Warning,
        _ => Forms.ToolTipIcon.Info
    };

    private void Raise(Action? handler) => Invoke(() => handler?.Invoke());

    private void RaiseProfile(ProfileModel profile) => Invoke(() => ProfileRequested?.Invoke(profile));

    private void Invoke(Action action)
    {
        // NotifyIcon живёт в UI-потоке, поэтому любые вызовы переносим туда.
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.BeginInvoke(action);
    }
}
