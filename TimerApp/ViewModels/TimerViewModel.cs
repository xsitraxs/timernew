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
    private string _phaseSoundId = "beep";
    private bool _isDarkTheme = true;
    private string _selectedPresetName = string.Empty;
    private string _timerName = string.Empty;
    private string _quickInputFeedback = string.Empty;
    private bool _useIntervalMode;
    private string _intervalWorkName = "Бег";
    private string _intervalRestName = "Ходьба";
    private double _intervalWorkMinutes = 1;
    private double _intervalWorkSecondsPart = 30;
    private double _intervalRestMinutes = 2;
    private double _intervalRestSecondsPart;
    private double _intervalRoundsInput = 10;
    private bool _intervalActive;
    private int _intervalPhase;
    private int _intervalRound = 1;
    private bool _intervalJustFinished;
    private bool _phoneRemoteEnabled;

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
        _phaseSoundId = string.IsNullOrWhiteSpace(settings.PhaseSoundId) ? "beep" : settings.PhaseSoundId;
        _isDarkTheme = !string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        _selectedPresetName = settings.LastPresetName ?? string.Empty;
        _timerName = settings.LastTimerName ?? string.Empty;
        _useIntervalMode = settings.IntervalModeEnabled;
        _intervalWorkName = string.IsNullOrWhiteSpace(settings.IntervalWorkName)
            ? "Бег"
            : settings.IntervalWorkName;
        _intervalRestName = string.IsNullOrWhiteSpace(settings.IntervalRestName)
            ? "Ходьба"
            : settings.IntervalRestName;
        int workTotalSeconds = Math.Max(0, (int)Math.Round(settings.IntervalWorkSeconds));
        int restTotalSeconds = Math.Max(0, (int)Math.Round(settings.IntervalRestSeconds));
        _intervalWorkMinutes = Math.Clamp(workTotalSeconds / 60, 0, 600);
        _intervalWorkSecondsPart = Math.Clamp(workTotalSeconds % 60, 0, 59);
        _intervalRestMinutes = Math.Clamp(restTotalSeconds / 60, 0, 600);
        _intervalRestSecondsPart = Math.Clamp(restTotalSeconds % 60, 0, 59);
        _intervalRoundsInput = Math.Clamp(settings.IntervalRounds, 0, 999);
        _phoneRemoteEnabled = settings.PhoneRemoteEnabled;

        foreach (var preset in settings.CustomPresets)
            CustomPresets.Add(preset);

        var initialDuration = _useIntervalMode && IntervalWorkDuration > TimeSpan.Zero
            ? IntervalWorkDuration
            : InputDuration;
        _remaining = initialDuration;
        _timer.SetDuration(initialDuration);
        _sound.ApplyVolume(_volumePercent / 100.0, _isMuted);
        _sound.SetSound(_soundId, _settingsService.GetCustomSoundFilePath(settings));
        _sound.SetPhaseSound(_phaseSoundId);

        _timer.Ticked += OnTimerTicked;
        _timer.StateChanged += OnTimerStateChanged;
        _timer.Completed += OnTimerCompleted;

        StartCommand = new RelayCommand(Start, () =>
            (IsIdle || IsFinished) &&
            (UseIntervalMode ? IntervalWorkDuration > TimeSpan.Zero : InputDuration > TimeSpan.Zero));
        PauseCommand = new RelayCommand(Pause, () => IsRunning);
        ResumeCommand = new RelayCommand(Resume, () => IsPaused);
        ResetCommand = new RelayCommand(Reset);
        ToggleStartPauseCommand = new RelayCommand(ToggleStartPause);
        ApplyPresetCommand = new RelayCommand<object?>(ApplyPreset);
        ApplyCustomPresetCommand = new RelayCommand<CustomPreset>(ApplyCustomPreset);
        ApplyIntervalProgramCommand = new RelayCommand<string?>(ApplyIntervalProgram);
        DeleteCustomPresetCommand = new RelayCommand<CustomPreset>(DeleteCustomPreset);
        AddMinuteCommand = new RelayCommand(AddMinute, () => IsRunning || IsPaused);
        SkipPhaseCommand = new RelayCommand(SkipPhase, () =>
            _intervalActive && (IsRunning || IsPaused) && HasNextIntervalPhase);
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
    public string StatusText => _intervalActive
        ? State switch
        {
            TimerState.Running => $"{CurrentIntervalPhaseName} · круг {IntervalRoundText}",
            TimerState.Paused => $"Пауза · {CurrentIntervalPhaseName} · круг {IntervalRoundText}",
            TimerState.Finished => "Тренировка завершена",
            _ => "Интервалы готовы"
        }
        : State switch
        {
            TimerState.Running => "Идёт отсчёт",
            TimerState.Paused => "Пауза",
            TimerState.Finished => "Время вышло",
            _ => "Таймер не запущен"
        };

    /// <summary>Заголовок финишной панели: с названием таймера человечнее.</summary>
    public string FinishedInfoTitle => _intervalJustFinished
        ? "Тренировка завершена!"
        : HasTimerName ? $"{TimerName} — готово!" : "Время вышло";

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
                OnPropertyChanged(nameof(WindowTitleText));
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

    /// <summary>
    /// Текст заголовка окна (он же виден на кнопке панели задач и в Alt-Tab):
    /// во время отсчёта — живой остаток, на финише — «Время вышло».
    /// </summary>
    public string WindowTitleText => State switch
    {
        TimerState.Running or TimerState.Paused => _intervalActive
            ? $"{DisplayTime} — {CurrentIntervalPhaseName} · круг {IntervalRoundText}"
            : HasTimerName
                ? $"{DisplayTime} — {TimerName}"
                : $"{DisplayTime} — Таймер",
        TimerState.Finished => _intervalJustFinished
            ? "Тренировка завершена — Таймер"
            : HasTimerName
                ? $"{TimerName}: время вышло"
                : "Время вышло — Таймер",
        _ => HasTimerName ? $"Таймер — {TimerName}" : "Таймер"
    };

    /// <summary>Осталось в процентах, например «42%».</summary>
    public string ProgressPercentText => $"{(int)Math.Round(Progress * 100)}%";

    /// <summary>
    /// Спокойная информационная строка под цифрами: во сколько закончится,
    /// какая длительность задана и т.п.
    /// </summary>
    public string EndTimeText => _intervalActive
        ? State switch
        {
            TimerState.Running => HasFiniteIntervalRounds
                ? "этап до " + _timer.EndTimeUtc.ToLocalTime().ToString("HH:mm") +
                  " · всего осталось " + TimeFormatter.FormatFriendly(IntervalTotalRemaining())
                : "этап до " + _timer.EndTimeUtc.ToLocalTime().ToString("HH:mm") + " · круги без конца",
            TimerState.Paused => HasFiniteIntervalRounds
                ? "пауза · всего осталось " + TimeFormatter.FormatFriendly(IntervalTotalRemaining())
                : "пауза · круги без конца",
            TimerState.Finished => "тренировка завершена",
            _ => IntervalCycleText
        }
        : State switch
        {
            TimerState.Running =>
                "закончится в " + _timer.EndTimeUtc.ToLocalTime().ToString("HH:mm"),
            TimerState.Paused => "отсчёт приостановлен",
            TimerState.Finished => "заданное время вышло",
            _ => UseIntervalMode
                ? IntervalCycleText
                : InputDuration > TimeSpan.Zero
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

    /// <summary>Мелодия короткого сигнала смены этапа: alarm, chime, beep или gentle.</summary>
    public string PhaseSoundId
    {
        get => _phaseSoundId;
        set
        {
            if (SetProperty(ref _phaseSoundId, value))
            {
                _settings.PhaseSoundId = value;
                _sound.SetPhaseSound(value);
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

    /// <summary>
    /// Управление с телефона по домашней Wi-Fi сети: окно по этой настройке
    /// поднимает/гасит локальный веб-сервер с QR-кодом.
    /// </summary>
    public bool PhoneRemoteEnabled
    {
        get => _phoneRemoteEnabled;
        set
        {
            if (SetProperty(ref _phoneRemoteEnabled, value))
            {
                _settings.PhoneRemoteEnabled = value;
                _settingsService.Save(_settings);
            }
        }
    }

    // --------------------------------------------------------------- пресеты

    /// <summary>Пользовательские пресеты (сохраняются между запусками).</summary>
    public ObservableCollection<CustomPreset> CustomPresets { get; } = new();

    public bool HasCustomPresets => CustomPresets.Count > 0;

    /// <summary>
    /// Название выбранного пресета (пусто, если длительность набрана вручную
    /// или правлена после выбора пресета).
    /// </summary>
    public string SelectedPresetName
    {
        get => _selectedPresetName;
        private set
        {
            if (SetProperty(ref _selectedPresetName, value))
            {
                OnPropertyChanged(nameof(HasSelectedPreset));
                OnPropertyChanged(nameof(PresetNameText));
                OnPropertyChanged(nameof(ShowPresetName));
            }
        }
    }

    /// <summary>Есть ли выбранный пресет, чьё название надо показывать.</summary>
    public bool HasSelectedPreset => !string.IsNullOrWhiteSpace(SelectedPresetName);

    /// <summary>Подпись под цифрами, например «Пресет: Работа».</summary>
    public string PresetNameText => $"Пресет: {SelectedPresetName}";

    /// <summary>
    /// Подпись пресета скрываем, если название таймера её дословно повторяет
    /// (свой пресет при применении сам становится названием таймера).
    /// </summary>
    public bool ShowPresetName => HasSelectedPreset &&
        !(HasTimerName &&
          string.Equals(SelectedPresetName, TimerName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Название текущего таймера из быстрого ввода («15м чай» → «чай»):
    /// видно под цифрами, в заголовке окна и в уведомлении по окончании.
    /// </summary>
    public string TimerName
    {
        get => _timerName;
        private set
        {
            string normalized = (value ?? string.Empty).Trim();
            if (SetProperty(ref _timerName, normalized))
            {
                _settings.LastTimerName = normalized;
                _settingsService.Save(_settings);
                OnPropertyChanged(nameof(HasTimerName));
                OnPropertyChanged(nameof(ShowPresetName));
                OnPropertyChanged(nameof(WindowTitleText));
                OnPropertyChanged(nameof(FinishedInfoTitle));
            }
        }
    }

    /// <summary>Задано ли название таймера.</summary>
    public bool HasTimerName => !string.IsNullOrWhiteSpace(TimerName);

    /// <summary>Подсказка под полем быстрого ввода: что поняли из ввода.</summary>
    public string QuickInputFeedback
    {
        get => _quickInputFeedback;
        private set
        {
            if (SetProperty(ref _quickInputFeedback, value))
                OnPropertyChanged(nameof(HasQuickInputFeedback));
        }
    }

    /// <summary>Показывать ли подсказку быстрого ввода.</summary>
    public bool HasQuickInputFeedback => !string.IsNullOrWhiteSpace(QuickInputFeedback);

    // --------------------------------------------------------------- интервалы

    /// <summary>
    /// Интервальный режим двух этапов (бег/ходьба, работа/отдых): этапы сами
    /// чередуются по кругу, нажимать ничего не нужно.
    /// </summary>
    public bool UseIntervalMode
    {
        get => _useIntervalMode;
        set
        {
            if (SetProperty(ref _useIntervalMode, value))
            {
                _settings.IntervalModeEnabled = value;
                _settingsService.Save(_settings);
                SyncIdleDurationDisplay();
                RefreshStateProperties();
            }
        }
    }

    /// <summary>Название первого этапа (по умолчанию «Бег»).</summary>
    public string IntervalWorkName
    {
        get => _intervalWorkName;
        set
        {
            if (SetProperty(ref _intervalWorkName, value ?? string.Empty))
            {
                _settings.IntervalWorkName = _intervalWorkName;
                _settingsService.Save(_settings);
                OnPropertyChanged(nameof(CurrentIntervalPhaseName));
                OnPropertyChanged(nameof(IntervalCycleText));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(WindowTitleText));
            }
        }
    }

    /// <summary>Название второго этапа (по умолчанию «Ходьба»).</summary>
    public string IntervalRestName
    {
        get => _intervalRestName;
        set
        {
            if (SetProperty(ref _intervalRestName, value ?? string.Empty))
            {
                _settings.IntervalRestName = _intervalRestName;
                _settingsService.Save(_settings);
                OnPropertyChanged(nameof(CurrentIntervalPhaseName));
                OnPropertyChanged(nameof(IntervalCycleText));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(WindowTitleText));
            }
        }
    }

    public double IntervalWorkMinutes
    {
        get => _intervalWorkMinutes;
        set
        {
            if (SetProperty(ref _intervalWorkMinutes, SanitizeInput(value, 600)))
                OnIntervalInputsChanged();
        }
    }

    public double IntervalWorkSecondsPart
    {
        get => _intervalWorkSecondsPart;
        set
        {
            if (SetProperty(ref _intervalWorkSecondsPart, SanitizeInput(value, 59)))
                OnIntervalInputsChanged();
        }
    }

    public double IntervalRestMinutes
    {
        get => _intervalRestMinutes;
        set
        {
            if (SetProperty(ref _intervalRestMinutes, SanitizeInput(value, 600)))
                OnIntervalInputsChanged();
        }
    }

    public double IntervalRestSecondsPart
    {
        get => _intervalRestSecondsPart;
        set
        {
            if (SetProperty(ref _intervalRestSecondsPart, SanitizeInput(value, 59)))
                OnIntervalInputsChanged();
        }
    }

    /// <summary>Кругов в интервальном режиме. 0 — бесконечно.</summary>
    public double IntervalRoundsInput
    {
        get => _intervalRoundsInput;
        set
        {
            if (SetProperty(ref _intervalRoundsInput, SanitizeInput(value, 999)))
            {
                _settings.IntervalRounds = IntervalRounds;
                _settingsService.Save(_settings);
                OnPropertyChanged(nameof(IntervalRounds));
                OnPropertyChanged(nameof(HasFiniteIntervalRounds));
                OnPropertyChanged(nameof(IntervalRoundText));
                OnPropertyChanged(nameof(EndTimeText));
                (SkipPhaseCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Длительность первого этапа из полей ввода.</summary>
    public TimeSpan IntervalWorkDuration =>
        TimeSpan.FromMinutes(_intervalWorkMinutes) + TimeSpan.FromSeconds(_intervalWorkSecondsPart);

    /// <summary>Длительность второго этапа из полей ввода.</summary>
    public TimeSpan IntervalRestDuration =>
        TimeSpan.FromMinutes(_intervalRestMinutes) + TimeSpan.FromSeconds(_intervalRestSecondsPart);

    /// <summary>Кругов задано (0 = бесконечно).</summary>
    public int IntervalRounds => (int)Math.Round(Math.Clamp(_intervalRoundsInput, 0, 999));

    /// <summary>Круги ограничены, а не бесконечные.</summary>
    public bool HasFiniteIntervalRounds => IntervalRounds > 0;

    /// <summary>Интервальная тренировка прямо сейчас идёт (или на паузе).</summary>
    public bool IsIntervalActive => _intervalActive;

    /// <summary>Последний завершённый отсчёт был интервальной тренировкой.</summary>
    public bool IntervalJustFinished => _intervalJustFinished;

    /// <summary>Отображаемое имя этапа: пустое поле ввода — «Этап 1»/«Этап 2».</summary>
    private string PhaseDisplayName(int phase) => phase == 0
        ? string.IsNullOrWhiteSpace(_intervalWorkName) ? "Этап 1" : _intervalWorkName.Trim()
        : string.IsNullOrWhiteSpace(_intervalRestName) ? "Этап 2" : _intervalRestName.Trim();

    /// <summary>Имя текущего этапа тренировки.</summary>
    public string CurrentIntervalPhaseName => PhaseDisplayName(_intervalPhase);

    /// <summary>Длительность текущего этапа.</summary>
    private TimeSpan CurrentIntervalPhaseDuration =>
        _intervalPhase == 0 ? IntervalWorkDuration : IntervalRestDuration;

    /// <summary>«3 из 10» или «3 · без конца».</summary>
    public string IntervalRoundText => HasFiniteIntervalRounds
        ? $"{_intervalRound} из {IntervalRounds}"
        : $"{_intervalRound} · без конца";

    /// <summary>Кнопка «Следующий этап» видна во время интервальной тренировки.</summary>
    public bool ShowSkipPhase => _intervalActive && (IsRunning || IsPaused);

    /// <summary>Есть ли этап после текущего (на последнем круге первого этапа — нет).</summary>
    public bool HasNextIntervalPhase
    {
        get
        {
            if (!_intervalActive)
                return false;

            if (_intervalPhase == 1)
                return true; // после второго этапа всегда идёт первый следующего круга

            // Первый этап: конец только на последнем круге конечной программы.
            return !HasFiniteIntervalRounds || _intervalRound < IntervalRounds;
        }
    }

    /// <summary>Короткое описание цикла для строки под цифрами.</summary>
    public string IntervalCycleText => IntervalWorkDuration > TimeSpan.Zero
        ? $"цикл: {PhaseDisplayName(0)} {TimeFormatter.FormatFriendly(IntervalWorkDuration)}" +
          $" + {PhaseDisplayName(1)} {TimeFormatter.FormatFriendly(IntervalRestDuration)}" +
          (HasFiniteIntervalRounds ? $" · кругов: {IntervalRounds}" : " · бесконечно")
        : "настройте длительность первого этапа";

    /// <summary>Сколько всего осталось до конца конечной интервальной программы.</summary>
    private TimeSpan IntervalTotalRemaining()
    {
        double total = Math.Max(0, _timer.Remaining.TotalSeconds);
        if (!HasFiniteIntervalRounds)
            return TimeSpan.FromSeconds(total);

        double work = IntervalWorkDuration.TotalSeconds;
        double rest = IntervalRestDuration.TotalSeconds;
        int roundsLeft = Math.Max(0, IntervalRounds - _intervalRound);

        if (_intervalPhase == 0)
        {
            // После текущего первого этапа: второй этап этого круга (если круг
            // не последний), первые этапы оставшихся кругов и их вторые этапы —
            // кроме самого последнего круга, где второго этапа уже нет.
            total += (_intervalRound < IntervalRounds ? rest : 0)
                     + roundsLeft * work
                     + Math.Max(0, roundsLeft - 1) * rest;
        }
        else
        {
            // После текущего второго этапа: оставшиеся круги; у последнего
            // круга второго этапа уже нет.
            total += roundsLeft * work + Math.Max(0, roundsLeft - 1) * rest;
        }

        return TimeSpan.FromSeconds(Math.Max(0, total));
    }

    private void OnIntervalInputsChanged()
    {
        _settings.IntervalWorkSeconds = IntervalWorkDuration.TotalSeconds;
        _settings.IntervalRestSeconds = IntervalRestDuration.TotalSeconds;
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(IntervalWorkDuration));
        OnPropertyChanged(nameof(IntervalRestDuration));
        OnPropertyChanged(nameof(IntervalCycleText));
        OnPropertyChanged(nameof(EndTimeText));
        SyncIdleDurationDisplay();
        (StartCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// В ожидании крупные цифры показывают то, что запустит кнопка «Старт»:
    /// в интервальном режиме — первый этап, в обычном — набранную длительность.
    /// </summary>
    private void SyncIdleDurationDisplay()
    {
        if (!CanEditInputs)
            return;

        TimeSpan display = UseIntervalMode && IntervalWorkDuration > TimeSpan.Zero
            ? IntervalWorkDuration
            : InputDuration;
        _sound.StopAlarm();
        _timer.SetDuration(display);
        Remaining = display;
        Progress = display > TimeSpan.Zero ? 1.0 : 0.0;
        OnPropertyChanged(nameof(EndTimeText));
        (StartCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void RefreshIntervalProperties()
    {
        OnPropertyChanged(nameof(IsIntervalActive));
        OnPropertyChanged(nameof(IntervalJustFinished));
        OnPropertyChanged(nameof(CurrentIntervalPhaseName));
        OnPropertyChanged(nameof(IntervalRoundText));
        OnPropertyChanged(nameof(ShowSkipPhase));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(FinishedInfoTitle));
        OnPropertyChanged(nameof(WindowTitleText));
        OnPropertyChanged(nameof(EndTimeText));
        (SkipPhaseCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void SetSelectedPresetName(string name)
    {
        SelectedPresetName = name;
        _settings.LastPresetName = name;
        _settingsService.Save(_settings);
    }

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
    public ICommand SkipPhaseCommand { get; }
    public ICommand ApplyIntervalProgramCommand { get; }
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
        _settings.LastPresetName = SelectedPresetName;
        _settings.LastTimerName = TimerName;
        _settings.IntervalModeEnabled = UseIntervalMode;
        _settings.IntervalWorkName = IntervalWorkName;
        _settings.IntervalRestName = IntervalRestName;
        _settings.IntervalWorkSeconds = IntervalWorkDuration.TotalSeconds;
        _settings.IntervalRestSeconds = IntervalRestDuration.TotalSeconds;
        _settings.IntervalRounds = IntervalRounds;
        _settings.CustomPresets = new System.Collections.Generic.List<CustomPreset>(CustomPresets);
        _settingsService.Save(_settings);
    }

    // -------------------------------------------------------------- реализация

    private void Start()
    {
        _intervalJustFinished = false;

        if (UseIntervalMode)
        {
            StartInterval();
            return;
        }

        _intervalActive = false;

        TimeSpan duration = InputDuration;
        if (duration <= TimeSpan.Zero)
            return;

        _sound.StopAlarm();
        _timer.Start(duration);
        PersistTimerSession();
    }

    /// <summary>Старт интервальной тренировки: первый этап, первый круг.</summary>
    private void StartInterval()
    {
        if (IntervalWorkDuration <= TimeSpan.Zero)
            return;

        _sound.StopAlarm();
        _intervalActive = true;
        _intervalPhase = 0;
        _intervalRound = 1;
        _timer.Start(IntervalWorkDuration);
        PersistTimerSession();
        RefreshIntervalProperties();
    }

    /// <summary>Кнопка «Следующий этап»: сразу перейти дальше по программе.</summary>
    private void SkipPhase()
    {
        if (!_intervalActive || !(IsRunning || IsPaused) || !HasNextIntervalPhase)
            return;

        bool wasPaused = IsPaused;
        if (!TryAdvanceIntervalPhase())
            return;

        // Пропуск с паузы не должен сам запускать отсчёт.
        if (wasPaused)
        {
            _timer.Pause();
            PersistTimerSession();
        }

        _sound.PlayPhaseCue();
        RefreshIntervalProperties();
    }

    /// <summary>
    /// Готовая программа одним нажатием: заполняет этапы, круги
    /// и включает режим тренировки. Запускать нужно кнопкой «Старт».
    /// </summary>
    private void ApplyIntervalProgram(string? key)
    {
        if (!(IsIdle || IsFinished))
            return;

        (string workName, int workSeconds, string restName, int restSeconds, int rounds) = key switch
        {
            "tabata" => ("Работа", 20, "Отдых", 10, 8),
            "runbeginner" => ("Бег", 60, "Ходьба", 120, 10),
            "runinterval" => ("Бег", 90, "Ходьба", 120, 10),
            "hiit" => ("Работа", 40, "Отдых", 20, 12),
            "pomodoro" => ("Фокус", 1500, "Отдых", 300, 4),
            _ => ("", 0, "", 0, 0)
        };

        if (workSeconds <= 0)
            return;

        IntervalWorkName = workName;
        IntervalRestName = restName;
        IntervalWorkMinutes = workSeconds / 60;
        IntervalWorkSecondsPart = workSeconds % 60;
        IntervalRestMinutes = restSeconds / 60;
        IntervalRestSecondsPart = restSeconds % 60;
        IntervalRoundsInput = rounds;

        if (!UseIntervalMode)
            UseIntervalMode = true;
    }

    /// <summary>Кнопка «Старт тренировки» на странице телефона.</summary>
    public bool StartIntervalFromPhone()
    {
        if (!(IsIdle || IsFinished))
            return false;

        if (!UseIntervalMode)
            UseIntervalMode = true;

        Start();
        return IsRunning;
    }

    /// <summary>
    /// Переход к следующему этапу программы. Возвращает false, если программа
    /// закончилась (текущий этап был последним) — тогда вызывающий код делает
    /// обычный финиш с будильником и уведомлением.
    /// </summary>
    private bool TryAdvanceIntervalPhase()
    {
        if (!_intervalActive)
            return false;

        if (_intervalPhase == 0)
        {
            // Первый этап последнего круга — дальше только финиш.
            if (HasFiniteIntervalRounds && _intervalRound >= IntervalRounds)
                return false;

            if (IntervalRestDuration > TimeSpan.Zero)
            {
                _intervalPhase = 1;
                _timer.Start(IntervalRestDuration);
            }
            else
            {
                // Второй этап нулевой — сразу следующий круг.
                _intervalPhase = 0;
                _intervalRound++;
                _timer.Start(IntervalWorkDuration);
            }
        }
        else
        {
            _intervalPhase = 0;
            _intervalRound++;
            _timer.Start(IntervalWorkDuration);
        }

        PersistTimerSession();
        return true;
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
        _intervalActive = false;
        _intervalJustFinished = false;
        _timer.Reset();

        // Цифры возвращаются к тому, что запустит «Старт» в текущем режиме.
        TimeSpan display = UseIntervalMode && IntervalWorkDuration > TimeSpan.Zero
            ? IntervalWorkDuration
            : InputDuration;
        _timer.SetDuration(display);
        Remaining = display;
        Progress = display > TimeSpan.Zero ? 1.0 : 0.0;
        PersistTimerSession();
        RefreshIntervalProperties();
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

        var duration = TimeSpan.FromSeconds(seconds);
        ApplyDuration(duration);
        SetSelectedPresetName(TimeFormatter.FormatFriendly(duration));
        TimerName = string.Empty;
    }

    private void ApplyCustomPreset(CustomPreset? preset)
    {
        if (!CanEditInputs || preset is null)
            return;

        ApplyDuration(TimeSpan.FromSeconds(preset.TotalSeconds));
        SetSelectedPresetName(preset.Name);

        // Имя своего пресета — это и есть название таймера: в конце
        // уведомление скажет, например, «Работа — готово!».
        TimerName = preset.Name;
    }

    /// <summary>
    /// Быстрый ввод одной строкой: «15м чай», «1ч 30м обед», «через 20 минут
    /// позвонить» или просто «чай» (только название, время не трогаем).
    /// </summary>
    public bool ApplyQuickInput(string? text)
    {
        if (!CanEditInputs)
        {
            QuickInputFeedback = "Во время отсчёта и паузы ввод недоступен.";
            return false;
        }

        var result = QuickInputParser.Parse(text);
        if (!result.Success)
        {
            QuickInputFeedback =
                "Не понял. Примеры: «15м чай», «1ч 30м обед», " +
                "«через 20 минут позвонить» или просто «чай».";
            return false;
        }

        if (result.Duration is { } duration)
            ApplyDuration(duration);

        // Строка описывает таймер целиком: названия нет — значит, без названия.
        TimerName = result.Name;

        QuickInputFeedback = result.Duration is { } parsed
            ? HasTimerName
                ? $"Принято: {TimeFormatter.FormatFriendly(parsed)} — «{TimerName}»."
                : $"Принято: {TimeFormatter.FormatFriendly(parsed)}."
            : $"Название таймера: «{TimerName}».";
        return true;
    }

    /// <summary>
    /// Быстрый запуск одной строкой — для кнопки на странице телефона:
    /// разобрать «15м чай» и сразу стартовать. Если что-то уже идёт,
    /// ничего не трогаем.
    /// </summary>
    public bool QuickStart(string? text)
    {
        if (!ApplyQuickInput(text))
            return false;

        if (IsIdle || IsFinished)
            Start();

        return true;
    }

    private void DeleteCustomPreset(CustomPreset? preset)
    {
        if (preset is null)
            return;

        CustomPresets.Remove(preset);
        _settings.CustomPresets.Remove(preset);
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(HasCustomPresets));

        // Удалили как раз тот пресет, что сейчас выбран, — подписи убираем.
        if (string.Equals(SelectedPresetName, preset.Name, StringComparison.OrdinalIgnoreCase))
            SetSelectedPresetName(string.Empty);
        if (string.Equals(TimerName, preset.Name, StringComparison.OrdinalIgnoreCase))
            TimerName = string.Empty;
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
        // Любая правка полей ввода означает «время набрано вручную»:
        // подпись пресета убираем. ApplyPreset/ApplyCustomPreset выставят
        // название заново уже после ApplyDuration.
        if (HasSelectedPreset)
            SetSelectedPresetName(string.Empty);

        _settings.LastHours = HoursInput;
        _settings.LastMinutes = MinutesInput;
        _settings.LastSeconds = SecondsInput;
        _settingsService.Save(_settings);

        (StartCommand as RelayCommand)?.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(EndTimeText));

        if (CanEditInputs)
        {
            // Правка длительности после финиша также глушит сигнал. В
            // интервальном режиме на цифрах — первый этап, а не обычный ввод.
            _sound.StopAlarm();
            TimeSpan display = UseIntervalMode && IntervalWorkDuration > TimeSpan.Zero
                ? IntervalWorkDuration
                : InputDuration;
            _timer.SetDuration(display);
            Remaining = display;
            Progress = display > TimeSpan.Zero ? 1.0 : 0.0;
        }
    }

    private void OnTimerTicked(object? sender, EventArgs e)
    {
        double previousSeconds = _remaining.TotalSeconds;

        OnPropertyChanged(nameof(EndTimeText));
        OnPropertyChanged(nameof(StatusBrush));
        Remaining = _timer.Remaining;
        Progress = _timer.Duration > TimeSpan.Zero
            ? Math.Clamp(_timer.Remaining.TotalSeconds / _timer.Duration.TotalSeconds, 0, 1)
            : 0;

        // Предсигнал за 3 секунды до конца этапа тренировки: тот же
        // короткий сигнал, но тише — «приготовься к смене этапа».
        if (_intervalActive && _timer.State == TimerState.Running)
        {
            double nowSeconds = _timer.Remaining.TotalSeconds;
            if (previousSeconds > 3.0 && nowSeconds <= 3.0 && nowSeconds > 0)
                _sound.PlayPhaseWarning();
        }
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

        // Этап тренировки закончился, но программа ещё нет: короткий сигнал
        // и сразу следующий этап — без будильника на 35 секунд и без тоста.
        if (_intervalActive && TryAdvanceIntervalPhase())
        {
            _sound.PlayPhaseCue();
            RefreshIntervalProperties();
            return;
        }

        bool wasInterval = _intervalActive;
        _intervalActive = false;
        _intervalJustFinished = wasInterval;
        PersistTimerSession();
        _sound.PlayAlarm();
        RefreshIntervalProperties();
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
            case TimerState.Running when _intervalActive:
                _settings.SavedTimerState = "IntervalRunning";
                _settings.SavedTimerEndUtcTicks = _timer.EndTimeUtc.UtcTicks;
                _settings.SavedTimerDurationSeconds = _timer.Duration.TotalSeconds;
                _settings.SavedTimerRemainingSeconds = _timer.Remaining.TotalSeconds;
                _settings.SavedIntervalPhase = _intervalPhase;
                _settings.SavedIntervalRound = _intervalRound;
                break;

            case TimerState.Paused when _intervalActive:
                _settings.SavedTimerState = "IntervalPaused";
                _settings.SavedTimerEndUtcTicks = 0;
                _settings.SavedTimerDurationSeconds = _timer.Duration.TotalSeconds;
                _settings.SavedTimerRemainingSeconds = _timer.Remaining.TotalSeconds;
                _settings.SavedIntervalPhase = _intervalPhase;
                _settings.SavedIntervalRound = _intervalRound;
                break;

            case TimerState.Running:
                _settings.SavedTimerState = "Running";
                _settings.SavedTimerEndUtcTicks = _timer.EndTimeUtc.UtcTicks;
                _settings.SavedTimerDurationSeconds = _timer.Duration.TotalSeconds;
                _settings.SavedTimerRemainingSeconds = _timer.Remaining.TotalSeconds;
                _settings.SavedIntervalPhase = 0;
                _settings.SavedIntervalRound = 0;
                break;

            case TimerState.Paused:
                _settings.SavedTimerState = "Paused";
                _settings.SavedTimerEndUtcTicks = 0;
                _settings.SavedTimerDurationSeconds = _timer.Duration.TotalSeconds;
                _settings.SavedTimerRemainingSeconds = _timer.Remaining.TotalSeconds;
                _settings.SavedIntervalPhase = 0;
                _settings.SavedIntervalRound = 0;
                break;

            default:
                _settings.SavedTimerState = string.Empty;
                _settings.SavedTimerEndUtcTicks = 0;
                _settings.SavedTimerDurationSeconds = 0;
                _settings.SavedTimerRemainingSeconds = 0;
                _settings.SavedIntervalPhase = 0;
                _settings.SavedIntervalRound = 0;
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

            // Интервальная тренировка восстанавливается в свой режим: этап и
            // круг сохранены отдельно, а время этапа — общим механизмом.
            bool intervalRunning = string.Equals(
                _settings.SavedTimerState, "IntervalRunning", StringComparison.OrdinalIgnoreCase);
            bool intervalPaused = string.Equals(
                _settings.SavedTimerState, "IntervalPaused", StringComparison.OrdinalIgnoreCase);
            if (intervalRunning || intervalPaused)
            {
                _intervalActive = true;
                _useIntervalMode = true;
                _settings.IntervalModeEnabled = true;
                _intervalPhase = (int)Math.Clamp(_settings.SavedIntervalPhase, 0, 1);
                _intervalRound = (int)Math.Clamp(
                    _settings.SavedIntervalRound <= 0 ? 1 : _settings.SavedIntervalRound, 1, 9999);
                OnPropertyChanged(nameof(UseIntervalMode));

                if (intervalRunning && _settings.SavedTimerEndUtcTicks > 0)
                {
                    var endUtc = new DateTimeOffset(_settings.SavedTimerEndUtcTicks, TimeSpan.Zero);
                    _timer.RestoreRunning(duration, endUtc);
                    Remaining = _timer.Remaining;
                    Progress = _timer.State == TimerState.Finished
                        ? 0
                        : Math.Clamp(_timer.Remaining.TotalSeconds / duration.TotalSeconds, 0, 1);

                    // Этап истёк, пока приложения не было: дальше честно
                    // показываем завершённую тренировку без звука.
                    if (_timer.State == TimerState.Finished)
                    {
                        _intervalActive = false;
                        _intervalJustFinished = true;
                        PersistTimerSession();
                    }
                }
                else if (intervalPaused)
                {
                    _timer.RestorePaused(
                        duration,
                        TimeSpan.FromSeconds(_settings.SavedTimerRemainingSeconds));
                    Remaining = _timer.Remaining;
                    Progress = Math.Clamp(
                        _timer.Remaining.TotalSeconds / duration.TotalSeconds, 0, 1);
                }

                RefreshIntervalProperties();
                return;
            }

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
        OnPropertyChanged(nameof(IsIntervalActive));
        OnPropertyChanged(nameof(IntervalJustFinished));
        OnPropertyChanged(nameof(CurrentIntervalPhaseName));
        OnPropertyChanged(nameof(IntervalRoundText));
        OnPropertyChanged(nameof(ShowSkipPhase));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(FinishedInfoTitle));
        OnPropertyChanged(nameof(DisplayTime));
        OnPropertyChanged(nameof(WindowTitleText));
        OnPropertyChanged(nameof(EndTimeText));

        (StartCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (PauseCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ResumeCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (AddMinuteCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SkipPhaseCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}
