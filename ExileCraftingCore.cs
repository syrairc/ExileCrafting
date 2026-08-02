using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace ExileCrafting;

public class ExileCrafting : BaseSettingsPlugin<ExileCraftingSettings>
{
    public override bool Initialise()
    {
        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
    }

    public override Job Tick()
    {
        return null;
    }

    public override void Render()
    {
    }

    public override void EntityAdded(Entity entity)
    {
    }
}
