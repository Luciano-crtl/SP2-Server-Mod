using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Assets.Scripts.Flight;
using Assets.Scripts.Multiplayer.ActivityFramework;

namespace SP2ServerMod
{
    // What a match demands, read from the activity the game is running (its <Data>), and the questions the rules ask of it. Everything here only
    // looks at the game's own state (the scores and the players of the activity), so the same answers come out on the server and on any client.
    internal static class MatchRules
    {
        public const string ActivityPrefix = "SP2SM.";

        public static bool IsOurs(NetworkedActivityScript activity)
        {
            return activity != null && activity.Data != null && activity.Data.Id != null &&
                   activity.Data.Id.StartsWith(ActivityPrefix, StringComparison.Ordinal);
        }

        public static int Lives(NetworkedActivityScript activity)
        {
            return IntAttribute(activity, "lives");
        }

        public static int KillLimit(NetworkedActivityScript activity)
        {
            return IntAttribute(activity, "killLimit");
        }

        public static bool IsTeamMatch(NetworkedActivityScript activity)
        {
            XElement data = activity != null && activity.Data != null ? activity.Data.XmlData : null;
            string text = data != null ? (string)data.Attribute("teamDeathmatch") : null;
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static int IntAttribute(NetworkedActivityScript activity, string name)
        {
            XElement data = activity != null && activity.Data != null ? activity.Data.XmlData : null;
            int value;
            return data != null && int.TryParse((string)data.Attribute(name), out value) ? Math.Max(0, value) : 0;
        }

        public static int Deaths(NetworkedActivityPlayer player)
        {
            NetworkedActivityScore score = player != null ? player.GetScore("Deaths") : null;
            return score != null ? score.ValueInt : 0;
        }

        public static int Kills(NetworkedActivityPlayer player)
        {
            NetworkedActivityScore score = player != null ? player.GetScore("Score") : null;
            return score != null ? score.ValueInt : 0;
        }

        // Out of lives: the player has died as many times as the match allows.
        public static bool IsEliminated(NetworkedActivityScript activity, NetworkedActivityPlayer player)
        {
            int lives = Lives(activity);
            return lives > 0 && Deaths(player) >= lives;
        }

        // The craft is in a match that is running, in a rule's way: the lives and the late joins only matter from the start on.
        public static bool IsRunning(NetworkedActivityScript activity)
        {
            return activity.State == NetworkedActivityState.Starting || activity.State == NetworkedActivityState.Started;
        }

        // Why this player may not spawn a craft now, or null. (An eliminated player of a running match: no more lives.)
        public static string RespawnBlockedReason(FlightScenePlayer player)
        {
            if (player == null) return null;
            NetworkedActivityScript activity = player.NetworkedActivity;
            if (!IsOurs(activity) || activity.State != NetworkedActivityState.Started) return null;
            NetworkedActivityPlayer member = activity.GetPlayer(player);
            if (member == null || !IsEliminated(activity, member)) return null;
            return "You are out of lives (" + Lives(activity) + "): you cannot come back in this match";
        }

        // For the local player's own line in the menu.
        public static string LocalSummary()
        {
            FlightSceneScript scene = FlightSceneScript.Instance;
            FlightScenePlayer player = scene != null ? scene.LocalPlayer : null;
            NetworkedActivityScript activity = player != null ? player.NetworkedActivity : null;
            if (!IsOurs(activity)) return "not in a match";
            NetworkedActivityPlayer member = activity.GetPlayer(player);
            if (member == null) return "joining";

            List<string> parts = new List<string>();
            int lives = Lives(activity);
            if (lives > 0) parts.Add("lives " + Math.Max(0, lives - Deaths(member)) + "/" + lives);
            int limit = KillLimit(activity);
            parts.Add("kills " + Kills(member) + (limit > 0 ? "/" + limit : ""));
            if (IsEliminated(activity, member)) parts.Add("OUT");
            return string.Join(", ", parts.ToArray());
        }
    }
}
