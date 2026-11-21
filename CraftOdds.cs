using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileImGui;
using Hexa.NET.ImGui;
using ItemFilterLibrary;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ExileCrafting;

#region Baked mod data - data/craft-mods.json

// one craftable mod, trimmed by tools/bake-mods.py out of the RePoE-fork export. the live ModsDat
// can't do this job: ModRecord carries no generation weights at all.
public class CraftMod
{
    [JsonProperty("t")] public string Side { get; set; }
    [JsonProperty("lvl")] public int Level { get; set; }
    [JsonProperty("g")] public string[] Groups { get; set; }

    // parallel and ordered. resolution is first-matching-tag-wins, so the order is load bearing
    [JsonProperty("swt")] public string[] SpawnTags { get; set; }
    [JsonProperty("sww")] public int[] SpawnWeights { get; set; }
    [JsonProperty("gwt")] public string[] GenTags { get; set; }
    [JsonProperty("gww")] public int[] GenWeights { get; set; }

    [JsonProperty("it")] public string[] ImplicitTags { get; set; }
    [JsonProperty("adds")] public string[] AddsTags { get; set; }
    [JsonProperty("ess")] public int EssenceOnly { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("text")] public string Text { get; set; }
    [JsonProperty("type")] public string Type { get; set; }

    public bool IsPrefix => Side == "p";
}

public class CraftBase
{
    [JsonProperty("t")] public string[] Tags { get; set; }
    [JsonProperty("c")] public string ItemClass { get; set; }
}

// base tags keyed by metadata path, plus the influence tag per item class. baked because the live
// tags are unusable: both ItemData.Tags and BaseItemType.Tags come back with just the armour-class
// tag, which loses every mod keyed on armour, boots or default. influence tags aren't in the base
// tags at all, at any level
public class CraftBases
{
    [JsonProperty("bases")] public Dictionary<string, CraftBase> Bases { get; set; }
    [JsonProperty("influence")] public Dictionary<string, Dictionary<string, string>> Influence { get; set; }
}

public static class ModData
{
    static Dictionary<string, CraftMod> _mods;
    static CraftBases _bases;

    public static string LoadError { get; private set; }

    public static CraftBases Bases
    {
        get
        {
            if (_bases == null) _bases = Read<CraftBases>("craft-bases.json");
            return _bases;
        }
    }

    public static IReadOnlyDictionary<string, CraftMod> Mods
    {
        get
        {
            if (_mods == null) _mods = Read<Dictionary<string, CraftMod>>("craft-mods.json");
            return _mods;
        }
    }

    static T Read<T>(string fileName) where T : class
    {
        if (LoadError != null) return null;

        try
        {
            var path = Path.Combine(ExileCrafting.Main.DirectoryFullName, "data", fileName);
            if (!File.Exists(path))
            {
                LoadError = $"data/{fileName} is missing. run tools/bake-mods.py";
                return null;
            }

            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            return null;
        }
    }

    // first entry whose tag the item carries wins, and everything after it is dead. a zero is a
    // hard block, not a placeholder - this is what stops a bow rolling tier 8 attack speed
    public static int SpawnWeight(CraftMod mod, HashSet<string> tags)
    {
        for (var i = 0; i < mod.SpawnTags.Length; i++)
            if (tags.Contains(mod.SpawnTags[i]))
                return mod.SpawnWeights[i];

        return 0;
    }

    // percent multiplier on top of the spawn weight, only 365 mods have one
    public static int GenerationWeight(CraftMod mod, HashSet<string> tags)
    {
        if (mod.GenTags == null) return 100;

        for (var i = 0; i < mod.GenTags.Length; i++)
            if (tags.Contains(mod.GenTags[i]))
                return mod.GenWeights[i];

        return 100;
    }

    public static int Weight(CraftMod mod, HashSet<string> tags) =>
        SpawnWeight(mod, tags) * GenerationWeight(mod, tags) / 100;
}

#endregion

#region Currency odds - what an orb would roll onto the hovered item

// hold an orb, hover an item, get the pool it would roll from beside the game's own tooltip.
// aug and exalt both add exactly one affix, so neither needs the contested affix-count constants
// and the odds are just weight over total. the draw is one roll across every side that still has
// room, so a rare with both sides open competes its prefixes and suffixes in a single pool.
public static class CurrencyOdds
{
    // the group heading is the tier text with every number blanked, the way Craft of Exile shows it
    static readonly Regex Numbers = new(@"\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\)|\d+(?:\.\d+)?", RegexOptions.Compiled);

    // every read below drops out on the odd frame - Cursor.ItemType comes back empty, UIHoverElement
    // loses the entity, the tooltip rect goes to zero - and the panel blinks. hold the last good
    // read briefly so a one frame gap doesn't reach the screen
    const int StickyMs = 250;

