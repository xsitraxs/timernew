using System;
using System.Text.Json.Serialization;
using TimerApp.Helpers;

namespace TimerApp.Models;

/// <summary>Пользовательский пресет длительности.</summary>
public sealed class CustomPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Полная длительность пресета в секундах.</summary>
    public int TotalSeconds { get; set; }

    /// <summary>Человекочитаемая длительность, например «1 ч 30 мин».</summary>
    [JsonIgnore]
    public string DurationText =>
        TimeFormatter.FormatFriendly(TimeSpan.FromSeconds(TotalSeconds));
}
