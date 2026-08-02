using System;
using System.Collections;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using Humanizer;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace ExileCrafting;

public sealed class ClickExecutor
{
    const int HoverConfirmTimeoutMs = 2000;
    const int CursorConfirmTimeoutMs = 2000;
    const int ApplyResultTimeoutMs = 3000;
    const int PollIntervalMs = 33;

    readonly HumanizerConfig _humanizerConfig = new();
    readonly Random _rng = new();

    IEnumerator _routine;
    TaskCompletionSource<bool> _tcs;
    CancellationToken _token;
    int _waitRemainingMs;
    bool _result;
    bool _pollResult;

    public Task<bool> ApplyCurrency(string currencyName, CancellationToken token)
    {
        if (Environment.CurrentManagedThreadId != ExileCrafting.Main.MainThreadId)
            throw new InvalidOperationException(
                "ApplyCurrency was called off the main thread. This usually means the script awaited " +
                "something other than ctx.ApplyCurrency (e.g. Task.Delay) - that resumes on a threadpool " +
                "thread, and ExileCore's game state isn't safe to read from there. Only await " +
                "ctx.ApplyCurrency(...) in crafting scripts, not other Task-returning calls.");

        if (_routine != null)
            throw new InvalidOperationException("ClickExecutor already has an operation in flight.");

        // deliberately not RunContinuationsAsynchronously: Tick() below completes this TCS from the
        // main thread, and we want the script's next line to resume synchronously right there, not
        // hop to a threadpool thread where ExileCore's game-state reads aren't safe to call from.
        _tcs = new TaskCompletionSource<bool>();
        _token = token;
        _waitRemainingMs = 0;
        _routine = ApplyCurrencyRoutine(currencyName);
        return _tcs.Task;
    }

    public void Tick(int deltaMs)
    {
        if (_routine == null) return;

        if (_token.IsCancellationRequested)
        {
            var cancelledTcs = _tcs;
            _routine = null;
            _tcs = null;
            cancelledTcs.TrySetCanceled(_token);
            return;
        }

        _waitRemainingMs -= deltaMs;
        if (_waitRemainingMs > 0) return;

        bool hasNext;
        try
        {
            hasNext = _routine.MoveNext();
        }
        catch (Exception e)
        {
            var faultedTcs = _tcs;
            _routine = null;
            _tcs = null;
            faultedTcs.TrySetException(e);
            return;
        }

        if (!hasNext)
        {
            var doneTcs = _tcs;
            var result = _result;
            _routine = null;
            _tcs = null;
            doneTcs.TrySetResult(result);
            return;
        }

        _waitRemainingMs = _routine.Current is WaitTime wait ? wait.Milliseconds : PollIntervalMs;
    }

    IEnumerator ApplyCurrencyRoutine(string currencyName)
    {
        _result = false;

        // block before touching anything if the cursor's already holding an item - either a
        // stranded currency from a cancelled/faulted previous run, or the player's own pickup.
        var cursor = ExileCrafting.Main.GameController.IngameState.IngameUi.Cursor;
        if (cursor != null && (cursor.Action == MouseActionType.HoldItem || cursor.Action == MouseActionType.UseItem))
            yield break;

        var startAddress = CraftingSlot.ReadItem().Address;

        // resolve the slot before we touch the currency - both checks below used to happen after
        // the pickup right-click, which could strand the currency on the cursor if they failed.
        var slot = CraftingSlot.FindClickableSlot();
        if (slot == null) yield break;

        var currencyItem = CraftingSlot.FindCurrencyInStash(currencyName);
        if (currencyItem == null) yield break;

        var currencyCenter = Center(currencyItem.GetClientRectCache);

        foreach (var step in MoveMouseTo(currencyCenter)) yield return step;

        foreach (var step in Poll(() => IsHovering(currencyItem.Item.Address), HoverConfirmTimeoutMs)) yield return step;
        if (!_pollResult) yield break;

        foreach (var step in ClickButton(MouseButtons.Right)) yield return step;
        // currency is on the cursor from here on - any abort below tries to drop it back first.

        foreach (var step in Poll(() => IsCursorHolding(currencyName), CursorConfirmTimeoutMs)) yield return step;
        if (!_pollResult) yield break;

        if (CraftingSlot.ReadItem().Address != startAddress)
        {
            foreach (var step in ReturnHeldCurrencyToStash(currencyCenter, currencyItem.Item.Address)) yield return step;
            yield break;
        }

        // slot was resolved before the pickup - re-check it still points at the same item.
        if (slot.Item?.Address != startAddress)
        {
            foreach (var step in ReturnHeldCurrencyToStash(currencyCenter, currencyItem.Item.Address)) yield return step;
            yield break;
        }

        foreach (var step in MoveMouseTo(Center(slot.GetClientRectCache))) yield return step;

        foreach (var step in Poll(() => IsHovering(slot.Item.Address), HoverConfirmTimeoutMs)) yield return step;
        if (!_pollResult)
        {
            foreach (var step in ReturnHeldCurrencyToStash(currencyCenter, currencyItem.Item.Address)) yield return step;
            yield break;
        }

        foreach (var step in ClickButton(MouseButtons.Left)) yield return step;

        foreach (var step in Poll(() => CraftingSlot.ReadItem().Address != startAddress, ApplyResultTimeoutMs)) yield return step;
        _result = _pollResult;
    }

