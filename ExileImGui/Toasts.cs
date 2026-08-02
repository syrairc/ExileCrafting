using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using SColor = SharpDX.Color;

namespace ExileImGui;

/// <summary>Severity of a toast, which picks its accent-bar colour.</summary>
public enum ToastLevel
{
    /// <summary>Neutral blue. The default.</summary>
    Info,
    /// <summary>Green.</summary>
    Success,
    /// <summary>Amber.</summary>
    Warning,
    /// <summary>Red.</summary>
    Error,
}

/// <summary>
/// Which way the stack grows from its anchor. Expose it to your own users, or just pin one.
/// </summary>
public enum ToastGrow
{
    /// <summary>Always grow upward from the anchor.</summary>
    Up,
    /// <summary>Always grow downward from the anchor.</summary>
    Down,
    /// <summary>Read the anchor's screen position: lower half grows up, upper half grows down.</summary>
    Default,
}

/// <summary>
/// Look and placement for a toast stack. Plain mutable defaults, so callers can bind their own
/// settings fields straight to it.
/// </summary>
public sealed class ToastStyle
{
    /// <summary>Text pixel size. Scaled off the current font size when drawing.</summary>
    public float TextSize = 16f;
    /// <summary>Padding inside each box.</summary>
    public float Padding = 6f;
    /// <summary>Wrap width for the whole box. 0 or less means no wrap.</summary>
    public float MaxWidth = 320f;
    /// <summary>Default lifetime in seconds. Override per toast with Show(..., seconds:).</summary>
    public double Duration = 4.0;
    /// <summary>Box fill. A zero alpha draws no background.</summary>
    public SColor Background = new(20, 22, 28, 230);
    /// <summary>Box outline colour.</summary>
    public SColor Border = new(60, 64, 74, 255);
    /// <summary>Box outline thickness. 0 draws no outline.</summary>
    public float BorderThickness = 1f;
    /// <summary>Text colour.</summary>
    public SColor TextColor = new(230, 232, 238, 255);
    /// <summary>Most toasts kept at once. The oldest are dropped past this.</summary>
    public int Max = 5;
    /// <summary>Width of the coloured bar down the left edge.</summary>
    public float AccentWidth = 4f;
    /// <summary>How long the tail fade lasts, in seconds.</summary>
    public double FadeSeconds = 0.5;
    /// <summary>Stack direction. See <see cref="ToastGrow"/>.</summary>
    public ToastGrow Grow = ToastGrow.Default;
}

/// <summary>
/// Transient messages stacked at a fixed anchor, each fading near the end of its life. One host per
/// plugin: call <see cref="Show"/> from anywhere, <see cref="Draw"/> every frame in Render.
/// </summary>
public sealed class ToastHost
{
    sealed class Toast { public string Text; public ToastLevel Level; public DateTime ShownAt; public double Duration; }

    readonly List<Toast> _toasts = new();

    /// <summary>How many toasts are currently queued.</summary>
    public int Count => _toasts.Count;

    /// <summary>Drops every queued toast immediately, with no fade.</summary>
    public void Clear() => _toasts.Clear();

    /// <summary>
    /// Queues a notification. Blank text is ignored; queuing past <see cref="ToastStyle.Max"/> drops
    /// the oldest.
    /// </summary>
    /// <param name="seconds">Lifetime override. Null uses <see cref="ToastStyle.Duration"/>.</param>
    public void Show(string text, ToastStyle style, ToastLevel level = ToastLevel.Info, double? seconds = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _toasts.Add(new Toast { Text = text, Level = level, ShownAt = DateTime.Now, Duration = seconds ?? style.Duration });
        if (_toasts.Count > style.Max) _toasts.RemoveRange(0, _toasts.Count - style.Max);
    }

    /// <summary>Accent-bar colour for a level.</summary>
    public static SColor Accent(ToastLevel l) => l switch
    {
        ToastLevel.Success => new SColor(120, 210, 120, 255),
        ToastLevel.Warning => new SColor(230, 180, 90, 255),
        ToastLevel.Error => new SColor(230, 110, 100, 255),
        _ => new SColor(150, 190, 240, 255),
    };

    /// <summary>
    /// Fade multiplier for a toast: 1 until the last <paramref name="fade"/> seconds, then ramping to
    /// 0 at expiry. Clamped, and pure so it can be asserted without a live frame.
    /// </summary>
    public static float FadeAlpha(double remaining, double fade) =>
        remaining >= fade ? 1f : (float)Math.Clamp(remaining / fade, 0, 1);

