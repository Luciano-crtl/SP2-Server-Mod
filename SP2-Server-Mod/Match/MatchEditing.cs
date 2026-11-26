using System;
using System.Globalization;
using Assets.Scripts.Flight;
using Assets.Scripts.Flight.StartLocations;
using UnityEngine;

namespace SP2ServerMod
{
    // What the menu does to matches: create, duplicate, delete, change, add/move/delete spawns, go to a spawn. Every change is saved to the
    // match's file at once and the match is registered again in the game's activity list (Commit), so the file, the menu and the game agree.
    internal static class MatchEditing
    {
        // ---- Where the player is -------------------------------------------------------

        // A spawn at the player's position: the heading it looks along, on the ground if within 10 m of it (else in the air at its speed).
        public static bool ReadHere(out SpawnPoint spawn)
        {
            spawn = null;
            FlightSceneScript scene = FlightSceneScript.Instance;
            FlightScenePlayer player = scene != null ? scene.LocalPlayer : null;
            if (player == null) return false;

            float height = (player.Aircraft?.MainCockpit?.GetAltitudeAgl(1f)).GetValueOrDefault();
            bool onGround = height < 10f;
            spawn = new SpawnPoint
            {
                Position = player.GlobalPosition,
                Rotation = new Vector3(0f, player.Rotation.y, 0f),
                OnGround = onGround,
                Speed = onGround ? 0f : Mathf.Round(player.Velocity.magnitude * 10f) / 10f,
                Spread = 15f,
            };
            return true;
        }

        // ---- Matches ----------------------------------------------------------------------

        public static MatchDefinition CreateNew()
        {
            MatchDefinition match = new MatchDefinition
            {
                Id = MatchStore.FreeId("match"),
                Name = "New match",
                Description = "A match made with the SP2 Server Mod.",
            };
            match.GetOrAddTeam(1);
            MatchStore.Save(match);
            MatchRegistry.Add(match);
            Notifier.Say("Created '" + match.Name + "' (" + match.Id + "): add its spawns with 'Add spawn here'");
            return match;
        }

        public static MatchDefinition Duplicate(MatchDefinition source)
        {
            MatchDefinition copy = source.Clone();
            copy.Id = MatchStore.FreeId(source.Id + "-copy");
            copy.Name = (source.Name ?? source.Id) + " (copy)";
            copy.SourceFile = null;
            MatchStore.Save(copy);
            MatchRegistry.Add(copy);
            Notifier.Say("Duplicated as '" + copy.Name + "'");
            return copy;
        }

        public static void DeleteMatch(MatchDefinition match)
        {
            if (!MatchStore.Delete(match)) return;
            SpawnBeams.RemoveMatch(match.Id);
            MatchRegistry.Remove(match);
            Notifier.Say("Deleted '" + match.Name + "' (its file was kept in the 'deleted' folder)");
        }

        // The match was changed: write it to its file and register it again.
        public static void Commit(MatchDefinition match)
        {
            if (!MatchStore.Save(match))
                Notifier.Say("Could not save '" + match.Name + "' (see the log)");
            MatchRegistry.Apply(match);
        }

        public static void SetTeams(MatchDefinition match, bool teams)
        {
            match.Mode = teams ? MatchMode.TeamDeathmatch : MatchMode.FreeForAll;
            if (teams) match.GetOrAddTeam(2);
            Commit(match);
        }

        // ---- Spawns -----------------------------------------------------------------------

        public static void AddSpawnHere(MatchDefinition match, int teamNumber)
        {
            SpawnPoint spawn;
            if (!ReadHere(out spawn))
            {
                Notifier.Say("Fly to where the spawn should be first: you are not in a flight");
                return;
            }
            TeamDefinition team = match.GetOrAddTeam(teamNumber);
            team.Spawns.Add(spawn);
            SpawnBeams.Set(SpawnBeams.KeyOf(match, team, team.Spawns.Count - 1), true);   // so it can be seen at once
            Commit(match);
            Notifier.Say("Spawn " + team.Spawns.Count + " added to team " + teamNumber + " of '" + match.Name + "'");
        }

        // The spawn keeps its spread and, on the ground, its speed of 0; position, heading and ground/air come from where the player is.
        public static void MoveSpawnHere(MatchDefinition match, TeamDefinition team, int index)
        {
            SpawnPoint here;
            if (!ReadHere(out here))
            {
                Notifier.Say("You are not in a flight");
                return;
            }
            SpawnPoint spawn = team.Spawns[index];
            spawn.Position = here.Position;
            spawn.Rotation = here.Rotation;
            spawn.OnGround = here.OnGround;
            spawn.Speed = here.Speed;
            Commit(match);
            Notifier.Say("Spawn " + (index + 1) + " of team " + team.Number + " moved here");
        }

        public static void DeleteSpawn(MatchDefinition match, TeamDefinition team, int index)
        {
            team.Spawns.RemoveAt(index);
            SpawnBeams.RemoveSpawn(match, team, index);   // the beams of the spawns after it follow their spawns to their new numbers
            Commit(match);
            Notifier.Say("Spawn " + (index + 1) + " of team " + team.Number + " deleted");
        }

