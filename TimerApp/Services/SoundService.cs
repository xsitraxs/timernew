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
    private const int AlarmRepeatCount = 3;

    private readonly MediaPlayer _player;
    private int _playsDone;
    private bool _alarmActive;
    private bool _previewOnly;
    private bool _disposed;

    public SoundService()
    {
        _player = new MediaPlayer
        {
            AutoPlay = false,
            AudioCategory = MediaPlaybackAudioCategory.SoundEffects
        };

        _player.Source = MediaSource.CreateFromUri(
            new Uri("ms-appx:///Assets/alarm.wav"));
        _player.MediaEnded += OnMediaEnded;
    }

    /// <summary>Применяет громкость (0..1) и состояние mute.</summary>
    public void ApplyVolume(double volume01, bool isMuted)
    {
        _player.Volume = isMuted ? 0.0 : Math.Clamp(volume01, 0.0, 1.0);
    }

    /// <summary>Запускает сигнал окончания (повтор до 3 раз).</summary>
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
    }
}
