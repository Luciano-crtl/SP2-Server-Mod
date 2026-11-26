using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.Scripts.Craft;
using Assets.Scripts.Craft.Parts;
using Assets.Scripts.Flight;
using Assets.Scripts.Flight.Cameras;
using Assets.Scripts.Input;
using Assets.Scripts.Multiplayer.ActivityFramework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SP2ServerMod
{
    // Spectator mode (phase F6), for a player who has the mod. A player who is out of lives in a running match of the mod watches the others:
    // the game's own "impact cam" (KillCameraController: a camera that orbits a part, with the mouse) is put on the cockpit of a player who
    // is still alive, and two keys go to the next and the previous one. It ends when the match ends or the player leaves it.
    //
    // It is only a camera. The player's state in the activity is not touched (they stay "playing" with no craft, out of lives), so the
    // referee, the scores and the game's own rules see the same thing as before. A player without the mod has no spectator camera: they see
    // the game as it is when a player has no craft.
    internal static class Spectator
    {
        private const float Interval = 0.25f;

        private static bool active;
        private static int targetId = -1;            // the player being watched
        private static PartScript targetPart;        // the part the camera is on
        private static float nextCheck;
        private static float nextCamera;
        private static string lastHint;

        public static bool Active
        {
            get { return active; }
        }

        // The text for the screen: who is being watched, and the keys.
        public static string Hint
        {
            get { return active ? lastHint : null; }
        }

        public static void Tick()
        {
            if (!SP2ServerModPlugin.SpectatorEnabled.Value)
            {
                if (active) Stop("switched off");
                return;
            }

            try
            {
                FlightSceneScript scene = FlightSceneScript.Instance;
                FlightScenePlayer me = scene != null ? scene.LocalPlayer : null;
                NetworkedActivityScript activity = me != null ? me.NetworkedActivity : null;

                if (!Wanted(activity, me))
                {
                    if (active) Stop("it is over for you here");
                    return;
                }

                HandleKeys(activity, me);
                if (Time.unscaledTime < nextCheck) return;
                nextCheck = Time.unscaledTime + Interval;

                if (!active)
                {
                    active = true;
                    SP2ServerModPlugin.Log.LogInfo("Spectator: you are out of lives; watching the others");
                    Notifier.Say("You are out of lives: you are watching the others (" + KeyName(SP2ServerModPlugin.SpectatorPreviousKey.Value) + " previous, " +
                                 KeyName(SP2ServerModPlugin.SpectatorNextKey.Value) + " next)");
                    Pick(activity, me, 1);
                }
                else if (!Valid(activity, me, targetId))
                {
                    Pick(activity, me, 1);   // the one being watched died, left or was eliminated: the next one
                }

                KeepCamera();
                lastHint = BuildHint(activity);
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("Spectator failed and is switched off: " + e);
                SP2ServerModPlugin.SpectatorEnabled.Value = false;
                Stop("error");
            }
        }

        // Out of lives, in a match of the mod that is on.
        private static bool Wanted(NetworkedActivityScript activity, FlightScenePlayer me)
        {
            if (me == null || !MatchRules.IsOurs(activity)) return false;
            if (activity.State != NetworkedActivityState.Started && activity.State != NetworkedActivityState.Ending) return false;
            NetworkedActivityPlayer member = activity.GetPlayer(me);
            return member != null && MatchRules.IsEliminated(activity, member);
        }

        // ---- Who can be watched --------------------------------------------------

        // Alive players (not me) with a craft, my own team first, then by player id: a steady order for next and previous.
        private static List<NetworkedActivityPlayer> Candidates(NetworkedActivityScript activity, FlightScenePlayer me)
        {
            NetworkedActivityPlayer mine = activity.GetPlayer(me);
            List<NetworkedActivityPlayer> list = new List<NetworkedActivityPlayer>();
            foreach (NetworkedActivityPlayer player in activity.Players)
            {
                if (player.Player == null || player.PlayerId == mine.PlayerId) continue;
                if (MatchRules.IsEliminated(activity, player)) continue;
                if (PartOf(player) == null) continue;
                list.Add(player);
            }
            list.Sort((a, b) =>
            {
                bool aMine = mine.Team != null && a.Team != null && a.Team.Id == mine.Team.Id;
                bool bMine = mine.Team != null && b.Team != null && b.Team.Id == mine.Team.Id;
                if (aMine != bMine) return aMine ? -1 : 1;
                return a.PlayerId.CompareTo(b.PlayerId);
            });
            return list;
        }

        // The part the camera goes on: the main cockpit of the player's craft, when the craft is there and not destroyed.
        private static PartScript PartOf(NetworkedActivityPlayer player)
        {
            AircraftScript aircraft = player.Player != null ? player.Player.Aircraft : null;
            if (aircraft == null || aircraft.CriticallyDamaged) return null;
            PartScript cockpit = aircraft.MainCockpit;
            return cockpit != null && cockpit.gameObject.activeInHierarchy ? cockpit : null;
        }

        private static bool Valid(NetworkedActivityScript activity, FlightScenePlayer me, int id)
        {
            if (id < 0 || targetPart == null || !targetPart.gameObject.activeInHierarchy) return false;
            NetworkedActivityPlayer player = activity.GetPlayer(id);
            return player != null && !MatchRules.IsEliminated(activity, player) && PartOf(player) != null;
        }

        // Goes to the next (direction 1) or the previous (-1) player in the order.
        private static void Pick(NetworkedActivityScript activity, FlightScenePlayer me, int direction)
        {
            List<NetworkedActivityPlayer> list = Candidates(activity, me);
            if (list.Count == 0)
            {
                targetId = -1;
                targetPart = null;
                return;
            }

            int index = list.FindIndex(p => p.PlayerId == targetId);
            index = index < 0 ? (direction > 0 ? 0 : list.Count - 1) : (index + direction + list.Count) % list.Count;
            NetworkedActivityPlayer chosen = list[index];
            targetId = chosen.PlayerId;
            targetPart = PartOf(chosen);
            nextCamera = 0f;   // put the camera on it at once
            SP2ServerModPlugin.Log.LogInfo("Spectator: watching " + chosen.Name);
        }

        // ---- The camera ------------------------------------------------------------

        // The game puts the player on foot when the craft goes, and its own keys change the view: the camera is put back on the target
        // whenever it is not on it.
        private static void KeepCamera()
        {
            CameraManagerScript cameras = CameraManagerScript.Instance;
            if (cameras == null || targetPart == null) return;
            if (Time.unscaledTime < nextCamera) return;
            nextCamera = Time.unscaledTime + 0.5f;

            if (cameras.IsKillCam && cameras.Controller != null && ReferenceEquals(watched, targetPart)) return;
            cameras.EnterKillCam(targetPart);
            watched = targetPart;
        }

        private static PartScript watched;   // the part the camera was last put on

        private static void Stop(string why)
        {
            if (active)
            {
                try
                {
                    CameraManagerScript cameras = CameraManagerScript.Instance;
                    if (cameras != null && cameras.IsKillCam) cameras.ExitKillCam();
                }
                catch (Exception e)
                {
                    SP2ServerModPlugin.Log.LogWarning("Spectator: the camera could not be given back: " + e.Message);
                }
                SP2ServerModPlugin.Log.LogInfo("Spectator ends (" + why + ")");
            }
            active = false;
            targetId = -1;
            targetPart = null;
            watched = null;
            lastHint = null;
        }

        // ---- Keys -------------------------------------------------------------------

        private static void HandleKeys(NetworkedActivityScript activity, FlightScenePlayer me)
        {
            if (!active || UiFocus.TypingInATextField()) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            Key next = SP2ServerModPlugin.SpectatorNextKey.Value;
            Key previous = SP2ServerModPlugin.SpectatorPreviousKey.Value;
            if (next != Key.None && keyboard[next].wasPressedThisFrame) Pick(activity, me, 1);
            else if (previous != Key.None && keyboard[previous].wasPressedThisFrame) Pick(activity, me, -1);
        }

        private static string KeyName(Key key)
        {
            switch (key)
            {
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.None: return "-";
                default: return key.ToString();
            }
        }

        private static string BuildHint(NetworkedActivityScript activity)
        {
            string keys = KeyName(SP2ServerModPlugin.SpectatorPreviousKey.Value) + " previous   " + KeyName(SP2ServerModPlugin.SpectatorNextKey.Value) + " next";
            NetworkedActivityPlayer player = targetId >= 0 ? activity.GetPlayer(targetId) : null;
            if (player == null) return "Out of lives. Nobody left to watch.";
            string team = player.Team != null ? (player.Team.Id == NetworkedActivityTeamIds.Team2 ? "  (team 2)" : "  (team 1)") : "";
            return "Watching " + player.Name + team + "      " + keys;
        }

        // ---- Drawn over the flight view -----------------------------------------------

        private static GUIStyle style;

        public static void OnGui()
        {
            string hint = Hint;
            if (hint == null || Event.current == null || Event.current.type != EventType.Repaint) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };
                style.normal.textColor = Color.white;
            }
            float width = Mathf.Min(Screen.width - 40f, 640f);
            GUI.Box(new Rect((Screen.width - width) / 2f, Screen.height - 80f, width, 40f), hint, style);
        }

        public static void Shutdown()
        {
            if (active) Stop("unloading");
            style = null;
        }

        // The game's keys that change the view (C by default) do nothing while spectating: they would take the camera off the target.
        internal static void BlockViewKeys(object input, ref bool result)
        {
            if (!result || !active) return;
            GameInputs inputs = GameInputs.Instance;
            if (inputs == null) return;
            if (ReferenceEquals(input, inputs.NextView) || ReferenceEquals(input, inputs.PreviousView)) result = false;
        }
    }

    [HarmonyPatch(typeof(GameInput), "GetButtonDownIfEnabled")]
    internal static class SpectatorViewKeyPatch
    {
        private static void Postfix(GameInput __instance, ref bool __result)
        {
            Spectator.BlockViewKeys(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(GameInput), "GetButtonDown")]
    internal static class SpectatorViewKeyDownPatch
    {
        private static void Postfix(GameInput __instance, ref bool __result)
        {
            Spectator.BlockViewKeys(__instance, ref __result);
        }
    }
}
