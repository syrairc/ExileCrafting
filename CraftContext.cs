using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExileCrafting;

public sealed class CraftContext
{
    readonly ClickExecutor _executor;
    readonly CancellationToken _token;
    readonly DateTime _startedAt;
    readonly int _maxApplications;
    readonly int _maxSeconds;
    int _applications;

    public CraftContext(ClickExecutor executor, CancellationToken token, int maxApplications, int maxSeconds)
    {
        _executor = executor;
        _token = token;
        _maxApplications = maxApplications;
        _maxSeconds = maxSeconds;
        _startedAt = DateTime.UtcNow;
    }

    // globals type puts our members directly in script scope, not a `ctx` variable - this lets
    // scripts still write ctx.Item / ctx.ApplyCurrency(...) like the docs and spec show.
    public CraftContext ctx => this;

    public CraftItem Item => CraftingSlot.ReadItem();

    public string StatusText { get; private set; } = "";

    public void Log(string message) => StatusText = message;

    public async Task<bool> ApplyCurrency(string currencyName)
    {
        _token.ThrowIfCancellationRequested();

        _applications++;
        if (_applications > _maxApplications)
            throw new InvalidOperationException($"Safety guard: exceeded {_maxApplications} currency applications for this run.");

        if ((DateTime.UtcNow - _startedAt).TotalSeconds > _maxSeconds)
            throw new InvalidOperationException($"Safety guard: exceeded {_maxSeconds}s for this run.");

        return await _executor.ApplyCurrency(currencyName, _token);
    }
}
