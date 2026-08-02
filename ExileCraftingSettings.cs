using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace ExileCrafting;

public class ExileCraftingSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
}
