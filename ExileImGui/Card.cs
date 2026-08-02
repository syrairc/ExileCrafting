using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace ExileImGui;

/// <summary>
/// What a <see cref="Card.Draw"/> did this frame.
/// </summary>
public readonly struct CardResult
{
    /// <summary>The body reported an edit. Fold this into your dirty flag.</summary>
    public readonly bool Changed;

    /// <summary>The close X fired this frame. The caller decides what to drop.</summary>
    public readonly bool Closed;

    /// <summary>Pairs the two flags a card reports.</summary>
    public CardResult(bool changed, bool closed) { Changed = changed; Closed = closed; }
}

/// <summary>
/// Bordered, rounded settings "card": a title header over a caller-drawn body, optionally
/// collapsible and closeable. The boxed cousin of <see cref="ListEditor.Cards"/>, which uses a bare
/// header instead.
/// </summary>
public static class Card
{
    /// <summary>
    /// One card. The header floats above the box; see <see cref="BeginCard"/> for the variant where
    /// the header row is itself the disclosure.
    /// </summary>
    /// <param name="body">Runs when expanded and returns its own changed flag.</param>
    /// <param name="collapsible">Turns the title into a collapsing header.</param>
    /// <param name="closeable">Adds the close X that sets <see cref="CardResult.Closed"/>.</param>
    public static CardResult Draw(string id, string title, Func<bool> body,
        bool collapsible = false, bool closeable = false, bool defaultOpen = true)
    {
        bool changed = false, closed = false;
        ImGui.PushID(id);

        // inset + rounded, border only. no fill: FrameBg here is the same color the inputs inside use, so
        // the card box vanished against its own contents. the border comes from the theme.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        // AutoResizeY -> height hugs the content; x=0 still fills the available width. AlwaysAutoResize keeps
        // measuring while the card is scrolled out of view, otherwise imgui culls the body and it folds flat.
        ImGui.BeginChild("##c", Vector2.Zero,
            ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);

        if (DrawHeader(title, collapsible, closeable, defaultOpen, ref closed))
            changed = body();

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopID();
        return new CardResult(changed, closed);
    }

    /// <summary>
    /// One boxed card per item, where the header X deletes that item. No drag-reorder on purpose -
    /// reach for <see cref="ListEditor.Cards"/> when you need reordering.
    /// </summary>
    /// <param name="title">Header text for an item.</param>
    /// <param name="body">Draws one item's fields, given the item and its index.</param>
    /// <param name="add">Optional add row. Null draws none.</param>
    /// <param name="confirmDelete">Routes deletes through a confirm modal.</param>
    /// <returns>True when anything was added, edited or removed.</returns>
    public static bool List<T>(string id, List<T> list, Func<T, string> title,
        Func<T, int, bool> body, AddSource<T> add = null,
        bool collapsible = false, bool confirmDelete = false)
    {
        bool d = false;
        ImGui.PushID(id);
        d |= ListEditor.DrawAdd(id, list, add);

        int toRemove = -1;
        for (int i = 0; i < list.Count; i++)
        {
            // body runs synchronously inside Draw, so capturing the loop i here is safe.
            var r = Draw(i.ToString(), title(list[i]), () => body(list[i], i),
                collapsible: collapsible, closeable: true);
            d |= r.Changed;
            if (r.Closed) toRemove = i;
        }

        d |= ListEditor.ApplyDelete(id, list, toRemove, confirmDelete);
        ImGui.PopID();
        return d;
    }

    // open state per BeginCard, so EndCard knows whether it has body padding to undo. a stack because
    // cards nest in principle; in practice it's one deep.
    static readonly Stack<bool> _open = new();

    /// <summary>
    /// A card whose FIRST ROW is the disclosure: arrow, title, and a right-aligned trailing slot, with
    /// the body inside the same bordered box, so header and body read as one object. Collapsed leaves
    /// just the header row.
    /// <para>
    /// Begin/End contract, same as <see cref="Windows.Begin"/>: ALWAYS call <see cref="EndCard"/>,
    /// including when this returns false.
    /// </para>
    /// </summary>
    /// <param name="trailing">Widgets pinned to the right of the header - chips, a size readout,
    /// latch toggles. Stays clickable; the header does not swallow the click.</param>
    /// <param name="menu">Right-click popup body for the header.</param>
    /// <param name="setOpen">Drives the open state from your own set, which is what makes expand-all
    /// and collapse-all one line each. The return value is the state AFTER a click, so write it
    /// straight back. Null lets ImGui remember it instead.</param>
    /// <returns>True when the body should draw.</returns>
    public static bool BeginCard(string id, string title, Action trailing = null, Action menu = null,
        bool? setOpen = null, bool defaultOpen = false)
    {
        ImGui.PushID(id);
        // zero window padding so the framed header fills the card edge to edge; the body gets its own indent.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        // AlwaysAutoResize: same story as Card.Draw, a culled child measures as empty and the box folds flat.
        ImGui.BeginChild("##card", Vector2.Zero,
            ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);

        // Framed makes the header a filled bar; SpanFullWidth widens the hit area to the whole card;
        // AllowOverlap keeps the trailing widgets clickable instead of the header swallowing the click;
        // NoTreePushOnOpen means there's no TreePop to pair.
        var flags = ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.SpanFullWidth
                  | ImGuiTreeNodeFlags.AllowOverlap | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (defaultOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (setOpen.HasValue) ImGui.SetNextItemOpen(setOpen.Value);

        // ###h keys the node on "h" so renaming the card doesn't reset its open state mid-type.
        bool open = ImGui.TreeNodeEx(Text.Ascii(title) + "###h", flags);
        if (menu != null && ImGui.BeginPopupContextItem("##ctx")) { menu(); ImGui.EndPopup(); }
        if (trailing != null)
        {
            Controls.BeginTrailing(id + "_trail", 6f);
            trailing();
            Controls.EndTrailing();
        }

        _open.Push(open);
        if (open) { ImGui.Indent(8f); ImGui.Dummy(new Vector2(0, 2f)); }
        return open;
    }

    /// <summary>
    /// Closes a <see cref="BeginCard"/>. Must be called whatever BeginCard returned, or the card's
    /// open-state stack desyncs for the rest of the session.
    /// </summary>
    public static void EndCard()
    {
        if (_open.Count > 0 && _open.Pop()) { ImGui.Dummy(new Vector2(0, 4f)); ImGui.Unindent(8f); }
        ImGui.EndChild();
        ImGui.PopStyleVar(1);
        ImGui.PopID();
    }

    // draws the header and returns whether the body should draw. sets closed if the x fired this frame.
    static bool DrawHeader(string title, bool collapsible, bool closeable, bool defaultOpen, ref bool closed)
    {
        var t = Text.Ascii(title);
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        if (collapsible)
        {
            // ###h keys the header on "h" so editing the title doesn't reset the collapse state mid-frame.
            if (closeable)
            {
                bool keep = true;
                bool open = ImGui.CollapsingHeader(t + "###h", ref keep, flags);
                if (!keep) closed = true;
                return open;
            }
            return ImGui.CollapsingHeader(t + "###h", flags);
        }

        // static title: right-align the x, then a rule under it to split header from body.
        ImGui.TextUnformatted(t);
        if (closeable)
        {
            ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
            if (ImGui.SmallButton("x")) closed = true;
        }
        ImGui.Separator();
        return true;
    }
}
