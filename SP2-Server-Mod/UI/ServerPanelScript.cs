using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Assets.Scripts.Flight;
using Assets.Scripts.Flight.UI;
using Assets.Scripts.UI;
using Assets.Scripts.UI.Controls;
using Jundroo.Juicy.Widgets;
using UnityEngine;

namespace SP2ServerMod.UI
{
    // The mod's menu window. The game creates it from the layout in ServerPanel.BuildLayout() and calls OnWidgetInitialized once its widgets exist.
    // It shows where the player is, lists every loaded match and edits them: the match's name, mode and time, and its spawns (add one where the
    // player is, move, delete, go to, a vertical beam to find it). Every change is saved to the match's file at once (MatchEditing.Commit).
    public class ServerPanelScript : PanelDialogScript
    {
        private const float RefreshInterval = 0.5f;
        private const int ManySpawns = 4;   // a team with more spawns than this starts with its spawn sections folded

        private Widget content;
        private PanelBuilder builder;

        // Small functions that refresh the live rows (the player's position, the nearest spawn).
        private readonly List<Action> refreshers = new List<Action>();

        private float nextRefresh;
        private bool rebuildRequested;
        private bool visible = true;
        private int builtVersion = -1;       // the MatchRegistry.Version the rows were made from
        private string builtSignature = "";  // the statuses and warnings the rows show

        // Doesn't block the game: you keep flying while it is open.
        public override bool IsModal
        {
            get { return false; }
        }

        public override void OnWidgetInitialized(Widget widget)
        {
            base.OnWidgetInitialized(widget);

            widget.FindWidget("sp2sm-close-button").Clicked += (Widget w) => Close();
            content = widget.FindWidget("sp2sm-content");

            BuildContent();
        }

        // (Re)creates every row.
        private void BuildContent()
        {
            foreach (Widget child in content.Widgets.ToList())
                child.Destroy();
            refreshers.Clear();

            builder = new PanelBuilder(content);
            builtVersion = MatchRegistry.Version;
            builtSignature = Signature();

            try
            {
                BuildStatus();
                BuildBeamSettings();
                BuildDisplaySettings();
                BuildServerRules();
                BuildMatches();
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("Couldn't build the SP2 Server Mod menu: " + e);
            }
        }

        // ---- Rows ----------------------------------------------------------------

        private void BuildStatus()
        {
            builder.Header("SP2 Server Mod");
            builder.Text("Mod version", SP2ServerModPlugin.Version);
            Live("You are at", PlayerPositionText);
            Live("Nearest spawn", NearestSpawnText);
            Live("Team craft", () => TeamLoadout.Status ?? "");
            Live("In the match", MatchRules.LocalSummary);
            BuildPick();
            Live("Players with the mod", ModdedPlayersText);
            builder.Buttons(new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("New match", () => MatchEditing.CreateNew()),
                new KeyValuePair<string, Action>("Reload matches", MatchRegistry.RequestReload),
                new KeyValuePair<string, Action>("Copy spawn line here", SpawnMarker.Mark),
            });
            builder.Text("Matches folder", @"BepInEx\config\sp2servermod");
        }

        private void BuildBeamSettings()
        {
            builder.Header("Spawn beams");
            builder.Toggle("Beam at every spawn", "Switches a vertical beam on or off at every spawn of every match. Blue: team 1, red: team 2, orange: free for all.",
                SpawnBeams.AllOn, on =>
                {
                    SpawnBeams.SetAll(on);
                    rebuildRequested = true;   // the switches of the spawns below must follow
                });
            builder.Slider("Beam height", "How high the beams reach.", 100f, 3000f, 59,
                SP2ServerModPlugin.BeamHeight.Value, "m",
                x => x.ToString("0", CultureInfo.InvariantCulture),
                SetBeamHeight,
                x => SP2ServerModPlugin.Instance.Config.Save());
        }

        private void BuildDisplaySettings()
        {
            builder.Header("Match display (only you)");
            builder.Toggle("Match bars", "Time left, lives, kills, who is alive and the score of the teams, in bars on the left of the flight screen during a match.",
                SP2ServerModPlugin.HudEnabled.Value, on => SP2ServerModPlugin.HudEnabled.Value = on);
            builder.Toggle("Spawn markers", "Before a match starts, marks the spawns of your team in the world, with the distance.",
                SP2ServerModPlugin.SpawnMarkersEnabled.Value, on => SP2ServerModPlugin.SpawnMarkersEnabled.Value = on);
            builder.Toggle("Spectator camera", "When you are out of lives in a match, the camera follows the players who are still alive. Next and previous player: the keys in the settings file (Spectator section; . and , by default).",
                SP2ServerModPlugin.SpectatorEnabled.Value, on => SP2ServerModPlugin.SpectatorEnabled.Value = on);
            Live("Spectator", () => Spectator.Active ? (Spectator.Hint ?? "on") : "off");
        }

