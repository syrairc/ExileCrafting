using System.Linq;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;

namespace ExileCrafting;

public static class CraftingSlot
{
    public static CraftItem ReadItem()
    {
        var inventory = ExileCrafting.Main.GameController.IngameState.ServerData.GetPlayerInventoryByType(InventoryTypeE.Crafting);
        var entity = inventory?.Items?.FirstOrDefault();
        return entity == null ? CraftItem.Empty : new CraftItem(entity);
    }

    // the crafting slot renders as one of the visible items in the currency-tab stash panel -
    // match it by entity address rather than guessing its tile size/position.
    public static NormalInventoryItem FindClickableSlot()
    {
        var target = ReadItem();
        if (target.IsEmpty) return null;

        var visible = ExileCrafting.Main.GameController.IngameState.IngameUi.StashElement?.VisibleStash?.VisibleInventoryItems;
        return visible?.FirstOrDefault(i => i.Item != null && i.Item.Address == target.Address);
    }

    public static NormalInventoryItem FindCurrencyInStash(string baseName)
    {
        var visible = ExileCrafting.Main.GameController.IngameState.IngameUi.StashElement?.VisibleStash?.VisibleInventoryItems;
        if (visible == null) return null;

        foreach (var item in visible)
        {
            var itemBaseName = ExileCrafting.Main.GameController.Files.BaseItemTypes.Translate(item.Item?.Path)?.BaseName;
            if (itemBaseName == baseName) return item;
        }

        return null;
    }
}
