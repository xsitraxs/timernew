using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using TimerApp.Helpers;
using TimerApp.Models;
using TimerApp.Services;

namespace TimerApp.ViewModels;

/// <summary>
/// ViewModel главного окна: состояние, ввод длительности, пресеты,
/// громкость/тема и команды кнопок и горячих клавиш.
/// </summary>
public sealed class TimerViewModel : ObservableObject
{
    private readonly TimerService _timer;
    private readonly SoundService _sound;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;

    private double _hoursInput;
    private double _minutesInput;
    private double _secondsInput;
    private TimeSpan _remaining;
    private double _progress = 1.0;
    private TimerState _state = TimerState.Idle;
    private double _volumePercent = 70;
    private bool _isMuted;
    private string _soundId = "alarm";
    private bool _isDarkTheme = true;

    public TimerViewModel(
        TimerService timer,
        SoundService sound,
        SettingsService settingsService,
        AppSettings settings)
    {
        _timer = timer;
        _sound = sound;
        _settingsService = settingsService;
        _settings = settings;

        _hoursInput = Math.Clamp(settings.LastHours, 0, 23);
        _minutesInput = Math.Clamp(settings.LastMinutes, 0, 59);
        _secondsInput = Math.Clamp(settings.LastSeconds, 0, 59);
        _volumePercent = Math.Clamp(settings.Volume, 0, 1) * 100;
        _isMuted = settings.IsMuted;
        _soundId = string.IsNullOrWhiteSpace(settings.SoundId) ? "alarm" : settings.SoundId;
        _isDarkTheme = !string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);

        foreach (var preset in settings.CustomPresets)
            CustomPresets.Add(preset);

        _remaining = InputDuration;
        _timer.SetDuration(InputDuration);
        _sound.ApplyVolume(_volumePercent / 100.0, _isMuted);
        _sound.SetSound(_soundId, _settingsService.GetCustomSoundFilePath(settings));

        _timer.Ticked += OnTimerTicked;
        _timer.StateChanged += OnTimerStateChanged;
        _timer.Completed += OnTimerCompleted;

        StartCommand = new RelayCommand(Start, () => (IsIdle || IsFinished) && InputDuration > TimeSpan.Zero);
        PauseCommand = new RelayCommand(Pause, () => IsRunning);
        ResumeCommand = new RelayCommand(Resume, () => IsPaused);
        ResetCommand = new RelayCommand(Reset);
        ToggleStartPauseCommand = new RelayCommand(ToggleStartPause);
        ApplyPresetCommand = new RelayCommand<object?>(ApplyPreset);
        ApplyCustomPresetCommand = new RelayCommand<CustomPreset>(ApplyCustomPreset);
        DeleteCustomPresetCommand = new RelayCommand<CustomPreset>(DeleteCustomPreset);
        AddMinuteCommand = new RelayCommand(AddMinute, () => IsRunning || IsPaused);
        ToggleMuteCommand = new RelayCommand(ToggleMute);
        PreviewSoundCommand = new RelayCommand(() => _sound.PlayPreview());

