using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;

namespace DesMeter;

internal static class KeyPicker
{
    private static bool capturing;

    private static readonly VirtualKey[] Ignored =
    [
        VirtualKey.NO_KEY, VirtualKey.CONTROL, VirtualKey.MENU, VirtualKey.SHIFT,
        VirtualKey.LCONTROL, VirtualKey.RCONTROL, VirtualKey.LMENU, VirtualKey.RMENU,
        VirtualKey.LSHIFT, VirtualKey.RSHIFT, VirtualKey.LBUTTON, VirtualKey.RBUTTON,
    ];

    internal static bool Draw(Configuration config)
    {
        if (!capturing)
        {
            if (ImGui.Button($"{Label(config)}##desmeter-key")) capturing = true;

            ImGui.SameLine();
            ImGui.TextDisabled("click, then press a key");
            return false;
        }

        ImGui.TextColored(new Vector4(0.45f, 0.95f, 0.55f, 1f), "press a key...");
        ImGui.SameLine();

        if (ImGui.Button("Cancel##desmeter-key-cancel"))
        {
            capturing = false;
            return false;
        }

        foreach (var key in Plugin.Keys.GetValidVirtualKeys())
        {
            if (Ignored.Contains(key) || !Plugin.Keys[key]) continue;

            if (key == VirtualKey.ESCAPE)
            {
                config.ToggleKey = VirtualKey.NO_KEY;
                config.ToggleCtrl = config.ToggleAlt = config.ToggleShift = false;
            }
            else
            {
                config.ToggleKey = key;
                config.ToggleCtrl = Plugin.Keys[VirtualKey.CONTROL];
                config.ToggleAlt = Plugin.Keys[VirtualKey.MENU];
                config.ToggleShift = Plugin.Keys[VirtualKey.SHIFT];
            }

            capturing = false;
            return true;
        }

        return false;
    }

    internal static string Label(Configuration config)
    {
        if (config.ToggleKey == VirtualKey.NO_KEY) return "unbound";

        return (config.ToggleCtrl ? "Ctrl+" : "")
             + (config.ToggleAlt ? "Alt+" : "")
             + (config.ToggleShift ? "Shift+" : "")
             + config.ToggleKey.GetFancyName();
    }
}
