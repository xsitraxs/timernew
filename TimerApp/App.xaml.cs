using System;
using Microsoft.UI.Xaml;
using TimerApp.Services;

namespace TimerApp;

/// <summary>
/// Точка входа. Регистрирует уведомления Windows до создания окна,
/// чтобы клик по тосту мог активировать главное окно.
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>Главное окно (нужно для активации по клику на уведомление).</summary>
    public static MainWindow? MainWindow { get; private set; }

    /// <summary>Общий сервис уведомлений приложения.</summary>
    public NotificationService Notifications { get; } = new();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Notifications.Initialize();
        Notifications.NotificationActivated += OnNotificationActivated;

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private void OnNotificationActivated(object? sender, EventArgs e)
    {
        // Событие может прийти не из UI-потока, если уведомление щёлкнули
        // при закрытом/фоновом процессе, поэтому активацию выполняет само окно
        // через свой DispatcherQueue.
        MainWindow?.ActivateFromNotification();
    }
}
