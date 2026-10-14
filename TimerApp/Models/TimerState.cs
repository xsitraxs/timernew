namespace TimerApp.Models;

/// <summary>Состояния таймера, которые явно показываются пользователю.</summary>
public enum TimerState
{
    /// <summary>Таймер не запущен, идёт настройка длительности.</summary>
    Idle,

    /// <summary>Идёт обратный отсчёт.</summary>
    Running,

    /// <summary>Отсчёт приостановлен, оставшееся время зафиксировано.</summary>
    Paused,

    /// <summary>Время вышло.</summary>
    Finished
}
