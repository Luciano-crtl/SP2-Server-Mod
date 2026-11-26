using System.Collections.Generic;
using System.Globalization;

namespace SP2ServerMod
{
    // One player as the referee sees them.
    internal struct RefereeEntry
    {
        public int Id;       // the player's id in the game (names can repeat)
        public string Name;
        public int Team;     // 1 or 2 in a match with two teams; ignored without teams
        public int Kills;
        public int Deaths;
    }

    // The end-of-match decision and the messages about the match, with nothing from the game in it (so they can be tested outside the game).
    internal static class RefereeLogic
    {
        // The address of a craft on the website. The game's chat makes an address on simpleplanes.com clickable (and shows it as it is: there is
        // no way to put a label on it for a player who does not have the mod).
        public static string CraftUrl(string siteId)
        {
            return "https://www.simpleplanes.com/a/" + siteId;
        }

        // The chat line for a player on a team whose craft is on the website. A team with several crafts gets one line for each, with its name
        // and number (a player only needs one of them).
        public static string CraftLinkText(int team, string siteId, string title = null, int number = 1, int count = 1)
        {
            if (count <= 1 || string.IsNullOrEmpty(title))
                return "Team " + team + "'s craft: " + CraftUrl(siteId) + "  (open it, download it, then Change craft and restart)";
            return "Team " + team + " craft " + number + " of " + count + ", " + title + ": " + CraftUrl(siteId) + "  (any one of them will do: open it, download it, then Change craft and restart)";
        }

        // The line that opens the match.
        public static string StartText(int timerSeconds, int lives, int killLimit, bool teams)
        {
            List<string> parts = new List<string>();
            if (timerSeconds > 0) parts.Add("time " + (timerSeconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (timerSeconds % 60).ToString("00", CultureInfo.InvariantCulture));
            parts.Add(teams ? "two teams" : "every player for themselves");
            if (lives > 0) parts.Add(lives + (lives == 1 ? " life" : " lives") + " each");
            if (killLimit > 0) parts.Add("first to " + killLimit + " kills");
            return "The match begins: " + string.Join(", ", parts.ToArray()) + ".";
        }

        // The messages for what happened since the last look: a player died (and, with lives, how many are left or that they are out).
        // last: the players as they were (by id); now: the players now. Only matches with lives say anything about deaths (otherwise every
        // death would be a message). The killer is named when exactly one player's kills went up and exactly one player died.
        public static List<string> Changes(IDictionary<int, RefereeEntry> last, IList<RefereeEntry> now, int lives, bool teams)
        {
            List<string> messages = new List<string>();
            if (lives <= 0) return messages;

            List<RefereeEntry> died = new List<RefereeEntry>();
            List<RefereeEntry> scorers = new List<RefereeEntry>();
            foreach (RefereeEntry player in now)
            {
                RefereeEntry before;
                if (!last.TryGetValue(player.Id, out before)) continue;   // a new player: nothing to compare
                if (player.Deaths > before.Deaths) died.Add(player);
                if (player.Kills > before.Kills) scorers.Add(player);
            }

            foreach (RefereeEntry victim in died)
            {
                string by = died.Count == 1 && scorers.Count == 1 && scorers[0].Id != victim.Id ? " by " + scorers[0].Name : "";
                int left = lives - victim.Deaths;
                if (left > 0)
                {
                    messages.Add(victim.Name + " was shot down" + by + ": " + left + (left == 1 ? " life" : " lives") + " left.");
                    continue;
                }

                int alive = 0, alive1 = 0, alive2 = 0;
                foreach (RefereeEntry player in now)
                {
                    if (player.Deaths >= lives) continue;
                    alive++;
                    if (player.Team == 1) alive1++;
                    else if (player.Team == 2) alive2++;
                }
                string rest = teams ? "Alive: team 1 " + alive1 + ", team 2 " + alive2 + "." : alive + (alive == 1 ? " player" : " players") + " left.";
                messages.Add(victim.Name + " is out of lives" + (by.Length > 0 ? " (shot down" + by + ")" : "") + ". " + rest);
            }
            return messages;
        }

        // The text that says why the match is over and who won, or null while it goes on.
        // players: those still taking part. lives / killLimit: 0 = no limit. mostPlayers: the most players the match has had at once (a lone
        // player is never "the last one standing").
        public static string Decide(IList<RefereeEntry> players, int lives, int killLimit, bool teams, int mostPlayers)
        {
            // Kill limit: a player's own kills, or the kills of a team added together.
            if (killLimit > 0)
            {
                if (!teams)
                {
                    RefereeEntry best = default(RefereeEntry);
                    bool found = false;
                    foreach (RefereeEntry player in players)
                        if (player.Kills >= killLimit && (!found || player.Kills > best.Kills))
                        {
                            best = player;
                            found = true;
                        }
                    if (found) return best.Name + " wins with " + best.Kills + " kills!";
                }
                else
                {
                    int team1 = 0, team2 = 0;
                    foreach (RefereeEntry player in players)
                    {
                        if (player.Team == 1) team1 += player.Kills;
                        else if (player.Team == 2) team2 += player.Kills;
                    }
                    if (team1 >= killLimit && team1 >= team2) return "Team 1 wins with " + team1 + " kills!";
                    if (team2 >= killLimit) return "Team 2 wins with " + team2 + " kills!";
                }
            }

            // Lives: the last player (or team) with lives wins.
            if (lives > 0 && players.Count > 0)
            {
                if (!teams)
                {
                    string last = null;
                    int alive = 0;
                    foreach (RefereeEntry player in players)
                    {
                        if (player.Deaths >= lives) continue;
                        alive++;
                        last = player.Name;
                    }
                    if (alive == 0) return "Everybody is out of lives. Draw!";
                    if (alive == 1 && mostPlayers >= 2) return last + " is the last one standing!";
                }
                else
                {
                    int alive1 = 0, alive2 = 0, total1 = 0, total2 = 0;
                    foreach (RefereeEntry player in players)
                    {
                        bool isOut = player.Deaths >= lives;
                        if (player.Team == 1) { total1++; if (!isOut) alive1++; }
                        else if (player.Team == 2) { total2++; if (!isOut) alive2++; }
                    }
                    if (alive1 + alive2 == 0) return "Both teams are out of lives. Draw!";
                    if (total1 > 0 && total2 > 0)
                    {
                        if (alive1 == 0) return "Team 2 wins: team 1 has no lives left!";
                        if (alive2 == 0) return "Team 1 wins: team 2 has no lives left!";
                    }
                }
            }
            return null;
        }
    }
}
