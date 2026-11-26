using System;
using Assets.Scripts.Flight;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SP2ServerMod
{
    // The organizer's tool for spawns: fly or drive to where players should start and press MarkSpawnKey. The <Spawn .../> line for that place
    // goes to the log, onto the screen and onto the clipboard, ready to paste into a .match.xml file.
    internal static class SpawnMarker
    {
        public static void Tick()
        {
            Keyboard keyboard = Keyboard.current;
            Key key = SP2ServerModPlugin.MarkSpawnKey.Value;
            if (keyboard == null || key == Key.None || !keyboard[key].wasPressedThisFrame || UiFocus.TypingInATextField()) return;
            Mark();
        }

        public static void Mark()
        {
            FlightSceneScript scene = FlightSceneScript.Instance;
            SpawnPoint spawn;
            if (!MatchEditing.ReadHere(out spawn)) return;

            string line = "<Spawn position=\"" + MatchStore.Format(spawn.Position, "0.##") + "\" rotation=\"" +
                          MatchStore.Format(spawn.Rotation, "0.#") + "\" onGround=\"" + (spawn.OnGround ? "true" : "false") +
                          "\" speed=\"" + spawn.Speed.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "\" spread=\"15\" />";

            SP2ServerModPlugin.Log.LogInfo("Spawn marked: " + line);
            try
            {
                GUIUtility.systemCopyBuffer = line;
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogWarning("Could not copy the spawn line to the clipboard: " + e.Message);
            }
            if (scene.FlightUI != null)
                scene.FlightUI.ShowMessage("Spawn marked and copied: " + line, 8f, false);
        }
    }
}
