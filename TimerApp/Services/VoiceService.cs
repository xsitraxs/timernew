using System;
using System.Linq;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace TimerApp.Services;

/// <summary>
/// Голосовые объявления таймера: Windows синтезирует речь (предпочтительно
/// русский голос, если установлен в системе), а отдельный MediaPlayer её
/// озвучивает. Работает при свёрнутом окне, учитывает громкость приложения
/// и кнопку mute. Отсутствие голосовых компонентов Windows — не ошибка:
/// объявления просто молча пропускаются.
/// </summary>
public sealed class VoiceService : IDisposable
{
    private readonly MediaPlayer _player;
    private SpeechSynthesizer? _synth;
    private bool _synthBroken;
    private SpeechSynthesisStream? _currentStream;
    private int _speakSequence;
    private bool _enabled = true;
    private double _volume01 = 0.7;
    private bool _isMuted;
    private bool _disposed;

    public VoiceService()
    {
        _player = new MediaPlayer { AutoPlay = false };

        try
        {
            _player.AudioCategory = MediaPlayerAudioCategory.Speech;
        }
        catch (Exception)
        {
            // Категория звука — мелочь: на части редакций Windows она недоступна.
        }
    }

    /// <summary>Применяет настройки: включены ли объявления, громкость, mute.</summary>
    public void Configure(bool enabled, double volume01, bool isMuted)
    {
        _enabled = enabled;
        _volume01 = Math.Clamp(volume01, 0.0, 1.0);
        _isMuted = isMuted;

        try
        {
            _player.Volume = isMuted ? 0.0 : _volume01;

            if (!enabled || isMuted)
                _player.Pause();
        }
        catch (Exception)
        {
            // Плеер уже освобождён.
        }
    }

    /// <summary>
    /// Произносит фразу, прерывая предыдущую. Никогда не бросает исключений
    /// наружу: нет русского голоса — просто молчим.
    /// </summary>
    public async void Speak(string text)
    {
        if (_disposed || !_enabled || _isMuted || _volume01 <= 0 ||
            string.IsNullOrWhiteSpace(text))
            return;

        int sequence = ++_speakSequence;

        try
        {
            var synth = GetSynthesizer();
            if (synth is null)
                return;

            var stream = await synth.SynthesizeTextToStreamAsync(text);

            // Пока синтезировали, могли попросить другую фразу или стоп.
            if (_disposed || sequence != _speakSequence)
            {
                stream.Dispose();
                return;
            }

            var previous = _currentStream;
            _currentStream = stream;
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Volume = _isMuted ? 0.0 : _volume01;
            _player.Play();
            previous?.Dispose();
        }
        catch (Exception)
        {
            // Нет ни одного голоса в системе или синтез недоступен —
            // объявления тихо отключаются, таймер работает как обычно.
            _synthBroken = true;
        }
    }

    /// <summary>Прерывает текущую фразу и отменяет ещё не озвученные.</summary>
    public void Stop()
    {
        _speakSequence++;

        try
        {
            _player.Pause();
        }
        catch (Exception)
        {
            // Плеер уже освобождён.
        }
    }

    private SpeechSynthesizer? GetSynthesizer()
    {
        if (_synthBroken)
            return null;

        if (_synth is not null)
            return _synth;

        try
        {
            _synth = new SpeechSynthesizer();

            // Русский голос предпочитаем: на русской Windows он обычно есть.
            var russian = SpeechSynthesizer.AllVoices.FirstOrDefault(v =>
                v.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
            if (russian is not null)
                _synth.Voice = russian;

            return _synth;
        }
        catch (Exception)
        {
            _synthBroken = true;
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _currentStream?.Dispose();
        _synth?.Dispose();
        _player.Dispose();
    }
}