    /// <summary>
    /// Whether the stack grows up from the anchor. Pulled out so the Default rule is testable
    /// without a live ImGui frame.
    /// </summary>
    public static bool GrowsUp(ToastGrow grow, float topY, float screenH) =>
        grow == ToastGrow.Up || (grow == ToastGrow.Default && topY > screenH * 0.5f);

    /// <summary>
    /// Draws the stack and expires whatever ran out. Call every frame.
    /// <para>
    /// Newest always sits nearest the bottom of the block. There is no screen-bounds clamp - a stack
    /// tall enough to run off screen is the caller's problem.
    /// </para>
    /// </summary>
    /// <param name="anchorX">Horizontal centre of the stack.</param>
    /// <param name="topY">The fixed anchor edge. <see cref="ToastStyle.Grow"/> decides which way it stacks from here.</param>
    public void Draw(float anchorX, float topY, ToastStyle st)
    {
        var now = DateTime.Now;
        _toasts.RemoveAll(x => (now - x.ShownAt).TotalSeconds >= x.Duration);
        if (_toasts.Count == 0) return;

        var dl = ImGui.GetForegroundDrawList();
        var font = ImGui.GetFont();
        var baseSize = ImGui.GetFontSize();
        if (baseSize <= 0) baseSize = 16f;
        float size = st.TextSize;
        float scale = size / baseSize;
        float pad = st.Padding;
        float lineH = (float)Math.Ceiling(size) + 4f;
        const float gap = 6f;

        (Vector2 size, List<string> rows) Measure(string text)
        {
            var ascii = Text.Ascii(text);
            float maxTextW = st.MaxWidth > 0 ? Math.Max(10f, st.MaxWidth - pad * 2 - st.AccentWidth) : float.MaxValue;
            var rows = st.MaxWidth > 0 && ImGui.CalcTextSize(ascii).X * scale > maxTextW
                ? Text.Wrap(ascii, maxTextW, scale)
                : new List<string> { ascii };
            float contentW = 0f;
            foreach (var r in rows) contentW = Math.Max(contentW, ImGui.CalcTextSize(r).X * scale);
            return (new Vector2(contentW + pad * 2 + st.AccentWidth, rows.Count * lineH + pad * 2), rows);
        }

        // measure up front so an upward stack can place each box by its own height. <= Max toasts, so cheap.
        var boxes = new (Vector2 size, List<string> rows)[_toasts.Count];
        for (int i = 0; i < _toasts.Count; i++) boxes[i] = Measure(_toasts[i].Text);

        bool up = GrowsUp(st.Grow, topY, ImGui.GetIO().DisplaySize.Y);

        float cursor = topY;
        for (int idx = 0; idx < _toasts.Count; idx++)
        {
            // reading order stays oldest->newest top-to-bottom either way; up mode just walks the list backward
            // from the anchor so the block sits above topY.
            int i = up ? _toasts.Count - 1 - idx : idx;
            var t = _toasts[i];
            var (boxSize, rows) = boxes[i];
            float top = up ? cursor - boxSize.Y : cursor;
            cursor = up ? top - gap : top + boxSize.Y + gap;

            var remaining = t.Duration - (now - t.ShownAt).TotalSeconds;
            float alpha = FadeAlpha(remaining, st.FadeSeconds);

            var min = new Vector2(anchorX - boxSize.X / 2f, top);
            var max = min + boxSize;

            var bg = EColor.Fade(st.Background, alpha * st.Background.A / 255f);
            if (bg.A > 0) dl.AddRectFilled(min, max, EColor.U32(bg));
            dl.AddRectFilled(min, new Vector2(min.X + st.AccentWidth, max.Y),
                EColor.U32(EColor.Fade(Accent(t.Level), alpha)));
            if (st.BorderThickness > 0)
                dl.AddRect(min, max, EColor.U32(EColor.Fade(st.Border, alpha * st.Border.A / 255f)),
                    0f, ImDrawFlags.None, st.BorderThickness);

            uint textCol = EColor.U32(EColor.Fade(st.TextColor, alpha * st.TextColor.A / 255f));
            var p = new Vector2(min.X + st.AccentWidth + pad, min.Y + pad);
            foreach (var r in rows) { dl.AddText(font, size, p, textCol, r); p.Y += lineH; }
        }
    }
}
