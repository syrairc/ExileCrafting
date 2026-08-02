using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ExileCrafting;

public class ExileCrafting : BaseSettingsPlugin<ExileCraftingSettings>
{
    public static ExileCrafting Main;

    public ScriptHost ScriptHost;
    readonly ClickExecutor _executor = new();
    readonly CraftDialog _dialog = new();

    CancellationTokenSource _runCts;
    Task<object> _runTask;
    CraftContext _runningContext;

    public string RunningScriptName { get; private set; }
    public string RunningStatusText { get; private set; }
    public string RunningError { get; private set; }
    public bool IsScriptRunning => _runTask != null;

    // ClickExecutor checks calls against this - scripts must resume on the main thread, since
    // ExileCore's game-state reads aren't safe from a background one.
    public int MainThreadId { get; private set; }

    public override bool Initialise()
    {
        Main = this;
        ScriptHost = new ScriptHost(Path.Combine(ConfigDirectory, "Scripts"));
        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
    }

    public override Job Tick()
    {
        // Tick is the thread we're actually guarding ApplyCurrency against, so capture it here
        // instead of Initialise (which may run on a plugin-loader thread, not this one).
        if (MainThreadId == 0) MainThreadId = Environment.CurrentManagedThreadId;

        var deltaMs = (int)(GameController.DeltaTime * 1000);
        _executor.Tick(deltaMs);

        if (Settings.OpenPickerHotkey.PressedOnce())
            _dialog.IsOpen = !_dialog.IsOpen;

        if (Settings.AbortHotkey.PressedOnce())
            CancelRunningScript();

        if (_runningContext != null && _runTask != null)
            RunningStatusText = _runningContext.StatusText;

        if (_runTask is { IsCompleted: true })
        {
            if (_runTask.IsFaulted)
                RunningError = _runTask.Exception?.GetBaseException().Message ?? "Unknown error.";
            else if (_runTask.IsCanceled)
                RunningStatusText = "Cancelled.";
            else
                RunningStatusText = "Done.";

            _runTask = null;
        }

        return null;
    }

    public override void Render()
    {
        _dialog.Draw(this);
    }

    public override void EntityAdded(Entity entity)
    {
    }

    public void StartScript(string fileName)
    {
        if (_runTask != null) return;

        RunningScriptName = fileName;
        RunningError = null;
        RunningStatusText = "Compiling...";

        if (!ScriptHost.TryGetOrCompile(fileName, out var script, out var compileError))
        {
            RunningError = compileError;
            RunningStatusText = null;
            return;
        }

        RunningStatusText = "Running...";
        _runCts = new CancellationTokenSource();
        _runningContext = new CraftContext(_executor, _runCts.Token, Settings.MaxCurrencyApplications.Value, Settings.MaxRunSeconds.Value);
        _runTask = RunScript(script, _runningContext);
    }

    static async Task<object> RunScript(Script<object> script, CraftContext ctx) =>
        (await script.RunAsync(ctx)).ReturnValue;

    public void CancelRunningScript() => _runCts?.Cancel();

    public void ResetRunState()
    {
        RunningScriptName = null;
        RunningStatusText = null;
        RunningError = null;
        _runningContext = null;
    }
}
