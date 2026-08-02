using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.Windows.Forms;

namespace ExileCrafting;

public class ExileCraftingSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
    public HotkeyNodeV2 OpenPickerHotkey { get; set; } = new(Keys.NumPad7);
    public HotkeyNodeV2 AbortHotkey { get; set; } = new(Keys.NumPad8);
    public ToggleNode UseHumanizedMouse { get; set; } = new ToggleNode(false);
    public RangeNode<int> MaxCurrencyApplications { get; set; } = new(200, 1, 2000);
    public RangeNode<int> MaxRunSeconds { get; set; } = new(120, 5, 900);
}
