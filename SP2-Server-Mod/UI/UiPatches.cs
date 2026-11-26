using System;
using System.Xml.Linq;
using Assets.Scripts.Flight.UI.Panels;
using HarmonyLib;
using Jundroo.Juicy.Widgets;

namespace SP2ServerMod.UI
{
    // The game builds its UI from XML files and finds widget scripts by name inside its own assembly. These three patches add our button, our
    // layout and our script type. (The Armor Mod patches the same methods for its own panel: each patch answers only for its own names and
    // lets the others through, so both work together.)

    // 1) Add our button to the in-flight menu.
    [HarmonyPatch(typeof(MenuPanelScript), "OnWidgetInitialized")]
    internal static class MenuButtonPatch
    {
        private static void Postfix(Widget widget)
        {
            try
            {
                ServerPanel.AddMenuButton(widget);
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("Couldn't add the SP2 Server Mod menu button: " + e);
            }
        }
    }

    // 2) When the game asks for our panel's XML, give it ours instead of looking in its files.
    [HarmonyPatch(typeof(Jundroo.Juicy.ResourceLoader), "LoadXml")]
    internal static class PanelLayoutPatch
    {
        private static bool Prefix(string path, ref XElement __result)
        {
            if (path == ServerPanel.PanelXmlPath)
            {
                __result = ServerPanel.BuildLayout();
                return false;   // skip the original
            }
            return true;        // not ours: run the original
        }
    }

    // 3) The layout names our script class. The game only searches its own assembly, so when that fails for one of our names, we answer.
    [HarmonyPatch(typeof(Jundroo.Juicy.ResourceLoader), "GetScriptType")]
    internal static class PanelScriptTypePatch
    {
        private static void Postfix(string scriptTypeName, ref Type __result)
        {
            if (__result == null && scriptTypeName != null && scriptTypeName.StartsWith("SP2ServerMod."))
                __result = typeof(SP2ServerModPlugin).Assembly.GetType(scriptTypeName);
        }
    }
}
