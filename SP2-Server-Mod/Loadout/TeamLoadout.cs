using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Assets.Scripts;
using Assets.Scripts.Flight;
using Assets.Scripts.Multiplayer;
using Assets.Scripts.Multiplayer.ActivityFramework;
using HarmonyLib;

namespace SP2ServerMod
{
    // The craft of each team (phases F3, and F8 for the list). On the computer of a player who has the mod: when the player is in one of the mod's
    // matches and in a team that has crafts, one of them (the player's choice; the first they can use at the start) is saved as a hidden file in the
    // game's own craft list ("__sp2_<match>_t<team>_<number>__.xml": the game hides names that start and end with two underscores), the player's
    // craft id is pointed at it and the craft is restarted where it is. From then on every spawn of the player (start of the match, respawns)
    // loads that craft, because the game spawns whatever the craft id says. When the player leaves the match or it ends, the id goes back to what
    // it was.
    //
    // A team can have several crafts. Before the match starts the player can pick another one and the craft is swapped at once; once it is
    // running a new pick only changes what the NEXT spawn loads (so a player cannot change vehicle in the air).
    //
    // Nothing here is a rule the server enforces: a player without the mod keeps flying whatever craft they like (the server's craft check, in
    // ServerCraftCheck, accepts any craft of the team's list).
    internal static class TeamLoadout
    {
        private const float Interval = 0.25f;
        private const string ActivityPrefix = "SP2SM.";

        private static float nextCheck;
        private static bool applying;                // we are the ones setting the craft id (the lock below must let it through)
        private static string previousCraftId;       // the player's own craft, to give back
        private static string lockedId;              // the craft id the match requires now (null = no loadout in force)
        private static string appliedKey;            // match|team|number|file|hash|site of what is in force
        private static string appliedTeamKey;        // match|team of what is in force
        private static bool restartPending;          // the craft id changed and the craft must be restarted when it can be
        private static float nextAttemptAt;
        private static string failedKey;             // the loadout that could not be applied (so the message is said once, not every tick)
        private static float lastLockMessage = -100f;

        // What the player can choose from now (null when they are not in a match with crafts for their team) and which one is picked.
        private static List<CraftChoice> options;
        private static int optionIndex;
        private static string optionsTeamKey;
        private static readonly Dictionary<string, int> Chosen = new Dictionary<string, int>();   // match|team -> the player's pick

        public static string Status { get; private set; }

        // The craft id in force for the local player, or null.
        public static string LockedId
        {
            get { return lockedId; }
        }

        public static bool Applying
        {
            get { return applying; }
        }

        // The crafts of the player's team, when there is more than one to choose from.
        public static IList<CraftChoice> Options
        {
            get { return options != null && options.Count > 1 ? options : null; }
        }

        public static int OptionIndex
        {
            get { return optionIndex; }
        }

        // Changes when the list or the pick changes: the menu builds its rows again.
        public static string ChoiceSignature
        {
            get { return options == null ? "" : optionsTeamKey + "|" + optionIndex + "|" + options.Count; }
        }

        public static void Select(int index)
        {
            if (options == null || options.Count < 2) return;
            index = Math.Max(0, Math.Min(options.Count - 1, index));
            if (index == optionIndex) return;
            Chosen[optionsTeamKey] = index;
            optionIndex = index;
            nextCheck = 0f;   // apply it at the next look
        }

        public static void SelectNext()
        {
            if (options != null && options.Count > 1) Select((optionIndex + 1) % options.Count);
        }

        public static void Tick()
        {
            if (UnityEngine.Time.unscaledTime < nextCheck) return;
            nextCheck = UnityEngine.Time.unscaledTime + Interval;

            FlightSceneScript scene = FlightSceneScript.Instance;
            FlightScenePlayer player = scene != null ? scene.LocalPlayer : null;
            if (player == null || player.NetworkPlayer == null)
            {
                // Out of the flight scene: nothing to give back (the game's own state is rebuilt), nothing in force.
                Forget();
                return;
            }

            try
            {
                // The restart that gives the player's own craft back may have had to wait (loading, cooldown).
                if (restartPending && lockedId == null) TryRestart(player);

                string matchId;
                int team;
                bool lobby;
                List<CraftChoice> crafts;
                if (!InMatch(player, out matchId, out team, out lobby, out crafts))
                {
                    options = null;
                    if (lockedId != null) Release(player);
                    Status = "no loadout";
                    return;
                }

                string teamKey = matchId + "|" + team;
                int index;
                if (!Chosen.TryGetValue(teamKey, out index)) index = CraftList.DefaultIndex(crafts, CraftProvider.Available);
                index = Math.Max(0, Math.Min(crafts.Count - 1, index));
                options = crafts;
                optionIndex = index;
                optionsTeamKey = teamKey;

                CraftChoice craft = crafts[index];
                string key = teamKey + "|" + index + "|" + craft.File + "|" + craft.Hash + "|" + craft.Site;
                string hiddenId = HiddenId(matchId, team, index);

                if (appliedKey != key)
                {
                    if (failedKey == key && UnityEngine.Time.unscaledTime < nextAttemptAt)
                    {
                        Status = "the team's craft could not be used (see the message)";
                        return;
                    }
                    Apply(player, key, teamKey, hiddenId, craft, index, crafts.Count, team, lobby);
                }
                else if (player.NetworkPlayer.CraftId != lockedId)
                {
                    // Something changed the craft id behind the lock (the lock below catches the usual ways; this is the net under it).
                    SetCraftId(player, lockedId);
                    restartPending = true;
                    Say("The craft of your team is required in this match");
                }

                if (restartPending) TryRestart(player);
                Status = lockedId != null ? "flying the team's craft (" + craft.Title + ")" : Status;
            }
            catch (Exception e)
            {
                Status = "error (see the log)";
                SP2ServerModPlugin.Log.LogError("TeamLoadout: " + e);
                nextCheck = UnityEngine.Time.unscaledTime + 5f;
            }
        }

