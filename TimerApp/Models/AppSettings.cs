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

    /// <summary>Последняя введённая длительность — часы.</summary>
    public double LastHours { get; set; }

    /// <summary>Последняя введённая длительность — минуты.</summary>
    public double LastMinutes { get; set; } = 5;

    /// <summary>Последняя введённая длительность — секунды.</summary>
    public double LastSeconds { get; set; }

    /// <summary>Сохранённые пользователем пресеты.</summary>
    public List<CustomPreset> CustomPresets { get; set; } = new();
}
