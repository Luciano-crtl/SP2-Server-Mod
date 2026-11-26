using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Assets.Scripts.Multiplayer.ActivityFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SP2ServerMod
{
    // Puts the organizer's matches into the game's list of activities (NetworkedActivityManager). A match is the game's own deathmatch
    // activity with our start locations, time and team setting in its data: nothing new has to exist on the other players' side.
    // The registry also keeps the matches the menu shows (and edits): the same objects, so an edit is one change and one Apply.
    internal static class MatchRegistry
    {
        private const string DeathmatchPrefab = "Deathmatch/DeathmatchActivity";

        private static readonly List<string> RegisteredIds = new List<string>();
        private static readonly Dictionary<string, string> Statuses = new Dictionary<string, string>();
        private static readonly Dictionary<string, List<string>> Warnings = new Dictionary<string, List<string>>();
        private static List<MatchDefinition> loaded = new List<MatchDefinition>();
        private static NetworkedActivityManager lastManager;
        private static bool reloadRequested = true;
        private static float nextCheck;

        // The matches read from the files the last time (the ones the menu lists), also those that could not be registered.
        public static IList<MatchDefinition> Loaded
        {
            get { return loaded; }
        }

        // Goes up when the list of matches changes (read again, one added or removed), so the menu knows to rebuild its rows.
        public static int Version { get; private set; }

        // Something the menu shows changed (a craft came in from the website): the menu builds its rows again.
        public static void Touch()
        {
            Version++;
        }

        public static string StatusOf(string matchId)
        {
            string status;
            return Statuses.TryGetValue(matchId, out status) ? status : "not registered yet";
        }

        // What the validation found that will not stop the match from working but will surprise the organizer.
        public static IList<string> WarningsOf(string matchId)
        {
            List<string> list;
            return Warnings.TryGetValue(matchId, out list) ? list : new List<string>();
        }

        // The same as the ReloadMatchesKey, for the menu's button.
        public static void RequestReload()
        {
            reloadRequested = true;
            nextCheck = 0f;
        }

        public static void Tick()
        {
            Keyboard keyboard = Keyboard.current;
            Key reloadKey = SP2ServerModPlugin.ReloadMatchesKey.Value;
            if (keyboard != null && reloadKey != Key.None && keyboard[reloadKey].wasPressedThisFrame && !UiFocus.TypingInATextField())
            {
                reloadRequested = true;
                nextCheck = 0f;
            }

            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;

            // The manager is created when the game starts; it is there long before any flight, but wait for it anyway.
            NetworkedActivityManager manager = Assets.Scripts.Game.Instance?.NetworkedActivityManager;
            if (manager == null) return;
            if (reloadRequested || !ReferenceEquals(manager, lastManager))
            {
                bool announce = reloadRequested && lastManager != null;
                RegisterAll(manager);
                lastManager = manager;
                reloadRequested = false;
                if (announce) Notifier.Say("Matches reloaded (" + RegisteredIds.Count + ")");
            }
        }

        public static void Shutdown()
        {
            NetworkedActivityManager manager = lastManager;
            if (manager != null) UnregisterAll(manager);
            lastManager = null;
        }

        // ---- Changes made from the menu ----------------------------------------------

        // A match was edited: register it again with its new data (the game keeps its own copy of what an activity already running uses).
        public static void Apply(MatchDefinition match)
        {
            NetworkedActivityManager manager = lastManager;
            if (manager == null)
            {
                Statuses[match.Id] = "not registered yet";
                return;
            }
            RemoveEntry(manager, match.ActivityId);
            RegisterOne(manager, match);
        }

        // A new match (created or duplicated): it joins the list and is registered.
        public static void Add(MatchDefinition match)
        {
            loaded.Add(match);
            Version++;
            Apply(match);
        }

        // A match was deleted: it leaves the list and the game's activity list.
        public static void Remove(MatchDefinition match)
        {
            NetworkedActivityManager manager = lastManager;
            if (manager != null) RemoveEntry(manager, match.ActivityId);
            loaded.Remove(match);
            Statuses.Remove(match.Id);
            Warnings.Remove(match.Id);
            Version++;
        }

        // ---- Registration -------------------------------------------------------------

        private static void RegisterAll(NetworkedActivityManager manager)
        {
            UnregisterAll(manager);

            loaded = MatchStore.LoadAll();
            Statuses.Clear();
            Warnings.Clear();
            Version++;

            foreach (MatchDefinition match in loaded)
                RegisterOne(manager, match);
        }

        private static void RegisterOne(NetworkedActivityManager manager, MatchDefinition match)
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            match.Validate(errors, warnings);
            foreach (TeamDefinition team in match.Teams)
            {
                foreach (CraftChoice craft in team.Crafts)
                {
                    string problem = CraftProvider.Check(craft);
                    if (problem != null) warnings.Add("team " + team.Number + " craft: " + problem);
                }
            }
            Warnings[match.Id] = warnings;
            foreach (string warning in warnings)
                SP2ServerModPlugin.Log.LogWarning("Match '" + match.Id + "': " + warning);
            if (errors.Count > 0)
            {
                SP2ServerModPlugin.Log.LogWarning("Match '" + match.Id + "' is not registered: " + string.Join("; ", errors.ToArray()));
                Statuses[match.Id] = "not registered: " + string.Join("; ", errors.ToArray());
                return;
            }

            try
            {
                XElement activities = new XElement("Activities", BuildActivityXml(match));
                manager.LoadActivitiesFromXml(activities);
                if (manager.GetRegisteredActivity(match.ActivityId) == null)
                {
                    SP2ServerModPlugin.Log.LogError("Match '" + match.Id + "': the game did not accept the activity (see the errors just above)");
                    Statuses[match.Id] = "the game did not accept it (see the log)";
                    return;
                }
                if (!RegisteredIds.Contains(match.ActivityId)) RegisteredIds.Add(match.ActivityId);
                Statuses[match.Id] = "registered as " + match.ActivityId;
                SP2ServerModPlugin.Log.LogInfo("Match '" + match.Name + "' registered as activity " + match.ActivityId + " (" + match.Mode + ", " +
                    CountSpawns(match, 1) + " + " + CountSpawns(match, 2) + " spawns, " + match.TimerSeconds + " s)");
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("Match '" + match.Id + "' could not be registered: " + e);
                Statuses[match.Id] = "could not be registered (see the log)";
            }
        }

        // The game has no "remove activity"; its list is a private dictionary. Take our own entries out of it, so an edited match replaces the old one.
        private static IDictionary RegistryOf(NetworkedActivityManager manager)
        {
            FieldInfo field = typeof(NetworkedActivityManager).GetField("_registeredActivities", BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null ? field.GetValue(manager) as IDictionary : null;
        }

        private static void RemoveEntry(NetworkedActivityManager manager, string activityId)
        {
            try
            {
                IDictionary registry = RegistryOf(manager);
                if (registry == null)
                {
                    SP2ServerModPlugin.Log.LogWarning("Could not reach the game's activity list to take an old match out");
                    return;
                }
                registry.Remove(activityId);
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogWarning("Could not take an old match out of the game's activity list: " + e.Message);
            }
            RegisteredIds.Remove(activityId);
        }

        private static void UnregisterAll(NetworkedActivityManager manager)
        {
            if (RegisteredIds.Count == 0) return;
            foreach (string id in RegisteredIds.ToArray())
                RemoveEntry(manager, id);
            RegisteredIds.Clear();
        }

        // The <Activity> element the game reads (NetworkedActivityData.SerializeRead). The Data part is what DeathmatchActivityScript reads:
        // position/rotation of the activity object, timer, teamDeathmatch and <StartLocations teamId="Team1|Team2">. lives and killLimit are
        // ours (the game ignores attributes it does not know); phase F4 uses them.
        internal static XElement BuildActivityXml(MatchDefinition match)
        {
            TeamDefinition first = match.Team(1);
            Vector3 anchor = first != null && first.Spawns.Count > 0 ? first.Spawns[0].Position : Vector3.zero;

            XElement data = new XElement("Data",
                new XAttribute("position", MatchStore.Format(anchor, "0.##")),
                new XAttribute("rotation", "0,0,0"),
                new XAttribute("timer", match.TimerSeconds),
                new XAttribute("teamDeathmatch", match.Mode == MatchMode.TeamDeathmatch ? "true" : "false"));
            if (match.Lives > 0) data.Add(new XAttribute("lives", match.Lives));
            if (match.KillLimit > 0) data.Add(new XAttribute("killLimit", match.KillLimit));

            // The crafts of each team. They travel inside the activity, so every player with the mod knows them (TeamLoadout) and the server
            // can tell which crafts are the team's (ServerCraftCheck).
            for (int number = 1; number <= (match.Mode == MatchMode.TeamDeathmatch ? 2 : 1); number++)
            {
                TeamDefinition withCrafts = match.Team(number);
                if (withCrafts != null) CraftList.Write(data, number, withCrafts.Crafts);
            }

            foreach (TeamDefinition team in match.Teams)
            {
                if (team.Spawns.Count == 0) continue;
                if (match.Mode == MatchMode.FreeForAll && team.Number != 1) continue;
                XElement locations = new XElement("StartLocations", new XAttribute("teamId", team.GameId));
                foreach (SpawnPoint spawn in team.Spawns)
                {
                    Vector3 velocity = spawn.OnGround || spawn.Speed <= 0f
                        ? Vector3.zero
                        : Quaternion.Euler(spawn.Rotation) * Vector3.forward * spawn.Speed;
                    locations.Add(new XElement("Location",
                        new XAttribute("position", MatchStore.Format(spawn.Position, "0.##")),
                        new XAttribute("rotation", MatchStore.Format(spawn.Rotation, "0.#")),
                        new XAttribute("startOnGround", spawn.OnGround ? "true" : "false"),
                        new XAttribute("initialVelocity", MatchStore.Format(velocity, "0.##")),
                        new XAttribute("maxDistributionAmount", spawn.Spread.ToString("0.#", CultureInfo.InvariantCulture))));
                }
                data.Add(locations);
            }

            // The game refuses an activity whose name or description is empty.
            string name = string.IsNullOrWhiteSpace(match.Name) ? match.Id : match.Name;
            string description = string.IsNullOrWhiteSpace(match.Description) ? "A match made with the SP2 Server Mod." : match.Description;
            return new XElement("Activity",
                new XAttribute("id", match.ActivityId),
                new XAttribute("displayName", name),
                new XAttribute("description", description),
                new XAttribute("prefab", DeathmatchPrefab),
                new XAttribute("category", "Combat"),
                new XAttribute("icon", "IconCombat"),
                new XAttribute("allowPeacefulMode", "false"),
                new XElement("Settings"),
                data);
        }

        private static int CountSpawns(MatchDefinition match, int team)
        {
            TeamDefinition t = match.Team(team);
            return t == null ? 0 : t.Spawns.Count;
        }
    }
}
