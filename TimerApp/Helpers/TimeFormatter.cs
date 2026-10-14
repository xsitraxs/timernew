using System;

namespace TimerApp.Helpers;

/// <summary>Форматирование времени для крупных цифр и подписей пресетов.</summary>
public static class TimeFormatter
{
    /// <summary>
    /// Формат для главного дисплея: H:MM:SS, если длительность не меньше часа,
    /// иначе MM:SS.
    /// </summary>
    public static string Format(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    /// <summary>Дружелюбный формат: «45 мин», «1 ч 30 мин», «90 сек».</summary>
    public static string FormatFriendly(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            int hours = (int)value.TotalHours;
            return value.Minutes > 0 ? $"{hours} ч {value.Minutes} мин" : $"{hours} ч";
        }

        if (value.TotalMinutes >= 1)
        {
            return value.Seconds > 0
                ? $"{value.Minutes} мин {value.Seconds} сек"
                : $"{value.Minutes} мин";
        }

        return $"{value.Seconds} сек";
    }
}