    static Orb? _held;
    static long _heldAt;
    static Entity _hover;
    static long _hoverAt;
    static SharpDX.RectangleF _tooltip;
    static long _tooltipAt;

    static bool Fresh(long stamp) => Environment.TickCount64 - stamp < StickyMs;

    static readonly Vector4 Dim = new(0.65f, 0.65f, 0.68f, 1f);
    static readonly Vector4 Warn = new(0.95f, 0.72f, 0.30f, 1f);

    // Rarity null means either magic or rare. Cap overrides the item's own affix cap, which only
    // regal needs - it upgrades to rare before it rolls, so a 1-prefix magic still has room
    readonly record struct Orb(string Base, ItemRarity? Rarity, bool Adds, int? Cap);

    static readonly Orb[] Supported =
    {
        new("Orb of Augmentation", ItemRarity.Magic, true, null),
        new("Regal Orb", ItemRarity.Magic, true, 3),
        new("Exalted Orb", ItemRarity.Rare, true, null),
        new("Orb of Annulment", null, false, null),
    };

    // ImGui.Text and TextColored are printf-style, and mod text is full of % signs
    static void Label(string text) => ImGui.TextUnformatted(text);

    static void Label(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void Draw(ExileCrafting plugin)
    {
        if (!plugin.Settings.ShowCraftOdds.Value) return;

        var held = Held(plugin);
        if (held != null) (_held, _heldAt) = (held, Environment.TickCount64);
        else if (Fresh(_heldAt)) held = _held;
        else return;

        var entity = plugin.GameController.IngameState.UIHoverElement?.Entity;
        if (entity is { IsValid: true }) (_hover, _hoverAt) = (entity, Environment.TickCount64);
        else if (Fresh(_hoverAt)) entity = _hover;
        else return;

        if (entity is not { IsValid: true }) return;
        if (!entity.HasComponent<Mods>()) return;

        var item = new ItemData(entity, plugin.GameController);
        var wanted = held.Value.Rarity;
        if (wanted == null
                ? item.Rarity != ItemRarity.Magic && item.Rarity != ItemRarity.Rare
                : item.Rarity != wanted)
            return;

        Position(plugin);

        var open = true;
        if (ExileImGui.Windows.Begin("Craft odds", "craftodds", ref open,
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoInputs))
            Body(item, held.Value, plugin.Settings);

        ExileImGui.Windows.End();
    }

    static Orb? Held(ExileCrafting plugin)
    {
        var state = plugin.GameController.IngameState;
        var onCursor = state.IngameUi?.Cursor?.ItemType?.BaseName;

        if (string.IsNullOrEmpty(onCursor))
        {
            // Cursor.ItemType comes back empty in some states, so fall back to the cursor inventory
            var slot = state.ServerData?.GetPlayerInventoryByType(InventoryTypeE.Cursor)?
                .InventorySlotItems?.FirstOrDefault(s => s?.Item != null);
            onCursor = slot == null
                ? null
                : plugin.GameController.Files.BaseItemTypes.Translate(slot.Item.Path)?.BaseName;
        }

        foreach (var entry in Supported)
            if (entry.Base == onCursor)
                return entry;

        return null;
    }

    // sit beside the game's own tooltip, flipping to its left when there's no room on the right
    static void Position(ExileCrafting plugin)
    {
        var gap = plugin.Settings.OddsGap.Value;
        var tooltip = plugin.GameController.IngameState.UIHoverTooltip;
        var rect = tooltip is { IsVisible: true } ? tooltip.GetClientRectCache : default;

        if (rect.Width > 0) (_tooltip, _tooltipAt) = (rect, Environment.TickCount64);
        else if (Fresh(_tooltipAt)) rect = _tooltip;

        if (rect.Width <= 0)
        {
            ImGui.SetNextWindowPos(ImGui.GetMousePos() + new Vector2(24f + gap, 0f), ImGuiCond.Always);
            return;
        }

        var screen = plugin.GameController.Window.GetWindowRectangle();
        var right = rect.X + rect.Width + gap;
        var flip = right + 380f > screen.Width;

        ImGui.SetNextWindowPos(new Vector2(flip ? rect.X - gap : right, rect.Y), ImGuiCond.Always,
            new Vector2(flip ? 1f : 0f, 0f));
    }

    static void Body(ItemData item, Orb orb, ExileCraftingSettings settings)
    {
        if (ModData.Mods == null)
        {
            Label(Warn, ModData.LoadError ?? "mod data not loaded");
            return;
        }

        if (item.IsCorrupted)
        {
            Label(Dim, "corrupted, no orb applies");
            return;
        }

        // a missed base lookup falls back to the live tags, which silently costs most of the pool
        if (ModData.Bases != null && Base(item) == null)
            Label(Warn, $"base missing from craft-bases.json: {item.Path}");

        if (!orb.Adds)
        {
            Annul(item, orb.Base, settings);
            return;
        }

        var prefixOpen = orb.Cap is { } cap
            ? (item.ModsInfo.Prefixes?.Count ?? 0) < cap
            : item.ModsInfo.HasOpenPrefix;
        var suffixOpen = orb.Cap is { } cap2
            ? (item.ModsInfo.Suffixes?.Count ?? 0) < cap2
            : item.ModsInfo.HasOpenSuffix;

        if (!prefixOpen && !suffixOpen)
        {
            Label(Dim, "no open affix");
            return;
        }

        var currency = orb.Base;
        var both = prefixOpen && suffixOpen;
        var pool = BuildPool(item, prefixOpen, suffixOpen, out var total);

        var sides = both ? "Prefix + Suffix" : prefixOpen ? "Prefix" : "Suffix";
        Label($"{currency}  -  ilvl {item.ItemLevel}");
        Label(Dim, $"{sides}  -  {pool.Count} mods, {total} total weight");

        if (both && total > 0)
        {
            var prefixWeight = pool.Where(p => p.Mod.IsPrefix).Sum(p => p.Weight);
            Label(Dim, $"prefix {prefixWeight * 100f / total:0.0}%   " +
                       $"suffix {(total - prefixWeight) * 100f / total:0.0}%");
        }

        ImGui.Separator();

        if (pool.Count == 0)
        {
            Label(Warn, "nothing can roll here");
            return;
        }

        var groups = Grouped(pool);
        var lines = 0;
        var drawn = 0;

        if (ImGui.BeginTable("odds", both ? 3 : 2,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            var showTiers = settings.OddsShowTiers.Value;

            foreach (var group in groups)
            {
                // never cut a group in half, but always draw at least one
                var cost = showTiers ? 1 + group.Tiers.Count : 1;
                if (drawn > 0 && lines + cost > settings.OddsLineBudget.Value) break;

                Row(both, group.IsPrefix, $"{group.Weight * 100f / total,6:0.00}%", group.Label, null);
                lines++;

                if (showTiers)
                {
                    ImGui.PushFont(ImGui.GetFont(), ImGui.GetStyle().FontSizeBase * settings.OddsTierScale.Value);
                    foreach (var (mod, weight) in group.Tiers)
                    {
                        Row(both, group.IsPrefix, $"{weight * 100f / total,6:0.00}%", "    " + mod.Text, Dim);
                        lines++;
                    }

                    ImGui.PopFont();
                }

                drawn++;
            }

            ImGui.EndTable();
        }

        if (drawn >= groups.Count) return;

        var shown = groups.Take(drawn).Sum(g => g.Weight);
        Label(Dim, $"+{groups.Count - drawn} more groups, {(total - shown) * 100f / total:0.00}% between them");
    }

    // annul picks uniformly among the affixes already on the item. fractured mods are immune, and
    // an implicit or an enchant was never an affix to begin with
    static void Annul(ItemData item, string currency, ExileCraftingSettings settings)
    {
        var fractured = new HashSet<string>(
            (item.ModsInfo.FracturedMods ?? Array.Empty<ItemMod>()).Select(m => m.RawName));

        var candidates = (item.ModsInfo.ExplicitMods ?? Array.Empty<ItemMod>())
            .Where(m => !fractured.Contains(m.RawName))
            .ToList();

        Label($"{currency}  -  ilvl {item.ItemLevel}");

        if (candidates.Count == 0)
        {
            Label(Dim, fractured.Count > 0
                ? "every affix is fractured, nothing can be annulled"
                : "no affix to annul");
            return;
        }

        Label(Dim, $"{candidates.Count} affixes, one removed at random");
        if (fractured.Count > 0) Label(Dim, $"{fractured.Count} fractured and immune");
        ImGui.Separator();

        var chance = 100f / candidates.Count;

        if (!ImGui.BeginTable("annul", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
            return;

        foreach (var mod in candidates.Take(settings.OddsLineBudget.Value))
        {
            var baked = mod.ModRecord?.Key is { } key && ModData.Mods.TryGetValue(key, out var m) ? m : null;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Label($"{chance,6:0.00}%");
            ImGui.TableNextColumn();
            Label(Dim, baked == null ? "?" : baked.IsPrefix ? "P" : "S");
            ImGui.TableNextColumn();
            Label(baked?.Text ?? mod.Translation ?? mod.RawName);
        }

        ImGui.EndTable();
    }

    static void Row(bool both, bool isPrefix, string chance, string text, Vector4? color)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (color == null) Label(chance); else Label(color.Value, chance);

        if (both)
        {
            ImGui.TableNextColumn();
            Label(Dim, isPrefix ? "P" : "S");
        }

        ImGui.TableNextColumn();
        if (color == null) Label(text); else Label(color.Value, text);
    }

    // one entry per mod group, heaviest first, tiers inside it best first
    static List<(string Label, int Weight, bool IsPrefix, List<(CraftMod Mod, int Weight)> Tiers)> Grouped(
        List<(CraftMod Mod, int Weight)> pool) =>
        pool.GroupBy(p => string.Join("+", p.Mod.Groups))
            .Select(g =>
            {
                var tiers = g.OrderByDescending(t => t.Mod.Level).ToList();
                return (Numbers.Replace(tiers[0].Mod.Text ?? "", "#"), g.Sum(t => t.Weight),
                    tiers[0].Mod.IsPrefix, tiers);
            })
            .OrderByDescending(g => g.Item2)
            .ToList();

    static List<(CraftMod Mod, int Weight)> BuildPool(ItemData item, bool prefixOpen, bool suffixOpen,
        out int total)
    {
        var tags = ItemTags(item);
        var taken = TakenGroups(item);
        var pool = new List<(CraftMod, int)>();

        total = 0;
        foreach (var mod in ModData.Mods.Values)
        {
            if (!(mod.IsPrefix ? prefixOpen : suffixOpen)) continue;
            if (mod.EssenceOnly != 0) continue;
            if (mod.Level > item.ItemLevel) continue;
            if (mod.Groups.Any(taken.Contains)) continue;

            var weight = ModData.Weight(mod, tags);
            if (weight <= 0) continue;

            pool.Add((mod, weight));
            total += weight;
        }

        pool.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return pool;
    }

    // base tags plus whatever the mods already on the item grant. only grants_2h_support and
    // grants_crit_chance_support gate anything, the other two adds_tags are inert for weights
    static HashSet<string> ItemTags(ItemData item)
    {
        var tags = new HashSet<string>(item.Tags ?? new List<string>()) { "default" };
        var known = Base(item);

        // the baked tags are the real set. the live ones only carry the armour class, which drops
        // every mod keyed on armour, boots or default - most of a boot's pool
        if (known != null)
        {
            foreach (var tag in known.Tags) tags.Add(tag);

            foreach (var tag in InfluenceTags(item, known.ItemClass)) tags.Add(tag);
        }

        foreach (var existing in Existing(item))
        {
            var key = existing.ModRecord?.Key;
            if (key == null || !ModData.Mods.TryGetValue(key, out var baked)) continue;
            if (baked.AddsTags == null) continue;

            foreach (var tag in baked.AddsTags) tags.Add(tag);
        }

        return tags;
    }

    static CraftBase Base(ItemData item)
    {
        var bases = ModData.Bases?.Bases;
        return bases != null && bases.TryGetValue(item.Path ?? "", out var known) ? known : null;
    }

    // the internal names aren't the ones the game shows: basilisk is Hunter, eyrie is Redeemer,
    // adjudicator is Warlord
    static IEnumerable<string> InfluenceTags(ItemData item, string itemClass)
    {
        if (!item.IsInfluenced) yield break;
        if (ModData.Bases?.Influence == null) yield break;
        if (!ModData.Bases.Influence.TryGetValue(itemClass ?? "", out var byInfluence)) yield break;

        foreach (var (flag, influence) in new[]
                 {
                     (item.IsShaper, "shaper"), (item.IsElder, "elder"),
                     (item.IsCrusader, "crusader"), (item.IsHunter, "basilisk"),
                     (item.IsRedeemer, "eyrie"), (item.IsWarlord, "adjudicator"),
                 })
            if (flag && byInfluence.TryGetValue(influence, out var tag))
                yield return tag;
    }

    // exclusion is set intersection over groups, never the mod type
    static HashSet<string> TakenGroups(ItemData item)
    {
        var groups = new HashSet<string>();

        foreach (var existing in Existing(item))
            foreach (var group in existing.ModRecord?.Groups ?? new List<string>())
                groups.Add(group);

        return groups;
    }

    // fractured mods hold a slot but aren't reliably in ExplicitMods, so union both
    static IEnumerable<ItemMod> Existing(ItemData item) =>
        (item.ModsInfo.ExplicitMods ?? Array.Empty<ItemMod>())
        .Concat(item.ModsInfo.FracturedMods ?? Array.Empty<ItemMod>())
        .GroupBy(m => m.RawName)
        .Select(g => g.First());
}

#endregion
