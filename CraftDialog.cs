using System.Numerics;
using ExileImGui;
using ImGuiNET;

namespace ExileCrafting;

public sealed class CraftDialog
{
    public bool IsOpen;

    public void Draw(ExileCrafting plugin)
    {
        if (!IsOpen) return;

        var open = IsOpen;
        // windows sdk pulls in a global Windows namespace here, has to be fully qualified or it wont resolve
        if (!ExileImGui.Windows.Begin("Craft Scripts", "craftdialog", ref open, 0, new Vector2(360, 400)))
        {
            ExileImGui.Windows.End();
            IsOpen = open;
            return;
        }

        if (plugin.RunningScriptName == null)
        {
            foreach (var name in plugin.ScriptHost.DiscoverScripts())
            {
                if (ImGui.Selectable(name))
                    plugin.StartScript(name);
            }
        }
        else
        {
            ImGui.TextWrapped($"Running: {plugin.RunningScriptName}");
            ImGui.Separator();

            if (plugin.RunningError != null)
            {
                using (new EColor.StyleColorScope((ImGuiCol.Text, 0xFF0000FFu)))
                    ImGui.TextWrapped(plugin.RunningError);
            }
            else
            {
                ImGui.TextWrapped(plugin.RunningStatusText ?? "");
            }

            if (plugin.IsScriptRunning)
            {
                if (ImGui.Button("Cancel")) plugin.CancelRunningScript();
            }
            else if (ImGui.Button("Back"))
            {
                plugin.ResetRunState();
            }
        }

        ExileImGui.Windows.End();
        IsOpen = open;
    }
}