    // best effort only: click the currency's own stash slot to drop it back where it came from.
    // hover-confirms first like every other click here - if the hover never lands, leaving the
    // currency held beats clicking blind onto whatever's now under the cursor.
    IEnumerable ReturnHeldCurrencyToStash(Vector2 currencyCenter, long currencyEntityAddress)
    {
        foreach (var step in MoveMouseTo(currencyCenter)) yield return step;

        foreach (var step in Poll(() => IsHovering(currencyEntityAddress), HoverConfirmTimeoutMs)) yield return step;
        if (!_pollResult) yield break;

        foreach (var step in ClickButton(MouseButtons.Left)) yield return step;
    }

    IEnumerable Poll(Func<bool> condition, int timeoutMs)
    {
        var elapsed = 0;
        while (elapsed < timeoutMs)
        {
            if (condition())
            {
                _pollResult = true;
                yield break;
            }
            yield return new WaitTime(PollIntervalMs);
            elapsed += PollIntervalMs;
        }
        _pollResult = false;
    }

    IEnumerable MoveMouseTo(Vector2 target)
    {
        if (ExileCrafting.Main.Settings.UseHumanizedMouse.Value)
        {
            var move = new HumanInput(_humanizerConfig, _rng).MoveTo(target);
            while (move.MoveNext()) yield return move.Current;
        }
        else
        {
            Input.SetCursorPos(target);
            yield return new WaitTime(30);
        }
    }

    IEnumerable ClickButton(MouseButtons button)
    {
        if (ExileCrafting.Main.Settings.UseHumanizedMouse.Value)
        {
            var click = new HumanInput(_humanizerConfig, _rng).Click(button);
            while (click.MoveNext()) yield return click.Current;
        }
        else
        {
            if (button == MouseButtons.Right) Input.RightDown(); else Input.LeftDown();
            yield return new WaitTime(40);
            if (button == MouseButtons.Right) Input.RightUp(); else Input.LeftUp();
            yield return new WaitTime(30);
        }
    }

    // GetClientRectCache is game-window-relative but Input.SetCursorPos / Cursor.Position are
    // absolute screen coords - add the window's top-left so every caller gets a point that's
    // actually clickable in windowed mode or on a secondary monitor.
    static Vector2 Center(RectangleF rect)
    {
        var offset = ExileCrafting.Main.GameController.Window.GetWindowRectangle().TopLeft;
        return new Vector2(rect.X + rect.Width / 2f + offset.X, rect.Y + rect.Height / 2f + offset.Y);
    }

    static bool IsHovering(long entityAddress) =>
        ExileCrafting.Main.GameController.IngameState.UIHoverElement?.Entity?.Address == entityAddress;

    static bool IsCursorHolding(string baseName)
    {
        var cursor = ExileCrafting.Main.GameController.IngameState.IngameUi.Cursor;
        if (cursor == null) return false;
        if (cursor.Action != MouseActionType.HoldItem && cursor.Action != MouseActionType.UseItem) return false;
        return cursor.ItemType?.BaseName == baseName;
    }
}
