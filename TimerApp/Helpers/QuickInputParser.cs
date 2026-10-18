using System;
using System.Collections.Generic;
using System.Globalization;

namespace TimerApp.Helpers;

/// <summary>Результат разбора строки быстрого ввода.</summary>
public readonly struct QuickInputResult
{
    public QuickInputResult(TimeSpan? duration, string name)
    {
        Duration = duration;
        Name = name;
    }

    /// <summary>Разобранная длительность или null — длительность не задана.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>Название таймера (всё, что не длительность).</summary>
    public string Name { get; }

    /// <summary>Поняли хоть что-то: время или название.</summary>
    public bool Success => Duration.HasValue || !string.IsNullOrWhiteSpace(Name);
}

/// <summary>
/// Разбор свободного ввода вида «15м чай», «1ч 30м обед», «через 20 минут
/// позвонить маме», «12:30 суп» или просто «чай». Число без единицы
/// считается минутами; единицы: ч/час/h, м/мин/m, с/сек/s (слитно или
/// через пробел). Время формата «мм:сс» и «чч:мм:сс» тоже понимается.
/// Служебные слова («через», «на», «таймер», «поставь») выбрасываются.
/// </summary>
public static class QuickInputParser
{
    private static readonly Dictionary<string, double> UnitMultipliers = new()
    {
        ["ч"] = 3600, ["час"] = 3600, ["часа"] = 3600, ["часов"] = 3600,
        ["h"] = 3600, ["hr"] = 3600, ["hour"] = 3600, ["hours"] = 3600,
        ["м"] = 60, ["мин"] = 60, ["минута"] = 60, ["минуты"] = 60, ["минут"] = 60,
        ["m"] = 60, ["min"] = 60, ["mins"] = 60, ["minute"] = 60, ["minutes"] = 60,
        ["с"] = 1, ["сек"] = 1, ["секунда"] = 1, ["секунды"] = 1, ["секунд"] = 1,
        ["s"] = 1, ["sec"] = 1, ["secs"] = 1, ["second"] = 1, ["seconds"] = 1
    };

    /// <summary>Слова без смысловой нагрузки для названия таймера.</summary>
    private static readonly HashSet<string> FillerWords = new()
    {
        "через", "на", "таймер", "поставь", "поставить", "запусти", "запустить",
        "включи", "мне"
    };

    public static QuickInputResult Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new QuickInputResult(null, string.Empty);

        string[] tokens = input.Split(
            new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries);

        double totalSeconds = 0;
        bool hasDuration = false;
        var nameTokens = new List<string>();

        for (int i = 0; i < tokens.Length; i++)
        {
            string rawToken = tokens[i];
            string token = rawToken.Trim(',', ';').ToLowerInvariant();

            // «12:30» / «1:05:00» — минуты:секунды / часы:минуты:секунды.
            if (token.Contains(':') &&
                TryParseColonTime(token, out double colonSeconds))
            {
                totalSeconds += colonSeconds;
                hasDuration = true;
                continue;
            }

            // Число, после которого отдельным словом идёт единица: «20 минут».
            if (TryParseNumber(token, out double spacedNumber) &&
                i + 1 < tokens.Length &&
                UnitMultipliers.TryGetValue(
                    tokens[i + 1].Trim(',', ';').ToLowerInvariant(),
                    out double spacedMultiplier))
            {
                totalSeconds += spacedNumber * spacedMultiplier;
                hasDuration = true;
                i++; // единицу съели
                continue;
            }

            // Число с единицей слитно или просто число: «15м», «1ч30м», «45».
            if (TryParseGluedToken(token, out double gluedSeconds))
            {
                totalSeconds += gluedSeconds;
                hasDuration = true;
                continue;
            }

            // Отдельно стоящая единица без числа («…м») — информации нет.
            if (UnitMultipliers.ContainsKey(token))
                continue;

            // Служебные слова в название не попадают.
            if (FillerWords.Contains(token))
                continue;

            // Токен из одной пунктуации («—», «,») названия не добавляет.
            string nameToken = rawToken.Trim(',', ';');
            if (nameToken.Length > 0)
                nameTokens.Add(nameToken);
        }

        string name = string.Join(" ", nameTokens).Trim(' ', '-', '—', ':', ',', ';');

        TimeSpan? duration = null;
        if (hasDuration && totalSeconds > 0)
            duration = TimeSpan.FromSeconds(Math.Round(totalSeconds));

        return new QuickInputResult(duration, name);
    }

    /// <summary>«12:30» — минуты:секунды; «1:05:00» — часы:минуты:секунды.</summary>
    private static bool TryParseColonTime(string token, out double totalSeconds)
    {
        totalSeconds = 0;
        string[] parts = token.Split(':');
        if (parts.Length is < 2 or > 3)
            return false;

        double seconds = 0;
        double multiplier = parts.Length == 2 ? 60 : 3600;
        foreach (string part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value) || value < 0)
                return false;

            seconds += value * multiplier;
            multiplier /= 60;
        }

        totalSeconds = seconds;
        return true;
    }

    /// <summary>Токен целиком число («15», «1,5», «1.5»).</summary>
    private static bool TryParseNumber(string token, out double value)
    {
        value = 0;
        if (token.Length == 0)
            return false;

        foreach (char c in token)
        {
            if (!char.IsDigit(c) && c != '.' && c != ',')
                return false;
        }

        return double.TryParse(token.Replace(',', '.'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Слитная запись: один и более кусочков «число+единица» подряд
    /// («15м», «1ч30м», «90сек»). Одно голое число — это минуты.
    /// </summary>
    private static bool TryParseGluedToken(string token, out double totalSeconds)
    {
        totalSeconds = 0;
        int pos = 0;
        bool any = false;

        while (pos < token.Length)
        {
            int numberStart = pos;
            while (pos < token.Length &&
                   (char.IsDigit(token[pos]) || token[pos] == '.' || token[pos] == ','))
                pos++;

            if (pos == numberStart ||
                !double.TryParse(token[numberStart..pos].Replace(',', '.'),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return false;

            int unitStart = pos;
            while (pos < token.Length && char.IsLetter(token[pos]))
                pos++;

            string unit = token[unitStart..pos];
            if (unit.Length == 0)
            {
                // Голое число — минуты («45» = 45 минут).
                totalSeconds += number * 60;
                any = true;
            }
            else if (UnitMultipliers.TryGetValue(unit, out double multiplier))
            {
                totalSeconds += number * multiplier;
                any = true;
            }
            else
            {
                return false;
            }
        }

        return any;
    }
}
