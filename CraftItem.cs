using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;

namespace ExileCrafting;

public readonly struct CraftItem
{
    readonly Entity _entity;

    public CraftItem(Entity entity) => _entity = entity;

    public static readonly CraftItem Empty = new(null);

    public bool IsEmpty => _entity is not { IsValid: true };
    public long Address => _entity?.Address ?? 0;

    public string BaseName => _entity == null
        ? ""
        : ExileCrafting.Main.GameController.Files.BaseItemTypes.Translate(_entity.Path)?.BaseName ?? "";

    public Mods Mods => _entity != null && _entity.TryGetComponent<Mods>(out var mods) ? mods : null;
    public ItemRarity Rarity => Mods?.ItemRarity ?? ItemRarity.Normal;
}
