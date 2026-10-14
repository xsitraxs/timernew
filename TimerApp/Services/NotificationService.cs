using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace TimerApp.Services;

/// <summary>
/// Системные уведомления Windows (тосты) через AppNotificationManager.
///
/// Приложение упаковано в MSIX (см. Package.appxmanifest), поэтому у него
/// есть пакетная идентичность и регистрация уведомлений работает без
/// дополнительных COM-манифестов, которые понадобились бы unpackaged-варианту.
/// Клик по уведомлению активирует главное окно.
/// </summary>
public sealed class NotificationService
{
    private bool _registered;

    /// <summary>Пользователь щёлкнул по уведомлению.</summary>
    public event EventHandler? NotificationActivated;

    /// <summary>Поддерживаются ли уведомления на этой системе.</summary>
    public bool IsSupported
    {
        get
        {
            try
            {
                return AppNotificationManager.IsSupported();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Регистрация обработчика активации. Вызывается один раз при запуске
    /// приложения до создания главного окна.
    /// </summary>
    public void Initialize()
    {
        if (_registered || !IsSupported)
            return;

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception)
        {
            // Уведомления отключены политикой/пользователем — приложение
            // продолжит работать со звуком и внутриоконной индикацией.
            _registered = false;
        }
    }

    /// <summary>Показывает тост «Время вышло».</summary>
    public void ShowTimerFinished(string durationText)
    {
        if (!_registered)
            return;

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText("Таймер завершён")
                .AddText($"Время вышло: {durationText}.")
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception)
        {
            // Тост не показался (например, включён режим «Не беспокоить») —
            // звуковой сигнал и мигание в окне уже сработали отдельно.
        }
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        NotificationActivated?.Invoke(this, EventArgs.Empty);
    }
}
