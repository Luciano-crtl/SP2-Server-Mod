using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.Flight;
using Assets.Scripts.Multiplayer;
using Assets.Scripts.Multiplayer.ActivityFramework;
using HarmonyLib;

namespace SP2ServerMod
{
    // The rules of a match, enforced on the server (phase F4). The game's deathmatch counts kills ("Score") and deaths ("Deaths") for every
    // player, on every machine, and ends the match only when the timer runs out. This adds, for the mod's own matches only:
    //   - the end of the match when the kill limit is reached, or when only one player (or team) still has lives,
    //   - no coming back for a player who has no lives left (the server refuses the spawn: RespawnGuard),
    //   - no joining a match that is running when it has lives (a player could otherwise leave and join again with fresh lives),
    //   - the craft check (CraftCheck).
    // It works whoever flies, with or without the mod: only the server needs the mod. Where the server is a player's own game (hosting from the
    // game's menu), that game is the server.
    internal static class MatchReferee
    {
        private const float Interval = 0.5f;

        private sealed class Watch
        {
            public float NextCheck;
            public int MostPlayers;     // the most players seen in the match: one player alone is never "the last one standing"
            public bool Ended;
            public bool Announced;      // the opening message was sent
            public readonly Dictionary<int, RefereeEntry> Last = new Dictionary<int, RefereeEntry>();
            public readonly Queue<string> Messages = new Queue<string>();
            public float NextMessage;
            public float NextLink;
            public readonly Dictionary<int, int> Linked = new Dictionary<int, int>();   // player id -> the team whose craft link was settled (sent, or not needed)
            public readonly Dictionary<int, KeyValuePair<int, float>> Waiting = new Dictionary<int, KeyValuePair<int, float>>();   // player id -> (team, when first seen on it)
        }

        private static readonly Dictionary<NetworkedActivityScript, Watch> Watches = new Dictionary<NetworkedActivityScript, Watch>();

        // Called on the server for every activity, every frame.
        public static void Tick(NetworkedActivityScript activity)
        {
            if (!MatchRules.IsOurs(activity)) return;
            bool referee = SP2ServerModPlugin.Referee.Value;
            bool announce = SP2ServerModPlugin.Announce.Value;
            bool link = SP2ServerModPlugin.CraftLink.Value;
            if (!referee && !announce && !link) return;

            Watch watch;
            if (!Watches.TryGetValue(activity, out watch))
            {
                watch = new Watch();
                Watches[activity] = watch;
                if (Watches.Count > 8) Prune();
            }
            if (watch.Ended) return;

            SendQueued(activity, watch, false);
            if (link) SendCraftLinks(activity, watch);
            if (activity.State != NetworkedActivityState.Started) return;
            if (UnityEngine.Time.unscaledTime < watch.NextCheck) return;
            watch.NextCheck = UnityEngine.Time.unscaledTime + Interval;

            try
            {
                if (announce && !watch.Announced)
                {
                    watch.Announced = true;
                    int seconds = 0;
                    int.TryParse((string)activity.Data.XmlData.Attribute("timer"), out seconds);
                    watch.Messages.Enqueue(RefereeLogic.StartText(seconds, MatchRules.Lives(activity), MatchRules.KillLimit(activity), MatchRules.IsTeamMatch(activity)));
                }

                List<RefereeEntry> playing = Look(activity, watch);
                if (announce)
                {
                    foreach (string message in RefereeLogic.Changes(watch.Last, playing, MatchRules.Lives(activity), MatchRules.IsTeamMatch(activity)))
                        watch.Messages.Enqueue(message);
                }
                watch.Last.Clear();
                foreach (RefereeEntry entry in playing) watch.Last[entry.Id] = entry;

                if (!referee) return;
                string result = RefereeLogic.Decide(playing, MatchRules.Lives(activity), MatchRules.KillLimit(activity), MatchRules.IsTeamMatch(activity), watch.MostPlayers);
                if (result == null) return;

                watch.Ended = true;
                SP2ServerModPlugin.Log.LogInfo("Referee: the match '" + activity.Data.DisplayName + "' ends: " + result);
                SendQueued(activity, watch, true);   // what happened last comes before the result
                activity.ShowMessageToAllPlayers(result, true, true, 10f);
                activity.EndActivity();
            }
            catch (Exception e)
            {
                watch.Ended = true;   // do not repeat a failure every half second
                SP2ServerModPlugin.Log.LogError("Referee: " + e);
            }
        }