        // ---- The crafts of a team (phase F3; a list since F8) -------------------------------------

        // The craft the player is flying is added to the team's crafts: it is saved in the mod's crafts folder and its hash is written in the match.
        public static void AddCraftFromFlying(MatchDefinition match, int teamNumber)
        {
            CraftChoice craft;
            string error;
            if (!CraftProvider.SaveFlying(match, teamNumber, out craft, out error))
            {
                Notifier.Say(error);
                return;
            }
            AddCraft(match, teamNumber, craft);
        }

        // A craft from the player's own craft list (by its name there) is added to the team's crafts.
        public static void AddCraftFromGame(MatchDefinition match, int teamNumber, string id)
        {
            CraftChoice craft;
            string error;
            if (!CraftProvider.CopyFromGame(id, match, teamNumber, out craft, out error))
            {
                Notifier.Say(error);
                return;
            }
            AddCraft(match, teamNumber, craft);
        }

        // A craft published on the SimplePlanes website (a link or its id) is added to the team's crafts. It is downloaded now; the players that
        // do not have the file download it themselves when they enter the match.
        public static void AddCraftFromSite(MatchDefinition match, int teamNumber, string text)
        {
            string id = SiteCrafts.ParseId(text);
            if (id.Length == 0)
            {
                Notifier.Say("That is not a link or an id of a craft on the website");
                return;
            }
            Notifier.Say("Downloading craft " + id + " from the website...");
            SiteCrafts.Start(id, (craft, error) =>
            {
                if (error != null)
                {
                    Notifier.Say(error);
                    return;
                }
                AddCraft(match, teamNumber, craft);
            });
        }

        public static void RemoveCraft(MatchDefinition match, int teamNumber, int index)
        {
            TeamDefinition team = match.Team(teamNumber);
            if (team == null || index < 0 || index >= team.Crafts.Count) return;
            string title = team.Crafts[index].Title;
            team.Crafts.RemoveAt(index);
            Commit(match);
            MatchRegistry.Touch();
            Notifier.Say("'" + title + "' is no longer one of team " + teamNumber + "'s crafts" + (team.HasCraft ? "" : ": the players fly their own crafts again"));
        }

        public static void ClearCrafts(MatchDefinition match, int teamNumber)
        {
            TeamDefinition team = match.Team(teamNumber);
            if (team == null || !team.HasCraft) return;
            team.Crafts.Clear();
            Commit(match);
            MatchRegistry.Touch();
            Notifier.Say("Team " + teamNumber + " of '" + match.Name + "' flies the players' own crafts again");
        }

        private static void AddCraft(MatchDefinition match, int teamNumber, CraftChoice craft)
        {
            TeamDefinition team = match.GetOrAddTeam(teamNumber);
            int same = CraftList.IndexOfHash(team.Crafts, craft.Hash);
            if (same >= 0 && !string.Equals(team.Crafts[same].File, craft.File, System.StringComparison.OrdinalIgnoreCase))
            {
                Notifier.Say("'" + team.Crafts[same].Title + "' is already one of team " + teamNumber + "'s crafts");
                return;
            }
            if (!CraftList.Upsert(team.Crafts, craft))
            {
                Notifier.Say("A team can have at most " + CraftList.Max + " crafts");
                return;
            }
            Commit(match);
            MatchRegistry.Touch();
            Notifier.Say("'" + craft.Title + "' added to team " + teamNumber + "'s crafts (" + team.Crafts.Count + "). Give the files in " + CraftProvider.Folder + " to the players");
        }

        // ---- Going to a spawn ------------------------------------------------------------------

        // Teleports the player's own craft there, the way the game's teleport to a player does. Not inside an activity (it would be a way to
        // skip the activity's own start) and not where the server has switched teleporting off.
        public static void GoTo(SpawnPoint spawn)
        {
            FlightSceneScript scene = FlightSceneScript.Instance;
            FlightScenePlayer player = scene != null ? scene.LocalPlayer : null;
            if (player == null)
            {
                Notifier.Say("You are not in a flight");
                return;
            }
            if (player.NetworkedActivity != null)
            {
                Notifier.Say("Leave the activity first: you cannot go to a spawn from inside one");
                return;
            }
            bool single = scene.FlightUI.MultiplayerState == Assets.Scripts.Flight.UI.FlightUIScript.MultiplayerStateType.SinglePlayer;
            if (!single && !scene.FlightSceneNetwork.IsTeleportToPlayerEnabled)
            {
                Notifier.Say("Teleportation has been disabled by the server");
                return;
            }

            PositionUtility.TeleportPlayer(new StartLocation(spawn.Position, spawn.Rotation, spawn.OnGround ? 0f : spawn.Speed, spawn.OnGround));
            Notifier.Say("Teleported to the spawn at " + spawn.Position.x.ToString("0", CultureInfo.InvariantCulture) + ", " +
                         spawn.Position.y.ToString("0", CultureInfo.InvariantCulture) + ", " + spawn.Position.z.ToString("0", CultureInfo.InvariantCulture));
        }
    }
}
