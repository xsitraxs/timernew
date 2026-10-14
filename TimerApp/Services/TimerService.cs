using System;
using Microsoft.UI.Dispatching;
using TimerApp.Models;

namespace TimerApp.Services;

/// <summary>
/// Движок обратного отсчёта.
///
/// Ключевой принцип: оставшееся время НЕ декрементируется по тикам.
/// При старте/возобновлении фиксируется фактический момент окончания
/// (<see cref="EndTimeUtc"/>), а каждый тик интерфейса лишь пересчитывает
/// <c>EndTimeUtc - UtcNow</c>. Поэтому задержки UI, перерисовка кольца и
/// сворачивание окна не накапливают ошибку. Тик нужен только для плавной
/// анимации кольца (каждые 50 мс).
///
/// Сервис создаётся в UI-потоке, поэтому DispatcherQueueTimer и все события
/// также вызываются в UI-потоке. DispatcherQueueTimer продолжает работать
/// при свёрнутом окне настольного приложения.
/// </summary>
public sealed class TimerService : IDisposable
{
    private readonly DispatcherQueueTimer _timer;
    private bool _disposed;

    public TimerService()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
    }

    /// <summary>Текущее состояние.</summary>
    public TimerState State { get; private set; } = TimerState.Idle;

    /// <summary>Заданная длительность текущего отсчёта.</summary>
    public TimeSpan Duration { get; private set; } = TimeSpan.Zero;

    /// <summary>Оставшееся время (на паузе — зафиксированное значение).</summary>
    public TimeSpan Remaining { get; private set; } = TimeSpan.Zero;

    /// <summary>Фактический момент окончания (UTC) при запущенном отсчёте.</summary>
    public DateTimeOffset EndTimeUtc { get; private set; }

    /// <summary>Тик перерисовки (50 мс) с актуальным Remaining.</summary>
    public event EventHandler? Ticked;

    /// <summary>Состояние изменилось (Idle/Running/Paused/Finished).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Отсчёт дошёл до нуля. Вызывается ровно один раз.</summary>
    public event EventHandler? Completed;

    /// <summary>Задаёт длительность в состояниях Idle/Finished.</summary>
    public void SetDuration(TimeSpan duration)
    {
        if (State is TimerState.Running or TimerState.Paused)
            return;

        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        Remaining = Duration;

        if (State == TimerState.Finished)
            SetState(TimerState.Idle);

        Ticked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Запускает новый отсчёт на указанную длительность.</summary>
    public void Start(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        Duration = duration;
        Remaining = duration;
        EndTimeUtc = DateTimeOffset.UtcNow + duration;

        SetState(TimerState.Running);
        _timer.Start();
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ставит отсчёт на паузу, запоминая точное оставшееся время.</summary>
    public void Pause()
    {
        if (State != TimerState.Running)
            return;

        Remaining = GetLiveRemaining();
        _timer.Stop();
        SetState(TimerState.Paused);
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Продолжает отсчёт: новый дедлайн = сейчас + запомненный остаток.</summary>
    public void Resume()
    {
        if (State != TimerState.Paused)
            return;

        if (Remaining <= TimeSpan.Zero)
        {
            Complete();
            return;
        }

        EndTimeUtc = DateTimeOffset.UtcNow + Remaining;
        SetState(TimerState.Running);
        _timer.Start();
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Добавляет время к идущему или приостановленному отсчёту
    /// (кнопка «+1 мин»): дедлайн и длительность честно сдвигаются.
    /// </summary>
    public void AddTime(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero)
            return;

        if (State == TimerState.Running)
        {
            Duration += delta;
            EndTimeUtc += delta;
            Remaining = GetLiveRemaining();
        }
        else if (State == TimerState.Paused)
        {
            Duration += delta;
            Remaining += delta;
        }
        else
        {
            return;
        }

        Ticked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Сбрасывает таймер к заданной длительности.</summary>
    public void Reset()
    {
        _timer.Stop();
        Remaining = Duration;
        SetState(TimerState.Idle);
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (State != TimerState.Running)
            return;

        Remaining = GetLiveRemaining();

        if (Remaining <= TimeSpan.Zero)
        {
            Complete();
            return;
        }

        Ticked?.Invoke(this, EventArgs.Empty);
    }

    private TimeSpan GetLiveRemaining()
    {
        TimeSpan left = EndTimeUtc - DateTimeOffset.UtcNow;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    private void Complete()
    {
        _timer.Stop();
        Remaining = TimeSpan.Zero;
        SetState(TimerState.Finished);
        Ticked?.Invoke(this, EventArgs.Empty);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(TimerState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
