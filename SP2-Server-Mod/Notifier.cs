using System;
using Assets.Scripts.Flight;

namespace SP2ServerMod
{
    // A line in the log and, in a flight, a message on the player's own screen.
    internal static class Notifier
    {
        public static void Say(string text)
        {
            SP2ServerModPlugin.Log.LogInfo(text);
            try
            {
                FlightSceneScript scene = FlightSceneScript.Instance;
                if (scene != null && scene.FlightUI != null) scene.FlightUI.ShowMessage(text, 5f, false);
            }
            catch (Exception)
            {
                // not in a flight: the log line is enough
            }
        }
    }
}
