using System.Collections.Generic;

namespace TimerApp.Models;

/// <summary>Настройки, сохраняемые между запусками приложения.</summary>
public sealed class AppSettings
{
    /// <summary>"Dark" (по умолчанию) или "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Громкость сигнала от 0.0 до 1.0.</summary>
    public double Volume { get; set; } = 0.7;

    /// <summary>Звук отключён кнопкой mute.</summary>
    public bool IsMuted { get; set; }

    /// <summary>Выбранная мелодия: alarm, chime, beep, gentle или custom.</summary>
    public string SoundId { get; set; } = "alarm";

    /// <summary>Имя скопированного пользовательского файла звука в LocalFolder.</summary>
    public string CustomSoundFileName { get; set; } = string.Empty;

    /// <summary>Последняя введённая длительность — часы.</summary>
    public double LastHours { get; set; }

    /// <summary>Последняя введённая длительность — минуты.</summary>
    public double LastMinutes { get; set; } = 5;

    /// <summary>Последняя введённая длительность — секунды.</summary>
    public double LastSeconds { get; set; }

    /// <summary>
    /// Название последнего выбранного пресета (пусто — длительность
    /// набрана вручную). Показывается под цифрами таймера.
    /// </summary>
    public string LastPresetName { get; set; } = string.Empty;

    /// <summary>
    /// Название текущего таймера из быстрого ввода («15м чай» → «чай»).
    /// Показывается в окне и в уведомлении по окончании.
    /// </summary>
    public string LastTimerName { get; set; } = string.Empty;

    // --- Сессия таймера: чтобы отсчёт переживал закрытие/падение/перезагрузку ---

    /// <summary>Состояние сохранённого таймера: "Running", "Paused" или пусто.</summary>
    public string SavedTimerState { get; set; } = string.Empty;

    /// <summary>Момент окончания (UTC ticks) для запущенного таймера.</summary>
    public long SavedTimerEndUtcTicks { get; set; }

    /// <summary>Полная длительность сохранённого таймера, секунды.</summary>
    public double SavedTimerDurationSeconds { get; set; }

    /// <summary>Оставшееся время сохранённого таймера на паузе, секунды.</summary>
    public double SavedTimerRemainingSeconds { get; set; }

    /// <summary>Сохранённые пользователем пресеты.</summary>
    public List<CustomPreset> CustomPresets { get; set; } = new();

    // --- Интервальный режим: бег/ходьба/тренировка по циклам ---

    /// <summary>Включён ли интервальный режим двух этапов.</summary>
    public bool IntervalModeEnabled { get; set; }

    /// <summary>Название первого этапа интервалов.</summary>
    public string IntervalWorkName { get; set; } = "Бег";

    /// <summary>Название второго этапа интервалов.</summary>
    public string IntervalRestName { get; set; } = "Ходьба";

    /// <summary>Длительность первого этапа, секунды.</summary>
    public double IntervalWorkSeconds { get; set; } = 90;

    /// <summary>Длительность второго этапа, секунды.</summary>
    public double IntervalRestSeconds { get; set; } = 120;

    /// <summary>Количество кругов интервалов. 0 — бесконечно.</summary>
    public double IntervalRounds { get; set; } = 10;

    /// <summary>
    /// Управление с телефона: приложение раздаёт страницу управления по
    /// домашней Wi-Fi сети (QR-код в разделе «Управление с телефона»).
    /// </summary>
    public bool PhoneRemoteEnabled { get; set; }

    /// <summary>Этап сохранённой интервальной сессии: 0 — первый, 1 — второй.</summary>
    public long SavedIntervalPhase { get; set; }

    /// <summary>Круг сохранённой интервальной сессии (счёт с 1).</summary>
    public long SavedIntervalRound { get; set; }
}
