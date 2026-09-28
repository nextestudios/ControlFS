using System.Diagnostics;
using ControlFS.Application;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Windows.Diagnostics;
using ControlFS.Infrastructure.Windows.FileSystem;
using Microsoft.UI.Dispatching;

namespace ControlFS.App.Navigation;

/// <summary>
/// "Leve em segundo plano" (<see cref="BackgroundModePolicy"/>, docs/performance.md): aplica as decisões ao processo —
/// prioridade e EcoQoS, pausa do monitor de unidades e devolução da memória. Um temporizador de 1 s só roda com a janela
/// em segundo plano e enquanto ainda há algo a decidir (a devolução agendada, ou a mídia que ainda toca).
/// </summary>
internal sealed class BackgroundMode
{
    private readonly AppController _app;
    private readonly DriveWatcher _drives;
    private readonly BackgroundModePolicy _policy = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _active = true;

    public BackgroundMode(AppController app, DispatcherQueue queue, DriveWatcher drives)
    {
        _app = app;
        _drives = drives;
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Update();
        app.SettingsChanged += _ => Update();
    }

    public void OnWindowActivated(bool active)
    {
        _active = active;
        Update();
    }

    private void Update()
    {
        var enabled = _app.Settings.LightInBackground;
        var effects = _policy.Update(_active, enabled, _app.IsMediaPlaying, _clock.Elapsed);
        if (effects.HasFlag(BackgroundEffects.Lower))
        {
            ProcessEfficiency.SetBackground(true);
            _drives.Pause();
        }
        if (effects.HasFlag(BackgroundEffects.Restore))
        {
            ProcessEfficiency.SetBackground(false);
            _drives.Resume();
        }
        if (effects.HasFlag(BackgroundEffects.Trim)) _ = Task.Run(Trim);
        var pending = !_active && enabled && (!_policy.IsLowered || _policy.TrimDue is not null);
        if (pending && !_timer.IsRunning) _timer.Start();
        else if (!pending && _timer.IsRunning) _timer.Stop();
    }

    private static void Trim()
    {
        using var process = Process.GetCurrentProcess();
        var before = process.WorkingSet64;
        ProcessEfficiency.TrimMemory();
        process.Refresh();
        AppLog.Info($"Segundo plano: memória devolvida ({before / (1024 * 1024)} MB → {process.WorkingSet64 / (1024 * 1024)} MB no conjunto de trabalho)");
    }
}