        RefreshStateProperties();
        RestoreSavedSession();
    }

    /// <summary>Таймер дошёл до нуля: окно показывает уведомление и мигание.</summary>
    public event EventHandler? TimerFinished;

    // ------------------------------------------------------------------ ввод

    public double HoursInput
    {
        get => _hoursInput;
        set
        {
            if (SetProperty(ref _hoursInput, SanitizeInput(value, 23)))
                OnInputDurationChanged();
        }
    }

    public double MinutesInput
    {
        get => _minutesInput;
        set
        {
            if (SetProperty(ref _minutesInput, SanitizeInput(value, 59)))
                OnInputDurationChanged();
        }
    }

    public double SecondsInput
    {
        get => _secondsInput;
        set
        {
            if (SetProperty(ref _secondsInput, SanitizeInput(value, 59)))
                OnInputDurationChanged();
        }
    }

    /// <summary>Пустое поле ввода даёт NaN — приводим к безопасному значению.</summary>
    private static double SanitizeInput(double value, double max) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? 0
            : Math.Clamp(value, 0, max);

    /// <summary>Длительность, собранная из полей ввода.</summary>
    public TimeSpan InputDuration => new(
        (int)HoursInput, (int)MinutesInput, (int)SecondsInput);

    /// <summary>Поля ввода доступны только до старта и после финиша.</summary>
    public bool CanEditInputs => IsIdle || IsFinished;

    // -------------------------------------------------------------- состояние

    public TimerState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    /// <summary>Кнопка «Старт» видна до запуска и после финиша («Ещё раз»).</summary>
    public bool ShowStart => IsIdle || IsFinished;

    /// <summary>Кнопка «+1 мин» видна во время отсчёта и на паузе.</summary>
    public bool ShowAddMinute => IsRunning || IsPaused;

    public bool IsIdle => State == TimerState.Idle;
    public bool IsRunning => State == TimerState.Running;
    public bool IsPaused => State == TimerState.Paused;
    public bool IsFinished => State == TimerState.Finished;

    /// <summary>Русская подпись состояния под кольцом и в pill-индикаторе.</summary>
    public string StatusText => State switch
    {
        TimerState.Running => "Идёт отсчёт",
        TimerState.Paused => "Пауза",
        TimerState.Finished => "Время вышло",
        _ => "Таймер не запущен"
    };

    /// <summary>Цвет точки-индикатора состояния.</summary>
    public Brush StatusBrush => State switch
    {
        // Последние 10 секунд точка тревожно краснеет.
        TimerState.Running when Remaining <= TimeSpan.FromSeconds(10)
            => new SolidColorBrush(Colors.OrangeRed),
        TimerState.Running => new SolidColorBrush(Colors.LimeGreen),
        TimerState.Paused => new SolidColorBrush(Colors.Orange),
        TimerState.Finished => new SolidColorBrush(Colors.OrangeRed),
        _ => new SolidColorBrush(Colors.Gray)
    };

    /// <summary>Оставшееся время из движка.</summary>
    public TimeSpan Remaining
    {
        get => _remaining;
        private set
        {
            if (SetProperty(ref _remaining, value))
            {
                OnPropertyChanged(nameof(DisplayTime));
            }
        }
    }

    /// <summary>Крупные цифры: округление вверх, чтобы 00:00 не мелькало раньше срока.</summary>
    public string DisplayTime
    {
        get
        {
            TimeSpan shown = State is TimerState.Running or TimerState.Paused
                ? TimeSpan.FromSeconds(Math.Ceiling(Remaining.TotalSeconds))
                : Remaining;
            return TimeFormatter.Format(shown);
        }
    }

    /// <summary>Доля оставшегося времени 0..1 для кругового индикатора.</summary>
    public double Progress
    {
        get => _progress;
        private set
        {
            if (SetProperty(ref _progress, value))
                OnPropertyChanged(nameof(ProgressPercentText));
        }
    }

    /// <summary>Осталось в процентах, например «42%».</summary>
    public string ProgressPercentText => $"{(int)Math.Round(Progress * 100)}%";

    /// <summary>
    /// Спокойная информационная строка под цифрами: во сколько закончится,
    /// какая длительность задана и т.п.
    /// </summary>
    public string EndTimeText => State switch
    {
        TimerState.Running =>
            "закончится в " + _timer.EndTimeUtc.ToLocalTime().ToString("HH:mm"),
        TimerState.Paused => "отсчёт приостановлен",
        TimerState.Finished => "заданное время вышло",
        _ => InputDuration > TimeSpan.Zero
            ? "длительность " + TimeFormatter.FormatFriendly(InputDuration)
            : "задайте длительность"
    };

    // ------------------------------------------------------------------ звук

    /// <summary>Громкость в процентах для слайдера 0..100.</summary>
    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            double clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _volumePercent, clamped))
            {
                _settings.Volume = clamped / 100.0;
                _sound.ApplyVolume(_settings.Volume, IsMuted);
                OnPropertyChanged(nameof(VolumeText));
                _settingsService.Save(_settings);
            }
        }
    }

    public string VolumeText => $"{(int)Math.Round(VolumePercent)}%";

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _settings.IsMuted = value;
                OnPropertyChanged(nameof(IsNotMuted));
                _sound.ApplyVolume(_settings.Volume, value);
                if (value)
                    _sound.StopAlarm();
                _settingsService.Save(_settings);
            }
        }
    }

    // ------------------------------------------------------------------ тема

    /// <summary>Выбранная мелодия: alarm, chime, beep, gentle или custom.</summary>
    public string SoundId
    {
        get => _soundId;
        set
        {
            if (SetProperty(ref _soundId, value))
            {
                _settings.SoundId = value;
                _sound.SetSound(value, _settingsService.GetCustomSoundFilePath(_settings));
                _settingsService.Save(_settings);
            }
        }
    }

    /// <summary>Выбран ли пользовательский файл звука.</summary>
    public bool HasCustomSound => !string.IsNullOrWhiteSpace(_settings.CustomSoundFileName);

    /// <summary>Регистрирует скопированный пользовательский файл как текущую мелодию.</summary>
    public void SetCustomSoundFile(string fileName)
    {
        _settings.CustomSoundFileName = fileName;
        OnPropertyChanged(nameof(HasCustomSound));

        if (string.Equals(_soundId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            _sound.SetSound("custom", _settingsService.GetCustomSoundFilePath(_settings));
            _settingsService.Save(_settings);
        }
        else
        {
            SoundId = "custom";
        }
    }

    /// <summary>Обратное к IsMuted — для IsEnabled кнопки проверки звука.</summary>
    public bool IsNotMuted => !IsMuted;

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (SetProperty(ref _isDarkTheme, value))
            {
                _settings.Theme = value ? "Dark" : "Light";
                _settingsService.Save(_settings);
            }
        }
    }

    // --------------------------------------------------------------- пресеты

    /// <summary>Пользовательские пресеты (сохраняются между запусками).</summary>
    public ObservableCollection<CustomPreset> CustomPresets { get; } = new();

    public bool HasCustomPresets => CustomPresets.Count > 0;

    /// <summary>Добавляет пресет из диалога «Свой пресет».</summary>
    public void AddCustomPreset(string name, int totalSeconds)
    {
        if (totalSeconds <= 0)
            return;

        var preset = new CustomPreset
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? TimeFormatter.FormatFriendly(TimeSpan.FromSeconds(totalSeconds))
                : name.Trim(),
            TotalSeconds = totalSeconds
        };

        CustomPresets.Add(preset);
        _settings.CustomPresets.Add(preset);
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(HasCustomPresets));
    }

    // --------------------------------------------------------------- команды

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ToggleStartPauseCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand ApplyCustomPresetCommand { get; }
    public ICommand DeleteCustomPresetCommand { get; }
    public ICommand AddMinuteCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand PreviewSoundCommand { get; }

    /// <summary>Вызывается окном перед закрытием: сохранить ввод и пресеты.</summary>
    public void PersistAll()
    {
        _settings.LastHours = HoursInput;
        _settings.LastMinutes = MinutesInput;
        _settings.LastSeconds = SecondsInput;
        _settings.Volume = VolumePercent / 100.0;
        _settings.IsMuted = IsMuted;
        _settings.Theme = IsDarkTheme ? "Dark" : "Light";
        _settings.CustomPresets = new System.Collections.Generic.List<CustomPreset>(CustomPresets);
        _settingsService.Save(_settings);
    }

    // -------------------------------------------------------------- реализация

    private void Start()
    {
        TimeSpan duration = InputDuration;
        if (duration <= TimeSpan.Zero)
            return;

        _sound.StopAlarm();
        _timer.Start(duration);
        PersistTimerSession();
    }

    private void Pause()
    {
        _timer.Pause();
        PersistTimerSession();
    }

    private void Resume()
    {
        _sound.StopAlarm();
        _timer.Resume();
        PersistTimerSession();
    }

    private void Reset()
    {
        _sound.StopAlarm();
        _timer.Reset();
        _timer.SetDuration(InputDuration);
        Remaining = InputDuration;
        Progress = 1.0;
        PersistTimerSession();
    }

    /// <summary>Пробел: старт / пауза / продолжить в зависимости от состояния.</summary>
    private void ToggleStartPause()
    {
        switch (State)
        {
            case TimerState.Running:
                Pause();
                break;
            case TimerState.Paused:
                Resume();
                break;
            case TimerState.Finished:
                Start();
                break;
            default:
                Start();
                break;
        }
    }

    /// <summary>Встроенный пресет: параметр — секунды строкой из XAML ("300").</summary>
    private void ApplyPreset(object? parameter)
    {
        if (!CanEditInputs)
            return;

        if (!int.TryParse(parameter?.ToString(), out int seconds) || seconds <= 0)
            return;

        ApplyDuration(TimeSpan.FromSeconds(seconds));
    }

    private void ApplyCustomPreset(CustomPreset? preset)
    {
        if (!CanEditInputs || preset is null)
            return;

        ApplyDuration(TimeSpan.FromSeconds(preset.TotalSeconds));
    }

    private void DeleteCustomPreset(CustomPreset? preset)
    {
        if (preset is null)
            return;

        CustomPresets.Remove(preset);
        _settings.CustomPresets.Remove(preset);
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(HasCustomPresets));
    }

    /// <summary>«+1 мин» во время отсчёта и паузы.</summary>
    private void AddMinute()
    {
        _timer.AddTime(TimeSpan.FromMinutes(1));
        PersistTimerSession();
    }

    private void ToggleMute() => IsMuted = !IsMuted;

    private void ApplyDuration(TimeSpan duration)
    {
        // Таймер умеет максимум 23:59:59 — длинные пресеты честно обрезаем,
        // а не теряем сутки через duration.Hours.
        var maxDuration = new TimeSpan(23, 59, 59);
        if (duration > maxDuration)
            duration = maxDuration;
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        // Поля ввода вызовут OnInputDurationChanged и обновят движок.
        HoursInput = duration.Hours;
        MinutesInput = duration.Minutes;
        SecondsInput = duration.Seconds;
    }

    private void OnInputDurationChanged()
    {
        _settings.LastHours = HoursInput;
        _settings.LastMinutes = MinutesInput;
        _settings.LastSeconds = SecondsInput;
        _settingsService.Save(_settings);

        (StartCommand as RelayCommand)?.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(EndTimeText));

        if (CanEditInputs)
        {
            // Правка длительности после финиша также глушит сигнал.
            _sound.StopAlarm();
            _timer.SetDuration(InputDuration);
            Remaining = InputDuration;
            Progress = InputDuration > TimeSpan.Zero ? 1.0 : 0.0;
        }
    }

    private void OnTimerTicked(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EndTimeText));
        OnPropertyChanged(nameof(StatusBrush));
        Remaining = _timer.Remaining;
        Progress = _timer.Duration > TimeSpan.Zero
            ? Math.Clamp(_timer.Remaining.TotalSeconds / _timer.Duration.TotalSeconds, 0, 1)
            : 0;
    }

    private void OnTimerStateChanged(object? sender, EventArgs e)
    {
        State = _timer.State;
        RefreshStateProperties();
    }

    private void OnTimerCompleted(object? sender, EventArgs e)
    {
        Remaining = TimeSpan.Zero;
        Progress = 0;
        PersistTimerSession();
        _sound.PlayAlarm();
        TimerFinished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Сохраняет сессию отсчёта, чтобы приложение пережило закрытие,
    /// падение и перезагрузку ПК. На финише и сбросе сессия очищается.
    /// </summary>
    private void PersistTimerSession()
    {
        switch (_timer.State)
        {
            case TimerState.Running:
                _settings.SavedTimerState = "Running";
                _settings.SavedTimerEndUtcTicks = _timer.EndTimeUtc.UtcTicks;
                _settings.SavedTimerDurationSeconds = _timer.Duration.TotalSeconds;
                _settings.SavedTimerRemainingSeconds = _timer.Remaining.TotalSeconds;
                break;

            case TimerState.Paused:
                _settings.SavedTimerState = "Paused";
                _settings.SavedTimerEndUtcTicks = 0;
                _settings.SavedTimerDurationSeconds = _timer.Duration.TotalSeconds;
                _settings.SavedTimerRemainingSeconds = _timer.Remaining.TotalSeconds;
                break;

            default:
                _settings.SavedTimerState = string.Empty;
                _settings.SavedTimerEndUtcTicks = 0;
                _settings.SavedTimerDurationSeconds = 0;
                _settings.SavedTimerRemainingSeconds = 0;
                break;
        }

        _settingsService.Save(_settings);
    }

    /// <summary>Подхватывает сохранённый отсчёт при запуске приложения.</summary>
    private void RestoreSavedSession()
    {
        try
        {
            var duration = TimeSpan.FromSeconds(_settings.SavedTimerDurationSeconds);
            if (duration <= TimeSpan.Zero)
                return;

            if (string.Equals(_settings.SavedTimerState, "Running", StringComparison.OrdinalIgnoreCase) &&
                _settings.SavedTimerEndUtcTicks > 0)
            {
                var endUtc = new DateTimeOffset(_settings.SavedTimerEndUtcTicks, TimeSpan.Zero);
                _timer.RestoreRunning(duration, endUtc);
                Remaining = _timer.Remaining;
                Progress = _timer.State == TimerState.Finished
                    ? 0
                    : Math.Clamp(_timer.Remaining.TotalSeconds / duration.TotalSeconds, 0, 1);

                // Дедлайн прошёл, пока приложения не было: показали Finished
                // один раз и очистили сессию, чтобы не воскрешать её снова.
                if (_timer.State == TimerState.Finished)
                    PersistTimerSession();
            }
            else if (string.Equals(_settings.SavedTimerState, "Paused", StringComparison.OrdinalIgnoreCase))
            {
                _timer.RestorePaused(
                    duration,
                    TimeSpan.FromSeconds(_settings.SavedTimerRemainingSeconds));
                Remaining = _timer.Remaining;
                Progress = Math.Clamp(_timer.Remaining.TotalSeconds / duration.TotalSeconds, 0, 1);
            }
        }
        catch (Exception)
        {
            // Повреждённая сохранённая сессия не должна мешать запуску:
            // просто очищаем её и стартуем как обычно.
            _settings.SavedTimerState = string.Empty;
            _settingsService.Save(_settings);
        }
    }

    private void RefreshStateProperties()
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(CanEditInputs));
        OnPropertyChanged(nameof(ShowStart));
        OnPropertyChanged(nameof(ShowAddMinute));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(DisplayTime));
        OnPropertyChanged(nameof(EndTimeText));

        (StartCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (PauseCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ResumeCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (AddMinuteCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}