        // Is the local player in one of the mod's matches, in a team that has crafts?
        private static bool InMatch(FlightScenePlayer player, out string matchId, out int team, out bool lobby, out List<CraftChoice> crafts)
        {
            matchId = null;
            team = 0;
            lobby = false;
            crafts = null;

            NetworkedActivityScript activity = player.NetworkedActivity;
            if (activity == null || activity.Data == null || activity.Data.Id == null || !activity.Data.Id.StartsWith(ActivityPrefix, StringComparison.Ordinal))
                return false;

            NetworkedActivityPlayer member = activity.GetPlayer(player);
            if (member == null || member.Team == null) return false;

            // Team 1 and team 2 of the match. Free for all (no teams) puts everybody in team 1. The spectator side has no craft.
            if (member.Team.Id == NetworkedActivityTeamIds.Team1) team = 1;
            else if (member.Team.Id == NetworkedActivityTeamIds.Team2) team = 2;
            else return false;

            XElement data = activity.Data.XmlData;
            if (data == null) return false;
            crafts = CraftList.Read(data, team);
            if (crafts.Count == 0) return false;

            matchId = activity.Data.Id.Substring(ActivityPrefix.Length);
            lobby = activity.State == NetworkedActivityState.Created || activity.State == NetworkedActivityState.Initialized;
            return true;
        }

        private static string HiddenId(string matchId, int team, int number)
        {
            string name = CraftProvider.SafeName(matchId).Replace(".xml", "");
            return "__sp2_" + name + "_t" + team + "_" + number + "__.xml";
        }

        private static void Apply(FlightScenePlayer player, string key, string teamKey, string hiddenId, CraftChoice choice, int index, int count, int team, bool lobby)
        {
            XElement craft;
            string where;
            if (!CraftProvider.TryLoad(choice.File, out craft, out where))
            {
                if (choice.Site.Length > 0)
                {
                    // Not on this computer, but it is on the website: download it, and the next check applies it.
                    if (!SiteCrafts.IsDownloading(choice.Site))
                    {
                        Say("Downloading " + choice.Title + " from the website...");
                        SiteCrafts.Start(choice.Site, (downloaded, error) =>
                        {
                            if (error != null) Fail(key, error);
                            else
                            {
                                failedKey = null;
                                nextAttemptAt = 0f;
                                nextCheck = 0f;
                            }
                        });
                    }
                    Status = "downloading the team's craft from the website";
                    return;
                }
                Fail(key, "The team's craft '" + choice.File + "' is not on your computer. Put it in " + CraftProvider.Folder +
                          (count > 1 ? " (or pick another craft of the team)" : ""));
                return;
            }
            if (!string.IsNullOrEmpty(choice.Hash))
            {
                string hash = CraftProvider.HashOf(craft);
                if (hash != choice.Hash)
                {
                    Fail(key, "Your copy of '" + choice.File + "' (from " + where + ") is not the version this match uses. " +
                              (choice.Site.Length > 0 ? "The craft on the website may have been updated since; ask the organizer." : "Ask the organizer for the file."));
                    return;
                }
            }

            string current = player.NetworkPlayer.CraftId;
            if (lockedId == null) previousCraftId = current;   // the first loadout of this stay in the match: remember the player's own craft

            Assets.Scripts.Game.Instance.CraftDatabase.SaveCraft(hiddenId, craft, false, false);
            SetCraftId(player, hiddenId);

            // Changing to another craft of the same team inside a running match: the craft in the air stays, the next spawn loads the new one.
            bool switching = lockedId != null && appliedTeamKey == teamKey;
            lockedId = hiddenId;
            appliedKey = key;
            appliedTeamKey = teamKey;
            failedKey = null;
            restartPending = !switching || lobby;
            nextAttemptAt = 0f;

            string number = count > 1 ? " (" + (index + 1) + " of " + count + ")" : "";
            SP2ServerModPlugin.Log.LogInfo("Loadout: team " + team + " of the match flies '" + choice.File + "'" + number + " (from " + where + "); your own craft was '" + previousCraftId + "'");
            Say(restartPending ? "Your team's craft: " + choice.Title + number
                               : "Your next craft: " + choice.Title + number + " (you get it when you spawn again)");
        }