        // A player who is on a team whose craft is on the website gets the link in their chat, once for each team they join, unless they have the
        // mod (it swaps the craft by itself and says hello to the host: ModHello). A new team member is given a few seconds for the hello to arrive.
        private static void SendCraftLinks(NetworkedActivityScript activity, Watch watch)
        {
            if (activity.State == NetworkedActivityState.Ending || activity.State == NetworkedActivityState.Ended || activity.State == NetworkedActivityState.Destroyed) return;
            if (UnityEngine.Time.unscaledTime < watch.NextLink) return;
            watch.NextLink = UnityEngine.Time.unscaledTime + 1f;

            foreach (NetworkedActivityPlayer member in activity.Players)
            {
                if (member.IsAI || member.Team == null || member.Player == null) continue;
                int team = member.Team.Id == NetworkedActivityTeamIds.Team1 ? 1 : member.Team.Id == NetworkedActivityTeamIds.Team2 ? 2 : 0;
                if (team == 0) continue;
                int sent;
                if (watch.Linked.TryGetValue(member.PlayerId, out sent) && sent == team) continue;

                KeyValuePair<int, float> waiting;
                if (!watch.Waiting.TryGetValue(member.PlayerId, out waiting) || waiting.Key != team)
                {
                    waiting = new KeyValuePair<int, float>(team, UnityEngine.Time.unscaledTime);
                    watch.Waiting[member.PlayerId] = waiting;
                }
                if (ModHello.IsModded(member.PlayerId))
                {
                    watch.Linked[member.PlayerId] = team;   // has the mod: nothing to send
                    continue;
                }
                if (UnityEngine.Time.unscaledTime - waiting.Value < ModHello.GraceSeconds) continue;   // the hello may still be on its way
                watch.Linked[member.PlayerId] = team;

                List<CraftChoice> crafts = CraftList.Read(activity.Data.XmlData, team);
                for (int i = 0; i < crafts.Count; i++)
                {
                    if (crafts[i].Site.Length == 0) continue;
                    ChatTo(member.Player.NetworkPlayer, RefereeLogic.CraftLinkText(team, crafts[i].Site, crafts[i].Title, i + 1, crafts.Count));
                }
            }
        }

        // A line in the chat of one player, as a message from the server (the game turns an address on simpleplanes.com into a link that opens when
        // clicked). The game's own method for this is private: it is the one the server uses to answer a player's chat message.
        public static void ChatTo(NetworkPlayerScript player, string text)
        {
            try
            {
                FlightSceneNetworkScript network = FlightSceneScript.Instance != null ? FlightSceneScript.Instance.FlightSceneNetwork : null;
                if (network == null || player == null || player.Owner == null) return;
                if (player.Owner.IsLocalClient) return;   // the host's own game is the one that is sending

                if (chatToTarget == null)
                    chatToTarget = typeof(FlightSceneNetworkScript).GetMethod("SendChatMessageToTarget", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (chatToTarget == null)
                {
                    SP2ServerModPlugin.Log.LogWarning("The chat cannot be used to send the craft link (the game's method was not found)");
                    return;
                }
                // owner id -1: no player has it, so the chat shows it as a message of the server, without a name.
                chatToTarget.Invoke(network, new object[] { player.Owner, -1, System.Text.Encoding.UTF8.GetBytes(text) });
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogWarning("Could not write in the chat of the player: " + e.Message);
            }
        }

        private static MethodInfo chatToTarget;

        // The players taking part, as the referee sees them.
        private static List<RefereeEntry> Look(NetworkedActivityScript activity, Watch watch)
        {
            List<RefereeEntry> playing = new List<RefereeEntry>();
            foreach (NetworkedActivityPlayer player in activity.Players)
            {
                if (player.State != NetworkedActivityPlayerState.Playing && player.State != NetworkedActivityPlayerState.Starting) continue;
                int team = player.Team == null ? 0 : player.Team.Id == NetworkedActivityTeamIds.Team1 ? 1 : player.Team.Id == NetworkedActivityTeamIds.Team2 ? 2 : 0;
                playing.Add(new RefereeEntry { Id = player.PlayerId, Name = player.Name, Team = team, Kills = MatchRules.Kills(player), Deaths = MatchRules.Deaths(player) });
            }
            if (playing.Count > watch.MostPlayers) watch.MostPlayers = playing.Count;
            return playing;
        }

        // Messages to everybody, one at a time (the game shows them on the screen of players without the mod too, so a burst of deaths
        // must not bury them). all = send everything now.
        private static void SendQueued(NetworkedActivityScript activity, Watch watch, bool all)
        {
            while (watch.Messages.Count > 0 && (all || UnityEngine.Time.unscaledTime >= watch.NextMessage))
            {
                string message = watch.Messages.Dequeue();
                SP2ServerModPlugin.Log.LogInfo("Referee says: " + message);
                activity.ShowMessageToAllPlayers(message, true, false, 6f);
                watch.NextMessage = UnityEngine.Time.unscaledTime + 1.5f;
                if (!all) break;
            }
        }

        private static void Prune()
        {
            List<NetworkedActivityScript> gone = new List<NetworkedActivityScript>();
            foreach (NetworkedActivityScript activity in Watches.Keys)
                if (activity == null) gone.Add(activity);
            foreach (NetworkedActivityScript activity in gone) Watches.Remove(activity);
        }

        public static void Shutdown()
        {
            Watches.Clear();
        }

        // A line to the player that was refused something.
        public static void Tell(NetworkPlayerScript player, string text)
        {
            try
            {
                FlightSceneScript scene = FlightSceneScript.Instance;
                if (scene != null && scene.FlightSceneNetwork != null && player != null)
                    scene.FlightSceneNetwork.ShowMessageToTargetPlayer(player, text, true, true);
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogWarning("Could not tell the player '" + text + "': " + e.Message);
            }
        }
    }

