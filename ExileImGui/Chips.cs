using System;
using System.Collections.Generic;
using ImGuiNET;

namespace ExileImGui;

/// <summary>
/// Free-string pill list: existing entries as small removable chips, plus one input row to add more.
/// <para>
/// For an unordered set of short strings - keywords, hotstrings, tag lists - that the user builds up.
/// Reach for <see cref="ListEditor"/> instead when a row needs more than its own text.
/// </para>
/// </summary>
public static class Chips
{
    static readonly Dictionary<string, string> _pending = new(); // in-progress add text, per id

    /// <summary>
    /// Draws the chips, wrapping them across the line, then an input row that appends on Enter or "+".
    /// Mutates <paramref name="items"/> in place.
    /// <para>
    /// A blank, duplicate or rejected entry is left sitting in the box rather than silently eaten, so
    /// the user can see what happened.
    /// </para>
    /// </summary>
    /// <param name="id">Scopes both the ImGui ids and the retained in-progress text, so two chip
    /// lists need two ids.</param>
    /// <param name="items">The caller's list. Added to and removed from directly.</param>
    /// <param name="hint">Placeholder text for the add box.</param>
    /// <param name="caseInsensitiveDedup">When true, "fire" will not add alongside "Fire".</param>
    /// <param name="validate">Optional extra rule an entry must pass. Null accepts anything non-blank.</param>
    /// <returns>True the frame something is added or removed.</returns>
    public static bool Draw(string id, List<string> items, string hint = "add...",
        bool caseInsensitiveDedup = true, Func<string, bool> validate = null)
    {
        bool changed = false;
        ImGui.PushID(id);
        var style = ImGui.GetStyle();
        float wrapX = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;

        int toRemove = -1;
        for (int i = 0; i < items.Count; i++)
        {
            ImGui.PushID(i);
            // wrap decided off the width THIS chip will take, checked against where the last one ended -
            // imgui doesn't auto-wrap, and a chip's width isn't known until after it's drawn, so the call
            // has to be made looking backward at the previous item's rect, same as the imgui wrap idiom.
            if (i > 0)
            {
                float lastX2 = ImGui.GetItemRectMax().X;
                float nextX2 = lastX2 + style.ItemSpacing.X + ChipWidth(items[i]);
                if (nextX2 < wrapX) ImGui.SameLine();
            }
            if (DrawChip(items[i])) toRemove = i;
            ImGui.PopID();
        }
        if (toRemove >= 0) { items.RemoveAt(toRemove); changed = true; }

        var pending = _pending.TryGetValue(id, out var p) ? p : "";
        if (items.Count > 0) ImGui.Spacing();
        ImGui.SetNextItemWidth(160f);
        bool submit = ImGui.InputTextWithHint("##add", hint, ref pending, 128, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        submit |= ImGui.SmallButton("+");
        _pending[id] = pending;

        if (submit && ShouldAdd(pending, items, caseInsensitiveDedup, validate))
        {
            items.Add(pending);
            _pending[id] = "";
            changed = true;
        }

        ImGui.PopID();
        return changed;
    }

    /// <summary>
    /// The pure add-decision, pulled out of <see cref="Draw"/> so it can be asserted without an ImGui
    /// frame: not blank, not already in the list, and passing the caller's validator if there is one.
    /// </summary>
    internal static bool ShouldAdd(string text, List<string> items, bool caseInsensitiveDedup, Func<string, bool> validate)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool dup = caseInsensitiveDedup
            ? items.Exists(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase))
            : items.Contains(text);
        return !dup && (validate == null || validate(text));
    }

    // label pill + glued-on "x", grouped so wrap treats the pair as one item. returns true on x-click.
    static bool DrawChip(string label)
    {
        bool remove = false;
        var text = Text.Ascii(label);
        ImGui.BeginGroup();
        using (new EColor.StyleColorScope((ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.FrameBg))))
            ImGui.Button(text + "##lbl");
        ImGui.SameLine(0, 2);
        if (ImGui.SmallButton("x##del")) remove = true;
        ImGui.EndGroup();
        return remove;
    }

    // predicted width of DrawChip's group - close enough for the wrap check, doesn't need to be exact,
    // one chip wrapping a frame early or late is cosmetic only.
    static float ChipWidth(string label)
    {
        var style = ImGui.GetStyle();
        float textW = ImGui.CalcTextSize(Text.Ascii(label) ?? "").X;
        float lblBtnW = textW + style.FramePadding.X * 2;
        float xBtnW = ImGui.CalcTextSize("x").X + style.FramePadding.X * 2;
        return lblBtnW + 2 + xBtnW;
    }
}