        // The rules the server of the match applies (this computer, when it hosts).
        private void BuildServerRules()
        {
            builder.Header("Server rules (when you host)");
            builder.Toggle("Referee", "Ends a match when the kill limit is reached or only one player (or team) still has lives.",
                SP2ServerModPlugin.Referee.Value, on => SP2ServerModPlugin.Referee.Value = on);
            builder.Toggle("No spawn without lives", "A player who is out of lives cannot spawn again, and a match with lives cannot be joined once it is running.",
                SP2ServerModPlugin.EnforceRespawn.Value, on => SP2ServerModPlugin.EnforceRespawn.Value = on);
            builder.Toggle("Announce on screen", "Says the start, who was shot down (with lives: how many are left) and who is out on everybody's screen, with the game's own messages, so players without the mod see them too.",
                SP2ServerModPlugin.Announce.Value, on => SP2ServerModPlugin.Announce.Value = on);
            builder.Toggle("Craft link in the chat", "When a player joins a team whose craft is on the website, writes the link in their chat (clickable). For players without the mod.",
                SP2ServerModPlugin.CraftLink.Value, on => SP2ServerModPlugin.CraftLink.Value = on);
            builder.Text("Craft check", SP2ServerModPlugin.CraftCheck.Value.ToString());
            builder.Buttons(new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Craft check: off", () => SetCraftCheck(CraftCheckMode.Off)),
                new KeyValuePair<string, Action>("log only", () => SetCraftCheck(CraftCheckMode.Log)),
                new KeyValuePair<string, Action>("enforce", () => SetCraftCheck(CraftCheckMode.Enforce)),
            });
            builder.Note("", "Craft check compares each player's craft with the team's craft when the match starts. Use 'log only' first and look for 'craft check' lines in the log: if the hashes match, switch to enforce.", 44);
        }

        private void SetCraftCheck(CraftCheckMode mode)
        {
            SP2ServerModPlugin.CraftCheck.Value = mode;
            rebuildRequested = true;
        }

        private void BuildMatches()
        {
            IList<MatchDefinition> matches = MatchRegistry.Loaded;
            if (matches.Count == 0)
            {
                builder.Header("Matches");
                builder.Note("", "No match was found. Press New match, or put .match.xml files in the matches folder and press Reload matches.", 44);
                return;
            }

            foreach (MatchDefinition match in matches.ToList())
                BuildMatch(match);
        }

        private void BuildMatch(MatchDefinition match)
        {
            string status = MatchRegistry.StatusOf(match.Id);
            bool inGame = status.StartsWith("registered", StringComparison.Ordinal);
            builder.Header(match.Name + (match.Mode == MatchMode.TeamDeathmatch ? "  (TDM, " : "  (FFA, ") + match.TimerSeconds + " s)  [" + match.Id + "]" +
                           (inGame ? "" : "  - NOT IN THE ACTIVITY LIST"));
            builder.Note(inGame ? "Status" : "NOT LISTED", status, 44);
            foreach (string warning in MatchRegistry.WarningsOf(match.Id))
                builder.Note("Warning", warning, 44);

            builder.TextInput("Name", "The name in the game's activity list.", match.Name, 40, text =>
            {
                match.Name = text.Trim().Length == 0 ? match.Id : text.Trim();
                MatchEditing.Commit(match);
                rebuildRequested = true;   // the section's title shows the name
            });
            builder.TextInput("Description", "The description in the game's activity list.", match.Description, 140, text =>
            {
                match.Description = text.Trim();
                MatchEditing.Commit(match);
            });
            builder.Toggle("Two teams (TDM)", "Off: free for all, every player starts at team 1's spawns. On: two teams, each at its own spawns.",
                match.Mode == MatchMode.TeamDeathmatch, on =>
                {
                    MatchEditing.SetTeams(match, on);
                    rebuildRequested = true;   // the team 2 section appears or goes
                });
            builder.Slider("Time limit", "How long the match lasts, in seconds.", 30f, 3600f, 358,
                match.TimerSeconds, "s",
                x => x.ToString("0", CultureInfo.InvariantCulture),
                x => match.TimerSeconds = Mathf.Clamp(Mathf.RoundToInt(x / 10f) * 10, 30, 3600),
                x => MatchEditing.Commit(match));
            builder.Slider("Lives", "0 = no limit. Each death costs a life; at 0 the player cannot come back, and the match ends when only one player (or team) has lives. The server of the match enforces it.", 0f, 10f, 10,
                match.Lives, "",
                x => Mathf.RoundToInt(x).ToString(CultureInfo.InvariantCulture),
                x => match.Lives = Mathf.Clamp(Mathf.RoundToInt(x), 0, 10),
                x => MatchEditing.Commit(match));
            builder.Slider("Kill limit", "0 = no limit. The match ends when a player (or, with two teams, a team) gets this many kills. The server of the match enforces it.", 0f, 100f, 20,
                match.KillLimit, "",
                x => Mathf.RoundToInt(x).ToString(CultureInfo.InvariantCulture),
                x => match.KillLimit = Mathf.Clamp(Mathf.RoundToInt(x / 5f) * 5, 0, 100),
                x => MatchEditing.Commit(match));
            builder.Buttons(new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Duplicate", () => MatchEditing.Duplicate(match)),
                new KeyValuePair<string, Action>("Delete (keeps a copy)", () => MatchEditing.DeleteMatch(match)),
            });

            for (int number = 1; number <= 2; number++)
            {
                TeamDefinition team = match.Team(number);
                int count = team == null ? 0 : team.Spawns.Count;
                // FFA uses team 1's spawns only: team 2 shows only if it has some, to say they are not used.
                if (number == 2 && match.Mode == MatchMode.FreeForAll && count == 0) continue;
                BuildTeam(match, number, team, count);
            }
        }

        private void BuildTeam(MatchDefinition match, int number, TeamDefinition team, int count)
        {
            string unused = match.Mode == MatchMode.FreeForAll && number != 1 ? ", not used in FFA" : "";
            builder.Header(match.Id + ": team " + number + " (" + count + " spawn" + (count == 1 ? "" : "s") + unused + ")");
            builder.Buttons(new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Add spawn here", () =>
                {
                    MatchEditing.AddSpawnHere(match, number);
                    rebuildRequested = true;
                }),
            });

            BuildCrafts(match, number, team);

            for (int i = 0; team != null && i < team.Spawns.Count; i++)
                BuildSpawn(match, team, i);
        }

        // The crafts of a team: the host adds as many as he wants (up to CraftList.Max) and each player with the mod picks one of them.
        private void BuildCrafts(MatchDefinition match, int number, TeamDefinition team)
        {
            int count = team == null ? 0 : team.Crafts.Count;
            builder.Text("Team crafts", count == 0 ? "none: players fly their own"
                                                   : count + (count == 1 ? " craft" : " crafts: each player picks one"));
            for (int i = 0; i < count; i++)
            {
                CraftChoice craft = team.Crafts[i];
                int at = i;
                builder.Text("Craft " + (i + 1), craft.Title + (craft.Site.Length > 0 ? "  (website)" : "  (" + craft.File + ")"));
                builder.Buttons(new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("Remove craft " + (i + 1), () =>
                    {
                        MatchEditing.RemoveCraft(match, number, at);
                        rebuildRequested = true;
                    }),
                });
            }
            builder.Buttons(new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Add the craft I'm flying", () =>
                {
                    MatchEditing.AddCraftFromFlying(match, number);
                    rebuildRequested = true;
                }),
                new KeyValuePair<string, Action>("Remove all crafts", () =>
                {
                    MatchEditing.ClearCrafts(match, number);
                    rebuildRequested = true;
                }),
            });
            builder.TextInput("Add craft from the website", "The link of a craft published on the SimplePlanes website (or just its id). It is downloaded now; players that do not have it download it when they enter the match.", "", 200, text =>
            {
                if (text.Trim().Length == 0) return;
                MatchEditing.AddCraftFromSite(match, number, text);
            });
            builder.TextInput("Add craft from my list", "The file name of a craft in your own craft list (as the Change craft list shows it). It is copied to the crafts folder.", "", 80, text =>
            {
                if (text.Trim().Length == 0) return;
                MatchEditing.AddCraftFromGame(match, number, text);
                rebuildRequested = true;
            });
        }

        // The player's own pick among the crafts of their team (only with two or more): shown at the top of the window.
        private void BuildPick()
        {
            IList<CraftChoice> options = TeamLoadout.Options;
            if (options == null) return;

            builder.Note("Your team's crafts", "Pick the one you fly. Before the match starts it is swapped at once; once it runs you get it the next time you spawn.", 44);
            for (int start = 0; start < options.Count; start += 2)
            {
                List<KeyValuePair<string, Action>> row = new List<KeyValuePair<string, Action>>();
                for (int i = start; i < options.Count && i < start + 2; i++)
                {
                    int at = i;
                    CraftChoice craft = options[i];
                    row.Add(new KeyValuePair<string, Action>(craft.Title + (CraftProvider.Available(craft) ? "" : " (missing)"), () =>
                    {
                        TeamLoadout.Select(at);
                        rebuildRequested = true;
                    }));
                }
                // The picked one is in blue: only when it is in this row.
                int picked = TeamLoadout.OptionIndex >= start && TeamLoadout.OptionIndex < start + 2 ? TeamLoadout.OptionIndex - start : -1;
                builder.Buttons(row, picked);
            }
        }

        // One spawn: a section of its own (the title has its position, so it is visible folded), with everything to see and change it.
        private void BuildSpawn(MatchDefinition match, TeamDefinition team, int index)
        {
            SpawnPoint spawn = team.Spawns[index];
            string key = SpawnBeams.KeyOf(match, team, index);

            builder.Header(match.Id + " T" + team.Number + " #" + (index + 1) + "  " + Vec(spawn.Position), team.Spawns.Count > ManySpawns);
            builder.Text("Start", SpawnText(spawn));
            builder.Toggle("Vertical beam", "Shows a beam at this spawn (it reaches the height set in Spawn beams).", SpawnBeams.IsOn(key),
                on => SpawnBeams.Set(key, on));
            builder.Toggle("Starts on the ground", "Off: the craft starts in the air, flying at the speed below.", spawn.OnGround, on =>
            {
                spawn.OnGround = on;
                if (on) spawn.Speed = 0f;
                MatchEditing.Commit(match);
                rebuildRequested = true;   // the air speed row appears or goes
            });
            if (!spawn.OnGround)
            {
                builder.Slider("Air speed", "How fast the craft is going when it starts in the air.", 0f, 400f, 80,
                    spawn.Speed, "m/s",
                    x => x.ToString("0", CultureInfo.InvariantCulture),
                    x => spawn.Speed = Mathf.Clamp(Mathf.Round(x / 5f) * 5f, 0f, 400f),
                    x => MatchEditing.Commit(match));
            }
            builder.Slider("Spread", "Crafts that start at the same time are placed side by side along this width (meters).", 0f, 100f, 100,
                spawn.Spread, "m",
                x => x.ToString("0", CultureInfo.InvariantCulture),
                x => spawn.Spread = Mathf.Clamp(Mathf.Round(x), 0f, 100f),
                x => MatchEditing.Commit(match));
            builder.Buttons(new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Go to spawn", () => MatchEditing.GoTo(spawn)),
                new KeyValuePair<string, Action>("Move here", () =>
                {
                    MatchEditing.MoveSpawnHere(match, team, index);
                    rebuildRequested = true;
                }),
                new KeyValuePair<string, Action>("Delete spawn", () =>
                {
                    MatchEditing.DeleteSpawn(match, team, index);
                    rebuildRequested = true;
                }),
            });
        }

        // While the slider is dragged the setting changes on every step; the file is written when it is released.
        private static void SetBeamHeight(float x)
        {
            BepInEx.Configuration.ConfigFile file = SP2ServerModPlugin.Instance.Config;
            bool original = file.SaveOnConfigSet;
            file.SaveOnConfigSet = false;
            try
            {
                SP2ServerModPlugin.BeamHeight.Value = Mathf.Clamp(Mathf.Round(x / 50f) * 50f, 100f, 3000f);
            }
            finally
            {
                file.SaveOnConfigSet = original;
            }
        }

        // A row whose value is refreshed every half second.
        private void Live(string label, Func<string> getText)
        {
            TextControl row = builder.Text(label, getText());
            refreshers.Add(() =>
            {
                string text = getText();
                if (row.ValueText.Text != text) row.ValueText.Text = text;
            });
        }

        // What the status and warning rows show, so the rows are rebuilt when an edit changes them.
        private static string Signature()
        {
            StringBuilder text = new StringBuilder();
            text.Append(TeamLoadout.ChoiceSignature).Append('\n');
            foreach (MatchDefinition match in MatchRegistry.Loaded)
            {
                text.Append(match.Id).Append('|').Append(MatchRegistry.StatusOf(match.Id));
                foreach (string warning in MatchRegistry.WarningsOf(match.Id)) text.Append('|').Append(warning);
                text.Append('\n');
            }
            return text.ToString();
        }

        // ---- Texts ---------------------------------------------------------------

        private static string SpawnText(SpawnPoint spawn)
        {
            return Vec(spawn.Position) + " | hdg " + spawn.Rotation.y.ToString("0", CultureInfo.InvariantCulture) + " | " +
                   (spawn.OnGround ? "ground" : "air " + spawn.Speed.ToString("0", CultureInfo.InvariantCulture) + " m/s");
        }

        private static string Vec(Vector3 v)
        {
            // The same digits the .match.xml file has.
            return v.x.ToString("0.##", CultureInfo.InvariantCulture) + ", " + v.y.ToString("0.##", CultureInfo.InvariantCulture) + ", " +
                   v.z.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Distance(float meters)
        {
            return meters < 1000f ? meters.ToString("0", CultureInfo.InvariantCulture) + " m" : (meters / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        private static FlightScenePlayer LocalPlayer()
        {
            FlightSceneScript scene = FlightSceneScript.Instance;
            return scene != null ? scene.LocalPlayer : null;
        }

        // Who has said hello (the host's view). A client sees only that the hosts' mod answers nothing: the count is of the players it heard from.
        private static string ModdedPlayersText()
        {
            FlightSceneScript scene = FlightSceneScript.Instance;
            if (scene == null || scene.LocalPlayer == null) return "not in a flight";
            int total = scene.AllPlayers.Count();
            if (total < 2) return "you are the only player";
            int modded = 0;
            foreach (FlightScenePlayer player in scene.AllPlayers)
                if (player.NetworkPlayer != null && ModHello.IsModded(player.NetworkPlayer.PlayerId)) modded++;
            return modded + " of " + total + " (counting you)";
        }

        private static string PlayerPositionText()
        {
            FlightScenePlayer player = LocalPlayer();
            if (player == null) return "not in a flight";
            return Vec(player.GlobalPosition) + " | hdg " + player.Rotation.y.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string NearestSpawnText()
        {
            FlightScenePlayer player = LocalPlayer();
            if (player == null) return "not in a flight";

            Vector3 here = player.GlobalPosition;
            float best = float.MaxValue;
            string label = null;
            foreach (MatchDefinition match in MatchRegistry.Loaded)
                foreach (TeamDefinition team in match.Teams)
                    for (int i = 0; i < team.Spawns.Count; i++)
                    {
                        float distance = Vector3.Distance(here, team.Spawns[i].Position);
                        if (distance >= best) continue;
                        best = distance;
                        label = match.Name + ", team " + team.Number + " #" + (i + 1);
                    }
            return label == null ? "no spawns" : Distance(best) + " (" + label + ")";
        }

        // ---- Per frame -----------------------------------------------------------

        protected void Update()
        {
            UpdateVisibility();

            // The list of matches changed (read again, one added or deleted): show the new rows.
            if (MatchRegistry.Version != builtVersion) rebuildRequested = true;

            if (rebuildRequested)
            {
                rebuildRequested = false;
                BuildContent();
                return;
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshInterval;

                // An edit changed what the status or warning rows say: show them.
                if (Signature() != builtSignature)
                {
                    BuildContent();
                    return;
                }

                foreach (Action refresh in refreshers)
                {
                    try
                    {
                        refresh();
                    }
                    catch (Exception e)
                    {
                        SP2ServerModPlugin.Log.LogWarning("Menu refresh failed: " + e.Message);
                    }
                }
            }
        }

        // Hide the window together with the rest of the flight UI (for clean screenshots).
        private void UpdateVisibility()
        {
            FlightUIScript flightUI = FlightSceneScript.Instance != null ? FlightSceneScript.Instance.FlightUI : null;
            bool show = flightUI == null || flightUI.Visible;
            if (show == visible) return;
            visible = show;

            CanvasGroup group = Widget.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = Widget.gameObject.AddComponent<CanvasGroup>();
            group.alpha = show ? 1f : 0f;
            group.interactable = show;
            group.blocksRaycasts = show;
        }
    }
}
