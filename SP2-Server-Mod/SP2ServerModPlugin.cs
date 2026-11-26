using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using SP2ServerMod.UI;
using UnityEngine.InputSystem;

namespace SP2ServerMod
{
    // SP2 Server Mod: lets whoever organizes a match define it (spawns, teams, time) and run it as one of the game's own activities.
    // Phase 0/1: the plugin loads, checks the game members it uses, and registers the matches found in
    // BepInEx/config/sp2servermod/*.match.xml as deathmatch activities. See PLANO.md.
    [BepInPlugin(PluginGuid, "SP2 Server Mod", Version)]
    public class SP2ServerModPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.yourname.sp2servermod";
        public const string Version = "0.8.0";

        internal static ManualLogSource Log;
        internal static SP2ServerModPlugin Instance;

        internal static ConfigEntry<Key> MarkSpawnKey;
        internal static ConfigEntry<Key> ReloadMatchesKey;
        internal static ConfigEntry<float> BeamHeight;

        // Server rules (phase F4). They act where the game runs the server: the machine of whoever hosts the match.
        internal static ConfigEntry<bool> Referee;
        internal static ConfigEntry<bool> EnforceRespawn;
        internal static ConfigEntry<CraftCheckMode> CraftCheck;
        internal static ConfigEntry<bool> Announce;
        internal static ConfigEntry<bool> CraftLink;

        // What a player with the mod sees (phase F5).
        internal static ConfigEntry<bool> HudEnabled;
        internal static ConfigEntry<bool> SpawnMarkersEnabled;

        // Spectator mode (phase F6).
        internal static ConfigEntry<bool> SpectatorEnabled;
        internal static ConfigEntry<Key> SpectatorNextKey;
        internal static ConfigEntry<Key> SpectatorPreviousKey;

        private bool updateFailureLogged;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            MarkSpawnKey = Config.Bind("Keys", "MarkSpawnKey", Key.Insert,
                "In flight: writes a <Spawn .../> line for where you are (position, heading, on the ground or not) to the log, shows it on screen and copies it, " +
                "to paste into a .match.xml file. None = off.");
            ReloadMatchesKey = Config.Bind("Keys", "ReloadMatchesKey", Key.End,
                "Reads the .match.xml files again and registers the matches again (no need to restart the game after editing one). None = off.");

            BeamHeight = Config.Bind("Beams", "BeamHeight", 1000f,
                new ConfigDescription("How high (in meters) the vertical beams at the spawns reach (the mod's menu, Spawn beams).",
                    new AcceptableValueRange<float>(100f, 3000f)));

            Referee = Config.Bind("Server", "Referee", true,
                "Server: ends the mod's matches by the rules: the kill limit is reached, or only one player (or team) still has lives. Without it only the timer ends a match.");
            EnforceRespawn = Config.Bind("Server", "EnforceRespawn", true,
                "Server: a player who is out of lives cannot spawn again in the match (whatever way they ask), and nobody can join a match with lives that is already running.");
            CraftCheck = Config.Bind("Server", "CraftCheck", CraftCheckMode.Log,
                "Server: what to do when a player's craft is not the one the match asks for their team. Off = nothing; Log = write 'craft check' lines in the log and let them in " +
                "(use this first, to see that the hashes match in your game); Enforce = remove the craft and tell the player which one to fly.");

            Announce = Config.Bind("Server", "Announce", true,
                "Server: says what happens in the mod's matches on everybody's screen, with the game's own messages (so players without the mod see them too): " +
                "the start, who was shot down (with lives: how many are left), who is out and how many remain.");
            CraftLink = Config.Bind("Server", "CraftLink", true,
                "Server: when a player joins a team whose craft is on the SimplePlanes website, writes the link in their chat (the game makes it clickable), " +
                "and again when their craft is not the team's. For players without the mod, who cannot swap crafts on their own.");
            HudEnabled = Config.Bind("Display", "MatchDisplay", true,
                "Shows the state of the match (time, lives, kills, who is alive, the score of the teams) in bars on the left of the flight screen. Only you see it, and only with the mod.");
            SpawnMarkersEnabled = Config.Bind("Display", "SpawnMarkers", true,
                "Before a match starts, marks the spawns of your team in the world, with the distance. Only you see it, and only with the mod.");

            SpectatorEnabled = Config.Bind("Spectator", "Enabled", true,
                "When you are out of lives in a match of the mod, the camera follows the players who are still alive (the game's impact camera on their cockpit). Only you, and only with the mod.");
            SpectatorNextKey = Config.Bind("Spectator", "NextKey", Key.Period, "Watch the next player (while spectating). None = off.");
            SpectatorPreviousKey = Config.Bind("Spectator", "PreviousKey", Key.Comma, "Watch the previous player (while spectating). None = off.");

            Log.LogInfo("SP2 Server Mod " + Version + " loading");
            CompatibilityCheck.Run();
            Patcher.Apply();
            Log.LogInfo("SP2 Server Mod " + Version + " loaded. Matches folder: " + MatchStore.Folder);

            // One line with everything a report needs: the game and the rules this copy applies when it hosts.
            Log.LogInfo("Game " + UnityEngine.Application.version + ". Server rules (when this game hosts): referee " + OnOff(Referee.Value) +
                        ", no spawn without lives " + OnOff(EnforceRespawn.Value) + ", announce " + OnOff(Announce.Value) + ", craft link " + OnOff(CraftLink.Value) +
                        ", craft check " + CraftCheck.Value + ". Display: match bars " + OnOff(HudEnabled.Value) + ", spawn markers " + OnOff(SpawnMarkersEnabled.Value) +
                        ", spectator " + OnOff(SpectatorEnabled.Value));
        }

        private static string OnOff(bool on)
        {
            return on ? "on" : "off";
        }

        private void Update()
        {
            try
            {
                MatchRegistry.Tick();
                SpawnMarker.Tick();
                SpawnBeams.Tick();
                ServerPanel.Tick();
                TeamLoadout.Tick();
                SiteCrafts.Tick();
                MatchHud.Tick();
                ModHello.Tick();
                Spectator.Tick();
            }
            catch (Exception e)
            {
                if (!updateFailureLogged)
                {
                    updateFailureLogged = true;
                    Log.LogError("SP2 Server Mod update failed (said once): " + e);
                }
            }
        }

        private void OnGUI()
        {
            try
            {
                SpawnMarkers.OnGui();
                Spectator.OnGui();
            }
            catch (Exception e)
            {
                if (!updateFailureLogged)
                {
                    updateFailureLogged = true;
                    Log.LogError("SP2 Server Mod markers failed (said once): " + e);
                }
            }
        }

        private static void Step(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.LogWarning("SP2 Server Mod shutdown (" + what + "): " + e.Message);
            }
        }

        private void OnDestroy()
        {
            // The plugin was reloaded (F6) or the game is closing: take our matches out of the game's list, close the menu, remove the
            // beams and the patches. Each step on its own, so one failing does not leave the others behind.
            Step("matches", MatchRegistry.Shutdown);
            Step("loadout", TeamLoadout.Shutdown);
            Step("downloads", SiteCrafts.Shutdown);
            Step("referee", MatchReferee.Shutdown);
            Step("match display", MatchHud.Shutdown);
            Step("hello", ModHello.Shutdown);
            Step("spectator", Spectator.Shutdown);
            Step("markers", SpawnMarkers.Shutdown);
            Step("beams", SpawnBeams.Shutdown);
            Step("menu", ServerPanel.Shutdown);
            Step("patches", Patcher.Remove);
        }
    }
}
