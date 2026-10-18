using System;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace TimerApp.Services;

/// <summary>
/// Звуковой сигнал окончания таймера.
///
/// Играет встроенный файл Assets/alarm.wav через MediaPlayer (не зависит
/// от системных звуков и работает при свёрнутом окне). Сигнал повторяется
/// до трёх раз, учитывает сохранённую громкость и кнопку отключения звука.
/// Файл можно заменить своим .wav с тем же именем — код менять не нужно.
/// </summary>
public sealed class SoundService : IDisposable
{
    // Один проход WAV — ~1,1 сек. 30 повторов — около 35 секунд сигнала:
    // достаточно, чтобы услышать из другой комнаты, но не бесконечно.
    // Любое действие пользователя (старт/сброс/пауза/mute) глушит раньше.
    private const int AlarmRepeatCount = 30;

    private readonly MediaPlayer _player;
    private readonly MediaPlayer _phasePlayer;
    private int _playsDone;
    private bool _alarmActive;
    private bool _previewOnly;
    private double _effectiveVolume = 0.7;
    private bool _disposed;

    public SoundService()
    {
        _player = new MediaPlayer
        {
            AutoPlay = false
        };

        _player.Source = MediaSource.CreateFromUri(
            new Uri("ms-appx:///Assets/alarm.wav"));
        _player.MediaEnded += OnMediaEnded;

        // Отдельный плеер для коротких сигналов смены этапа тренировки:
        // основной в этот момент может быть занят длинным будильником.
        _phasePlayer = new MediaPlayer
        {
            AutoPlay = false
        };
        _phasePlayer.Source = MediaSource.CreateFromUri(
            new Uri("ms-appx:///Assets/beep.wav"));
    }

    /// <summary>
    /// Выбирает мелодию: встроенную (alarm/chime/beep/gentle по id)
    /// или пользовательский файл (id "custom" + путь к копии файла).
    /// </summary>
    public void SetSound(string? soundId, string? customFilePath)
    {
        StopAlarm();

        try
        {
            Uri uri;

            if (string.Equals(soundId, "custom", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(customFilePath) &&
                System.IO.File.Exists(customFilePath))
            {
                uri = new Uri(customFilePath);
            }
            else
            {
                string file = soundId?.ToLowerInvariant() switch
                {
                    "chime" => "chime.wav",
                    "beep" => "beep.wav",
                    "gentle" => "gentle.wav",
                    _ => "alarm.wav"
                };
                uri = new Uri("ms-appx:///Assets/" + file);
            }

            _player.Source = MediaSource.CreateFromUri(uri);
        }
        catch (Exception)
        {
            // Битый пользовательский файл не должен оставлять без звука:
            // откатываемся на встроенный сигнал.
            try
            {
                _player.Source = MediaSource.CreateFromUri(
                    new Uri("ms-appx:///Assets/alarm.wav"));
            }
            catch (Exception)
            {
                // Плеер уже освобождён.
            }
        }
    }

    /// <summary>Применяет громкость (0..1) и состояние mute.</summary>
    public void ApplyVolume(double volume01, bool isMuted)
    {
        _effectiveVolume = isMuted ? 0.0 : Math.Clamp(volume01, 0.0, 1.0);
        _player.Volume = _effectiveVolume;
        _phasePlayer.Volume = _effectiveVolume;
    }

    /// <summary>Выбирает мелодию короткого сигнала смены этапа (только встроенные).</summary>
    public void SetPhaseSound(string? soundId)
    {
        try
        {
            string file = soundId?.ToLowerInvariant() switch
            {
                "alarm" => "alarm.wav",
                "chime" => "chime.wav",
                "gentle" => "gentle.wav",
                _ => "beep.wav"
            };
            _phasePlayer.Source = MediaSource.CreateFromUri(
                new Uri("ms-appx:///Assets/" + file));
        }
        catch (Exception)
        {
            // Плеер уже освобождён.
        }
    }

    /// <summary>Короткий сигнал: этап сменился.</summary>
    public void PlayPhaseCue()
    {
        _phasePlayer.Volume = _effectiveVolume;
        PlayPhaseOnce();
    }

    /// <summary>Тихий предсигнал за пару секунд до конца этапа: «приготовься».</summary>
    public void PlayPhaseWarning()
    {
        _phasePlayer.Volume = _effectiveVolume * 0.6;
        PlayPhaseOnce();
    }

    private void PlayPhaseOnce()
    {
        if (_disposed)
            return;

        try
        {
            _phasePlayer.PlaybackSession.Position = TimeSpan.Zero;
            _phasePlayer.Play();
        }
        catch (Exception)
        {
            // Плеер уже освобождён при закрытии приложения.
        }
    }

    /// <summary>Запускает сигнал окончания (до ~35 секунд или до действия пользователя).</summary>
    public void PlayAlarm()
    {
        _alarmActive = true;
        _previewOnly = false;
        _playsDone = 0;
        PlayOnce();
    }

    /// <summary>Однократная проверка звука кнопкой «Проверка».</summary>
    public void PlayPreview()
    {
        _alarmActive = true;
        _previewOnly = true;
        _playsDone = 0;
        PlayOnce();
    }

    /// <summary>Останавливает сигнал (сброс, старт нового отсчёта, закрытие InfoBar).</summary>
    public void StopAlarm()
    {
        _alarmActive = false;
        try
        {
            _player.Pause();
            _player.PlaybackSession.Position = TimeSpan.Zero;
        }
        catch (Exception)
        {
            // Плеер уже освобождён при закрытии приложения.
        }
    }

    private void PlayOnce()
    {
        if (!_alarmActive || _disposed)
            return;

        try
        {
            _playsDone++;
            _player.PlaybackSession.Position = TimeSpan.Zero;
            _player.Play();
        }
        catch (Exception)
        {
            _alarmActive = false;
        }
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        if (!_alarmActive)
            return;

        int limit = _previewOnly ? 1 : AlarmRepeatCount;
        if (_playsDone < limit)
        {
            PlayOnce();
        }
        else
        {
            _alarmActive = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _alarmActive = false;
        _player.MediaEnded -= OnMediaEnded;
        _player.Dispose();
        _phasePlayer.Dispose();
    }
}
