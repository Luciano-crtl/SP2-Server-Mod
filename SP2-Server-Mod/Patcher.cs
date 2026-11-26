using System;
using HarmonyLib;

namespace SP2ServerMod
{
    // Applies every [HarmonyPatch] class of the mod, each one on its own so that one failing patch does not stop the others, and takes them
    // all off again when the plugin is unloaded. The Harmony id is unique to this copy of the plugin, so a copy loaded after a reload (F6)
    // never has its patches removed by the copy it replaces.
    internal static class Patcher
    {
        private static Harmony harmony;

        public static void Apply()
        {
            harmony = new Harmony(SP2ServerModPlugin.PluginGuid + "." + Guid.NewGuid().ToString("N"));
            foreach (Type type in typeof(Patcher).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    SP2ServerModPlugin.Log.LogInfo("Patch applied: " + type.Name);
                }
                catch (Exception e)
                {
                    SP2ServerModPlugin.Log.LogError("Patch " + type.Name + " failed: " + e.Message);
                }
            }
        }

        public static void Remove()
        {
            if (harmony == null) return;
            harmony.UnpatchSelf();
            harmony = null;
        }
    }
}
