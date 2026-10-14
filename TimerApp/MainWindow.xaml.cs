using System;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TimerApp.Helpers;
using TimerApp.Models;
using TimerApp.Services;
using TimerApp.ViewModels;
using Windows.Graphics;
using Windows.System;

namespace TimerApp;

/// <summary>
/// Главное окно: Mica-фон Windows 11, кастомный TitleBar, тёмная тема
/// по умолчанию, компактный режим, горячие клавиши и активация окна
/// по окончании таймера / клику на уведомление.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly TimerService _timerService;
    private readonly SoundService _soundService;
    private bool _isCompact;
    private SizeInt32 _sizeBeforeMini;
    private bool _customPresetClickSuppressed;

    public MainWindow()
    {
        InitializeComponent();

        _settingsService = new SettingsService();
        var settings = _settingsService.Load();

        _timerService = new TimerService();
        _soundService = new SoundService();

        ViewModel = new TimerViewModel(_timerService, _soundService, _settingsService, settings);
        RootGrid.DataContext = ViewModel;
        ViewModel.TimerFinished += OnTimerFinished;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ConfigureWindow();
        ApplyTheme(ViewModel.IsDarkTheme);

        RootGrid.Loaded += (_, _) => RootGrid.Focus(FocusState.Programmatic);
        Closed += OnClosed;
    }

    public TimerViewModel ViewModel { get; }

    // ------------------------------------------------------------- окно / тема

    private void ConfigureWindow()
    {
        Title = "Таймер";

        // Фон Mica в стиле Windows 11 (при недоступности просто тема окна).
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception)
        {
            SystemBackdrop = null;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;


        // Размер по умолчанию и центрирование на экране.
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        int width = 600;
        int height = 840;
        int x = area.WorkArea.X + (area.WorkArea.Width - width) / 2;
        int y = area.WorkArea.Y + Math.Max(0, (area.WorkArea.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void ApplyTheme(bool isDark)
    {
        RootGrid.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        AppWindow.TitleBar.ButtonForegroundColor =
            isDark ? Colors.White : Colors.Black;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimerViewModel.IsDarkTheme))
            ApplyTheme(ViewModel.IsDarkTheme);

        // Как только состояние ушло из Finished (старт/сброс/правки) — стоп миганию.
        if (e.PropertyName == nameof(TimerViewModel.IsFinished) && !ViewModel.IsFinished)
            StopFinishedAnimation();
    }

    private void ToggleThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsDarkTheme = !ViewModel.IsDarkTheme;
    }

    private void CompactButton_Click(object sender, RoutedEventArgs e)
    {
        SetMiniMode(!_isCompact);
    }

    /// <summary>
    /// Мини-режим: маленькое окно поверх всех окон. Остаются только кольцо,
    /// цифры, статус и кнопки управления — секции настроек прячутся.
    /// Повторное нажатие возвращает прежний размер и обычную компоновку.
    /// </summary>
    private void SetMiniMode(bool mini)
    {
        _isCompact = mini;

        var sectionVisibility = mini ? Visibility.Collapsed : Visibility.Visible;
        InputSection.Visibility = sectionVisibility;
        PresetsSection.Visibility = sectionVisibility;
        SoundSection.Visibility = sectionVisibility;
        HotkeysHint.Visibility = sectionVisibility;
        MuteTitleButton.Visibility = sectionVisibility;
        ThemeTitleButton.Visibility = sectionVisibility;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = mini;

        if (mini)
        {
            _sizeBeforeMini = AppWindow.Size;
            AppWindow.Resize(new SizeInt32(400, 460));
            ToolTipService.SetToolTip(CompactButton, "Обычный режим окна");
        }
        else
        {
            var restore = _sizeBeforeMini.Width >= 480 && _sizeBeforeMini.Height >= 600
                ? _sizeBeforeMini
                : new SizeInt32(600, 840);
            AppWindow.Resize(restore);
            ToolTipService.SetToolTip(CompactButton, "Мини-окно поверх всех окон");
        }
    }

    // ------------------------------------------------------------ финиш таймера

    private void OnTimerFinished(object? sender, EventArgs e)
    {
        // Системное уведомление Windows.
        string durationText = TimeFormatter.FormatFriendly(_timerService.Duration);
        if (Application.Current is App app)
            app.Notifications.ShowTimerFinished(durationText);

        // Если окно свёрнуто или не в фокусе — показать его пользователю.
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        if (!AppWindow.IsVisible)
            AppWindow.Show();

        // Поднимаем окно поверх остальных: таймер для того и звенел.
        Activate();

        // Мигание крупных цифр до сброса/нового старта.
        if (RootGrid.Resources["FinishedStoryboard"] is Storyboard storyboard)
            storyboard.Begin();
    }

    /// <summary>Активация окна по клику на тост (потокобезопасно).</summary>
    public void ActivateFromNotification()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }

            Activate();
        });
    }

    private void StopFinishedAnimation()
    {
        if (RootGrid.Resources["FinishedStoryboard"] is Storyboard storyboard)
            storyboard.Stop();

        TimeDisplayText.Opacity = 1.0;
    }

    // ------------------------------------------------------------ свои пресеты

    private void CustomPresetsGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_customPresetClickSuppressed)
        {
            _customPresetClickSuppressed = false;
            return;
        }

        if (e.ClickedItem is CustomPreset preset)
        {
            StopFinishedAnimation();
            ViewModel.ApplyCustomPresetCommand.Execute(preset);
        }
    }

    private void ApplyCustomPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is CustomPreset preset)
        {
            StopFinishedAnimation();
            ViewModel.ApplyCustomPresetCommand.Execute(preset);
        }
    }

    private void DeleteCustomPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not CustomPreset preset)
        {
            return;
        }

        // Клик по кнопке удаления внутри карточки не должен применять пресет.
        _customPresetClickSuppressed = true;
        ViewModel.DeleteCustomPresetCommand.Execute(preset);
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => _customPresetClickSuppressed = false);
    }

    private async void AddCustomPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            Header = "Название",
            PlaceholderText = "Например, «Чай»",
            MaxLength = 40
        };

        var minutesBox = new NumberBox
        {
            Header = "Минуты",
            Minimum = 0,
            Maximum = 23 * 60 + 59,
            Value = ViewModel.HoursInput * 60 + ViewModel.MinutesInput,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        var secondsBox = new NumberBox
        {
            Header = "Секунды",
            Minimum = 0,
            Maximum = 59,
            Value = ViewModel.SecondsInput,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(nameBox);
        panel.Children.Add(minutesBox);
        panel.Children.Add(secondsBox);

        var dialog = new ContentDialog
        {
            Title = "Свой пресет",
            Content = panel,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        int totalSeconds = (int)(minutesBox.Value * 60 + secondsBox.Value);
        if (totalSeconds <= 0)
            return;

        ViewModel.AddCustomPreset(nameBox.Text, totalSeconds);
    }

    // ----------------------------------------------------------- горячие клавиши

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Не перехватываем клавиши, когда пользователь печатает в полях ввода
        // или когда фокус на кнопке (пробел тогда нажмёт саму кнопку —
        // это стандартное поведение Windows).
        if (e.OriginalSource is TextBox or NumberBox)
            return;

        switch (e.Key)
        {
            case VirtualKey.Space:
                if (e.OriginalSource is Button)
                    return; // пробел нажмёт сфокусированную кнопку сам
                StopFinishedAnimation();
                ViewModel.ToggleStartPauseCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                if (e.OriginalSource is Button)
                    return; // Enter нажмёт саму кнопку, старт не дублируем
                if (ViewModel.IsIdle || ViewModel.IsFinished)
                {
                    StopFinishedAnimation();
                    ViewModel.StartCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            case VirtualKey.Escape:
                StopFinishedAnimation();
                ViewModel.ResetCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ---------------------------------------------------------------- закрытие

    private void OnClosed(object sender, WindowEventArgs args)
    {
        StopFinishedAnimation();
        ViewModel.PersistAll();
        ViewModel.TimerFinished -= OnTimerFinished;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _timerService.Dispose();
        _soundService.Dispose();
    }
}
