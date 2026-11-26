using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Assets.Scripts.Craft.CraftFiles;
using Assets.Scripts.Flight;
using Assets.Scripts.Multiplayer;
using Assets.Scripts.Multiplayer.ActivityFramework;
using HarmonyLib;

namespace SP2ServerMod
{
    public enum CraftCheckMode
    {
        Off,       // the server does not look at the crafts
        Log,       // it compares and writes the result in the log, and lets everybody in
        Enforce    // a craft that is not the team's is removed, with a message saying which one to fly
    }

    // On the server: when a player's craft arrives (the owner of the craft sends its hash to the server when it finishes loading), compare it with
    // the hash the match asks for that player's team. Works for players with and without the mod. Only while the match is starting or running (in the
    // lobby the players still fly their own crafts, until they are in their team).
    //
    // The hash is the one the game itself computes when a craft is sent (CraftProvider.GameHash), so the same file gives the same hash on the
    // organizer's computer and in the player's game. (If they differ in your game, set CraftCheck to Log and look at the lines "craft check".)
    [HarmonyPatch]
    internal static class ServerCraftCheckPatch
    {
        private static MethodBase TargetMethod()
        {
            // RpcLogic___RpcLoadRemoteCraftServer___1537256194: the number changes with game updates.
            foreach (MethodInfo method in typeof(NetworkAircraftScript).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (method.Name.StartsWith("RpcLogic___RpcLoadRemoteCraftServer___", StringComparison.Ordinal)) return method;
            return null;
        }

        // __0 = the hash the player sent, __1 = the craft's bytes (null when the server already had the craft).
        private static bool Prefix(NetworkAircraftScript __instance, string __0, byte[] __1)
        {
            CraftCheckMode mode = SP2ServerModPlugin.CraftCheck.Value;
            if (mode == CraftCheckMode.Off) return true;

            try
            {
                FlightSceneScript scene = FlightSceneScript.Instance;
                FlightScenePlayer player = scene != null ? scene.GetPlayer(__instance.PlayerId) : null;
                NetworkedActivityScript activity = player != null ? player.NetworkedActivity : null;
                if (!MatchRules.IsOurs(activity) || !MatchRules.IsRunning(activity)) return true;

                NetworkedActivityPlayer member = activity.GetPlayer(player);
                if (member == null || member.Team == null) return true;
                int team = member.Team.Id == NetworkedActivityTeamIds.Team1 ? 1 : member.Team.Id == NetworkedActivityTeamIds.Team2 ? 2 : 0;
                if (team == 0) return true;

                XElement data = activity.Data.XmlData;
                List<CraftChoice> crafts = CraftList.Read(data, team);
                if (crafts.Count == 0) return true;   // this team has no fixed craft

                // The server takes the hash of the bytes when it gets them, and trusts the player's hash when it already had the craft.
                string received = __1 != null ? CraftDatabase.ComputeHash(__1) : __0;
                string who = player.Name + " (team " + team + ")";
                if (CraftList.Accepts(crafts, received))
                {
                    SP2ServerModPlugin.Log.LogInfo("[craft check] " + who + " flies a craft of the team: the hash matches");
                    return true;
                }

                string names = string.Join(", ", crafts.Select(c => "'" + c.Title + "'").ToArray());
                SP2ServerModPlugin.Log.LogWarning("[craft check] " + who + " flies another craft than the team's (" + names + "): the hash is " + received +
                                                  " and the match asks for " + string.Join(" or ", crafts.Select(c => c.Hash).ToArray()) + " (mode " + mode + ")");
                if (mode != CraftCheckMode.Enforce) return true;

                // A player without the mod cannot be given the craft (the game loads a player's craft from their own computer), so the message
                // says the ways to get it: from the website, or by copying a teammate's craft in the player list (a game feature).
                bool anySite = crafts.Any(c => c.Site.Length > 0);
                MatchReferee.Tell(player.NetworkPlayer, (crafts.Count == 1 ? "This match needs the team's craft " + names + ". " : "This match needs one of the team's crafts: " + names + ". ") +
                    (anySite ? "Load " + (crafts.Count == 1 ? "it" : "one") + " with Change craft from the website (the links are in your chat). Or copy" : "Copy") +
                    " a teammate's craft in the player list (select the player, Copy craft), then restart.");
                if (anySite && SP2ServerModPlugin.CraftLink.Value)
                {
                    for (int i = 0; i < crafts.Count; i++)
                        if (crafts[i].Site.Length > 0)
                            MatchReferee.ChatTo(player.NetworkPlayer, RefereeLogic.CraftLinkText(team, crafts[i].Site, crafts[i].Title, i + 1, crafts.Count));
                }
                try
                {
                    __instance.RequestDespawn();
                }
                catch (Exception e)
                {
                    SP2ServerModPlugin.Log.LogWarning("[craft check] the craft could not be removed: " + e.Message);
                }
                return false;
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("[craft check] " + e);
                return true;   // a failure of the check never keeps a player out
            }
        }
    }
}