        // Stops the loadout: the player's own craft id comes back and the craft is restarted with it.
        private static void Release(FlightScenePlayer player)
        {
            if (previousCraftId != null && player.NetworkPlayer.CraftId == lockedId)
            {
                SetCraftId(player, previousCraftId);
                restartPending = true;
                SP2ServerModPlugin.Log.LogInfo("Loadout: you left the match; your craft '" + previousCraftId + "' comes back");
                Say("Your own craft comes back");
            }
            lockedId = null;
            appliedKey = null;
            appliedTeamKey = null;
            failedKey = null;
            previousCraftId = null;
            if (restartPending) TryRestart(player);
        }

        private static void Fail(string key, string message)
        {
            failedKey = key;
            nextAttemptAt = UnityEngine.Time.unscaledTime + 30f;   // look again in a while (the player may have put the file in the folder)
            SP2ServerModPlugin.Log.LogWarning("Loadout: " + message);
            Say(message);
        }

        // Restarts the craft where it is, once the game allows it (not while it loads, not within the cooldown, and only with a craft in flight).
        private static void TryRestart(FlightScenePlayer player)
        {
            if (player.Aircraft == null)
            {
                restartPending = false;   // dead or between crafts: the next spawn already uses the craft id
                return;
            }
            FlightUIScriptAccess ui = new FlightUIScriptAccess(FlightSceneScript.Instance);
            if (player.IsLoadingCraft || ui.Cooldown > 0f) return;

            restartPending = false;
            try
            {
                ui.RestartHere();
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogWarning("Loadout: the restart failed: " + e.Message);
            }
        }

        // Only this class changes the craft id on purpose; the lock lets that through.
        private static void SetCraftId(FlightScenePlayer player, string id)
        {
            applying = true;
            try
            {
                player.NetworkPlayer.CraftId = id;
            }
            finally
            {
                applying = false;
            }
        }

        // The craft id of the local player was about to change (Change craft, copy a craft, the designer...) while a loadout is in force.
        public static string Guard(NetworkPlayerScript target, string wanted)
        {
            if (lockedId == null || applying || wanted == lockedId) return wanted;
            FlightScenePlayer local = FlightSceneScript.Instance != null ? FlightSceneScript.Instance.LocalPlayer : null;
            if (local == null || local.NetworkPlayer != target) return wanted;

            if (UnityEngine.Time.unscaledTime - lastLockMessage > 3f)
            {
                lastLockMessage = UnityEngine.Time.unscaledTime;
                Say("This match requires one of your team's crafts");
            }
            return lockedId;
        }

        // The flight scene went away (or the plugin is being reloaded): forget everything. The game rebuilds its own state with the scene.
        public static void Forget()
        {
            lockedId = null;
            appliedKey = null;
            appliedTeamKey = null;
            failedKey = null;
            previousCraftId = null;
            restartPending = false;
            options = null;
            Status = "not in a flight";
        }

        // The plugin is unloading: give the player's craft id back if a loadout is in force.
        public static void Shutdown()
        {
            FlightScenePlayer player = FlightSceneScript.Instance != null ? FlightSceneScript.Instance.LocalPlayer : null;
            if (lockedId != null && player != null && player.NetworkPlayer != null && previousCraftId != null && player.NetworkPlayer.CraftId == lockedId)
                SetCraftId(player, previousCraftId);
            Forget();
            Chosen.Clear();
        }

        private static void Say(string text)
        {
            Notifier.Say(text);
        }

        // The restart goes through the game's flight UI.
        private struct FlightUIScriptAccess
        {
            private readonly Assets.Scripts.Flight.UI.FlightUIScript ui;

            public FlightUIScriptAccess(FlightSceneScript scene)
            {
                ui = scene.FlightUI;
            }

            public float Cooldown
            {
                get { return ui.LoadCraftCooldown; }
            }

            public void RestartHere()
            {
                ui.RestartHere();
            }
        }
    }

    // Change craft, copying a craft from the player list and the designer all end by setting NetworkPlayer.CraftId. While a loadout is in
    // force that set keeps the team's craft instead. (If the game compiled the setter into its callers this does not catch them; the Tick check
    // in TeamLoadout is the net under it.)
    [HarmonyPatch(typeof(NetworkPlayerScript), "CraftId", MethodType.Setter)]
    internal static class CraftIdLockPatch
    {
        private static void Prefix(NetworkPlayerScript __instance, ref string value)
        {
            value = TeamLoadout.Guard(__instance, value);
        }
    }
}