    // The server's turn of every activity: the referee looks at the match.
    [HarmonyPatch(typeof(NetworkedActivityScript), "OnUpdateServer")]
    internal static class RefereeTickPatch
    {
        private static void Postfix(NetworkedActivityScript __instance)
        {
            try
            {
                MatchReferee.Tick(__instance);
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("Referee tick: " + e.Message);
            }
        }
    }

    // A player cannot join a match with lives once it is running: leaving and joining again would give fresh lives.
    [HarmonyPatch(typeof(NetworkedActivityScript), "CanJoinActivity")]
    internal static class LateJoinPatch
    {
        private static void Postfix(NetworkedActivityScript __instance, ref bool __result, ref string joinDeniedReason)
        {
            if (!__result || !SP2ServerModPlugin.EnforceRespawn.Value) return;
            if (!MatchRules.IsOurs(__instance) || MatchRules.Lives(__instance) <= 0 || !MatchRules.IsRunning(__instance)) return;
            __result = false;
            joinDeniedReason = "This match has lives and it is already running";
        }
    }

    // On the server: no spawn for a player who is out of lives (whatever way they ask for it: menu, keys, Change craft, the designer).
    [HarmonyPatch]
    internal static class ServerRespawnPatch
    {
        private static MethodBase TargetMethod()
        {
            // The name has a hash in it that changes with game updates (RpcLogic___RpcSpawnPlayerAircraft___456348060): found by its start.
            foreach (MethodInfo method in typeof(NetworkPlayerScript).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (method.Name.StartsWith("RpcLogic___RpcSpawnPlayerAircraft___", StringComparison.Ordinal)) return method;
            return null;
        }

        private static bool Prefix(NetworkPlayerScript __instance)
        {
            if (!SP2ServerModPlugin.EnforceRespawn.Value) return true;
            string reason = MatchRules.RespawnBlockedReason(__instance.FlightScenePlayer);
            if (reason == null) return true;

            SP2ServerModPlugin.Log.LogInfo("Referee: " + __instance.Name + " asked for a craft and was refused (" + reason + ")");
            MatchReferee.Tell(__instance, reason);
            return false;
        }
    }

    // On the player's own game (with the mod): the same refusal before anything is despawned, so the player is not left half way.
    [HarmonyPatch(typeof(FlightScenePlayer), "SpawnAircraft")]
    internal static class ClientRespawnPatch
    {
        private static bool Prefix(FlightScenePlayer __instance)
        {
            if (__instance.NetworkPlayer == null || !__instance.NetworkPlayer.IsOwner) return true;
            string reason = MatchRules.RespawnBlockedReason(__instance);
            if (reason == null) return true;
            Notifier.Say(reason);
            return false;
        }
    }
}
