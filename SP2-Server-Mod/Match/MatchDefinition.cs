using System;
using System.Collections.Generic;
using UnityEngine;

namespace SP2ServerMod
{
    internal enum MatchMode
    {
        FreeForAll,
        TeamDeathmatch
    }

    // One place where a player starts (and starts again after dying).
    internal sealed class SpawnPoint
    {
        public Vector3 Position;        // absolute world position (the same numbers the game's own start locations use)
        public Vector3 Rotation;        // euler angles, normally (0, heading, 0)
        public bool OnGround = true;
        public float Speed;             // m/s along the heading when starting in the air
        public float Spread = 15f;      // the game places crafts that start together side by side, up to this many meters

        public SpawnPoint Clone()
        {
            return (SpawnPoint)MemberwiseClone();
        }
    }

    internal sealed class TeamDefinition
    {
        public int Number;              // 1 or 2
        public readonly List<SpawnPoint> Spawns = new List<SpawnPoint>();

        // The crafts the players of this team may fly (phase F3; a list since F8). The host adds as many as he wants (CraftList.Max). Empty = the
        // players fly their own.
        public readonly List<CraftChoice> Crafts = new List<CraftChoice>();

        public bool HasCraft
        {
            get { return Crafts.Count > 0; }
        }

        public string GameId
        {
            get { return Number == 2 ? "Team2" : "Team1"; }
        }

        public TeamDefinition Clone()
        {
            TeamDefinition copy = new TeamDefinition { Number = Number };
            foreach (SpawnPoint spawn in Spawns) copy.Spawns.Add(spawn.Clone());
            foreach (CraftChoice craft in Crafts) copy.Crafts.Add(craft.Clone());
            return copy;
        }
    }

    // A match as the organizer describes it (a .match.xml file). Phase 1: spawns, time and teams.
    internal sealed class MatchDefinition
    {
        public string Id;
        public string Name;
        public string Description;
        public MatchMode Mode = MatchMode.FreeForAll;
        public int TimerSeconds = 300;
        public int Lives;               // 0 = no limit. Saved in the file and in the activity data; enforced from phase F4
        public int KillLimit;           // 0 = no limit. The same
        public string SourceFile;
        public readonly List<TeamDefinition> Teams = new List<TeamDefinition>();

        public string ActivityId
        {
            get { return "SP2SM." + Id; }
        }

        public TeamDefinition Team(int number)
        {
            foreach (TeamDefinition team in Teams)
                if (team.Number == number) return team;
            return null;
        }

        public TeamDefinition GetOrAddTeam(int number)
        {
            TeamDefinition team = Team(number);
            if (team == null)
            {
                team = new TeamDefinition { Number = number };
                Teams.Add(team);
                Teams.Sort((a, b) => a.Number.CompareTo(b.Number));
            }
            return team;
        }

        // A copy that shares nothing with this one (a new id and file are given by whoever duplicates it).
        public MatchDefinition Clone()
        {
            MatchDefinition copy = new MatchDefinition
            {
                Id = Id,
                Name = Name,
                Description = Description,
                Mode = Mode,
                TimerSeconds = TimerSeconds,
                Lives = Lives,
                KillLimit = KillLimit,
            };
            foreach (TeamDefinition team in Teams) copy.Teams.Add(team.Clone());
            return copy;
        }

        public IEnumerable<SpawnPoint> AllSpawns()
        {
            foreach (TeamDefinition team in Teams)
                foreach (SpawnPoint spawn in team.Spawns)
                    yield return spawn;
        }

        // Problems that stop the match from working (errors) or that will surprise the organizer (warnings).
        public void Validate(List<string> errors, List<string> warnings)
        {
            TeamDefinition team1 = Team(1);
            if (team1 == null || team1.Spawns.Count == 0)
                errors.Add("team 1 has no spawn");
            if (Mode == MatchMode.TeamDeathmatch)
            {
                TeamDefinition team2 = Team(2);
                if (team2 == null || team2.Spawns.Count == 0)
                    errors.Add("team deathmatch needs spawns for team 2 as well");
            }
            else if (Team(2) != null && Team(2).Spawns.Count > 0)
            {
                warnings.Add("team 2 spawns are ignored in free-for-all (every player is on team 1's spawns)");
            }
            if (TimerSeconds <= 0)
                warnings.Add("timer is " + TimerSeconds + " s: the match would end at once");

            // The game tells spawn locations apart by their position rounded to 10 m (StartLocationData.GetSpawnLocationHashCode):
            // two spawns in the same 10 m cell count as one.
            Dictionary<string, int> cells = new Dictionary<string, int>();
            int index = 0;
            foreach (SpawnPoint spawn in AllSpawns())
            {
                index++;
                string cell = Mathf.RoundToInt(spawn.Position.x / 10f) + "," + Mathf.RoundToInt(spawn.Position.y / 10f) + "," + Mathf.RoundToInt(spawn.Position.z / 10f);
                int other;
                if (cells.TryGetValue(cell, out other))
                    warnings.Add("spawns " + other + " and " + index + " are in the same 10 m cell: the game treats them as the same place");
                else cells[cell] = index;
            }
        }
    }
}
