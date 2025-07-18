using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Base;

public class BotSource<T>(RoutineExecutor<T> Bot)
    where T : class, IConsoleBotConfig
{
    public readonly RoutineExecutor<T> Bot = Bot;
    private CancellationTokenSource Source = new();

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }

    private bool IsStopping { get; set; }

    public void Stop()
    {
        if (!IsRunning || IsStopping)
            return;

        IsStopping = true;
        Source.Cancel();
        Source = new CancellationTokenSource();

        Task.Run(async () => await Bot.HardStop()
            .ContinueWith(ReportFailure, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
            .ContinueWith(_ => IsPaused = IsRunning = IsStopping = false));
    }

    public void Pause()
    {
        if (!IsRunning || IsStopping)
            return;

        IsPaused = true;
        Task.Run(Bot.SoftStop)
            .ContinueWith(ReportFailure, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
            .ContinueWith(_ => IsPaused = false, TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Start()
    {
        if (IsPaused)
            Stop(); // can't soft-resume; just re-launch

        if (IsRunning || IsStopping)
            return;

        IsRunning = true;
        Task.Run(async () => await Bot.RunAsync(Source.Token)
            .ContinueWith(ReportFailure, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
            .ContinueWith(_ => IsRunning = false));
    }

    public void Restart()
    {
        bool ok = true;
        Task.Run(Bot.Connection.Reset).ContinueWith(task =>
        {
            ok = false;
            ReportFailure(task);
        }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
        .ContinueWith(_ =>
        {
            if (ok)
                Start();
        }, TaskContinuationOptions.RunContinuationsAsynchronously | TaskContinuationOptions.NotOnFaulted);
    }

    public void RebootAndStop()
    {
        Stop();
        Task.Run(async () =>
        {
            try
            {
                Bot.Connection.Connect();
                await Bot.InitialStartup(Source.Token).ConfigureAwait(false);

                var hub = (dynamic)Bot.GetType()
                    .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .First(f => f.Name.Contains("Hub"))
                    .GetValue(Bot)!;
                await ((dynamic)Bot).ReOpenGame(hub.Config, Source.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Bot.Log($"Failed to re-open game: {ex.Message}");
            }
            await Bot.HardStop().ConfigureAwait(false);
            Bot.Connection.Disconnect();
        })
        .ContinueWith(ReportFailure, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
        .ContinueWith(_ => IsRunning = false);

        IsRunning = true;
    }

    private void ReportFailure(Task finishedTask)
    {
        var ident = Bot.Connection.Name;
        var ae = finishedTask.Exception;
        if (ae == null)
        {
            LogUtil.LogError("Bot has stopped without error.", ident);
            return;
        }

        LogUtil.LogError("Bot has crashed!", ident);

        if (!string.IsNullOrEmpty(ae.Message))
            LogUtil.LogError("Aggregate message: " + ae.Message, ident);

        var st = ae.StackTrace;
        if (!string.IsNullOrEmpty(st))
            LogUtil.LogError("Aggregate stacktrace: " + st, ident);

        foreach (var e in ae.InnerExceptions)
        {
            if (!string.IsNullOrEmpty(e.Message))
                LogUtil.LogError("Inner message: " + e.Message, ident);
            LogUtil.LogError("Inner stacktrace: " + e.StackTrace, ident);
        }
    }

    public void Resume()
    {
        Start();
    }
}
