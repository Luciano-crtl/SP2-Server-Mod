using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Assets.Scripts.Craft;
using Assets.Scripts.Flight;
using Assets.Scripts.Flight.UI;
using Assets.Scripts.Multiplayer.ActivityFramework;
using Jundroo.Juicy.Widgets;
using UnityEngine;

namespace SP2ServerMod
{
    // The state of the match on the flight screen of a player who has the mod (phase F5): time left, lives, kills, who is still alive, the score
    // of the two teams. Pills on the left edge, made like the game's own (the same "btn btn-flight" button with a bar that fills, as the Armor
    // Mod's ammo bars are), as children of the game's "main-ui" so they hide together with the rest of the flight UI. Nothing here is sent
    // over the network: it is read from what the game already shares (the activity's scores, timer and data).
    internal static class MatchHud
    {
        private const string IdPrefix = "sp2sm-hud-";
        private const float LeftMargin = 15f;
        private const float FirstOffset = 90f;     // above the middle of the left edge, going down
        private const float Spacing = 40f;         // a bar is 32 high
        private const float PillWidth = 215f;
        private const float Interval = 0.25f;

        private sealed class Row
        {
            public string Id, Name, Value, Tooltip;
            public float Fill = -1f;               // 0..1, or -1 for a plain dark pill
            public Color Color;
        }

        private sealed class Pill
        {
            public Widget Root;
            public ImageWidget Fill;
            public TextWidget Name, Value;
            public string Id, ShownName, ShownValue;
            public float ShownFill = -2f;
            public Color ShownColor;
        }

        private static readonly List<Row> rows = new List<Row>();
        private static readonly List<Pill> pills = new List<Pill>();
        private static Widget parent;
        private static float nextCompute;
        private static float nextAttempt;
        private static float nextOrder;

        private static readonly Color Normal = new Color(0.89f, 0.62f, 0.05f, 1f);
        private static readonly Color Low = new Color(0.86f, 0.21f, 0.27f, 1f);       // the game's "danger" red
        private static readonly Color Team1 = new Color(0.157f, 0.349f, 0.62f, 1f);   // the game's primary blue
        private static readonly Color Team2 = new Color(0.86f, 0.21f, 0.27f, 1f);

        public static void Tick()
        {
            if (!SP2ServerModPlugin.HudEnabled.Value)
            {
                if (pills.Count > 0) Clear();
                return;
            }

            try
            {
                FlightSceneScript scene = FlightSceneScript.Instance;
                if (scene == null || scene.LocalPlayer == null)
                {
                    if (pills.Count > 0) Clear();
                    return;
                }

                if (Time.unscaledTime >= nextCompute)
                {
                    nextCompute = Time.unscaledTime + Interval;
                    Compute(scene.LocalPlayer);
                }
                if (rows.Count == 0)
                {
                    if (pills.Count > 0) Clear();
                    return;
                }

                if (NeedsRebuild())
                {
                    if (Time.unscaledTime < nextAttempt) return;   // looking for the flight screen costs a little
                    nextAttempt = Time.unscaledTime + 0.5f;
                    Rebuild(scene);
                }
                Refresh();
                KeepBehind();
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("The match display failed and is switched off: " + e);
                SP2ServerModPlugin.HudEnabled.Value = false;
                Clear();
            }
        }

        // ---- What to show ---------------------------------------------------------------

        private static void Compute(FlightScenePlayer player)
        {
            rows.Clear();
            NetworkedActivityScript activity = player.NetworkedActivity;
            if (!MatchRules.IsOurs(activity)) return;
            if (activity.State != NetworkedActivityState.Starting && activity.State != NetworkedActivityState.Started && activity.State != NetworkedActivityState.Ending)
                return;
            NetworkedActivityPlayer me = activity.GetPlayer(player);
            if (me == null) return;

            int lives = MatchRules.Lives(activity);
            int killLimit = MatchRules.KillLimit(activity);
            bool teams = MatchRules.IsTeamMatch(activity);

            // Time left.
            int? timer = activity.TimerValue;
            int total = 0;
            XElement data = activity.Data != null ? activity.Data.XmlData : null;
            if (data != null) int.TryParse((string)data.Attribute("timer"), out total);
            if (timer.HasValue)
                rows.Add(new Row
                {
                    Id = "time",
                    Name = "Time",
                    Value = Clock(timer.Value),
                    Fill = total > 0 ? Mathf.Clamp01((float)timer.Value / total) : -1f,
                    Color = timer.Value <= 30 ? Low : Normal,
                });

            // My lives.
            if (lives > 0)
            {
                int left = Mathf.Max(0, lives - MatchRules.Deaths(me));
                rows.Add(new Row
                {
                    Id = "lives",
                    Name = "Lives",
                    Value = left == 0 ? "OUT" : left + "/" + lives,
                    Fill = (float)left / lives,
                    Color = left <= 1 ? Low : Normal,
                });
            }

            // My kills (against the limit, when there is one).
            int kills = MatchRules.Kills(me);
            rows.Add(new Row
            {
                Id = "kills",
                Name = "Kills",
                Value = killLimit > 0 ? kills + "/" + killLimit : kills.ToString(CultureInfo.InvariantCulture),
                Fill = killLimit > 0 ? Mathf.Clamp01((float)kills / killLimit) : -1f,
                Color = Normal,
            });

            // Who is still alive, and the score of the teams.
            int alive1 = 0, alive2 = 0, total1 = 0, total2 = 0, kills1 = 0, kills2 = 0, aliveAll = 0, all = 0;
            foreach (NetworkedActivityPlayer other in activity.Players)
            {
                if (other.State != NetworkedActivityPlayerState.Playing && other.State != NetworkedActivityPlayerState.Starting) continue;
                bool out_ = MatchRules.IsEliminated(activity, other);
                all++;
                if (!out_) aliveAll++;
                if (other.Team == null) continue;
                if (other.Team.Id == NetworkedActivityTeamIds.Team1) { total1++; kills1 += MatchRules.Kills(other); if (!out_) alive1++; }
                else if (other.Team.Id == NetworkedActivityTeamIds.Team2) { total2++; kills2 += MatchRules.Kills(other); if (!out_) alive2++; }
            }

            if (lives > 0)
            {
                rows.Add(new Row
                {
                    Id = "alive",
                    Name = "Alive",
                    Value = teams ? alive1 + " vs " + alive2 : aliveAll + "/" + all,
                    Fill = teams ? -1f : (all > 0 ? (float)aliveAll / all : 0f),
                    Color = Normal,
                });
            }
            // The craft the player flies, when the team has several: a click picks the next one (it is the one the next spawn loads).
            IList<CraftChoice> options = TeamLoadout.Options;
            if (options != null && TeamLoadout.OptionIndex < options.Count)
                rows.Add(new Row
                {
                    Id = "craft",
                    Name = "Craft",
                    Value = Short(options[TeamLoadout.OptionIndex].Title, 16),
                    Fill = -1f,
                    Color = Normal,
                    Tooltip = "Click to pick the next craft of your team. You get it when you spawn again.",
                });

            if (teams)
            {
                bool mine1 = me.Team != null && me.Team.Id == NetworkedActivityTeamIds.Team1;
                rows.Add(new Row
                {
                    Id = "teams",
                    Name = "Teams",
                    Value = kills1 + " - " + kills2,
                    Fill = killLimit > 0 ? Mathf.Clamp01((float)Mathf.Max(kills1, kills2) / killLimit) : -1f,
                    Color = mine1 ? Team1 : Team2,
                });
            }
        }

        private static string Short(string text, int length)
        {
            return text.Length <= length ? text : text.Substring(0, length - 1) + "~";
        }

        private static void OnCraftClicked(Widget widget)
        {
            TeamLoadout.SelectNext();
        }

        private static string Clock(int seconds)
        {
            seconds = Mathf.Max(0, seconds);
            return (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        // ---- Widgets ------------------------------------------------------------------

        private static bool NeedsRebuild()
        {
            if (parent == null || pills.Count != rows.Count) return true;
            for (int i = 0; i < pills.Count; i++)
                if (pills[i].Root == null || pills[i].Id != rows[i].Id) return true;
            return false;
        }

        private static void Rebuild(FlightSceneScript scene)
        {
            Clear();
            FlightUIScript ui = scene.FlightUI;
            Widget root = ui != null ? ui.RootWidget : null;
            Widget mainUi = root != null ? root.FindWidget("main-ui") : null;
            if (mainUi == null) return;

            RemoveLeftovers(mainUi);
            for (int i = 0; i < rows.Count; i++)
            {
                Widget widget = mainUi.Context.CreateWidget(PillXml(i, rows[i]), mainUi, mainUi.Stylesheet);
                if (widget == null) continue;
                Pill pill = new Pill
                {
                    Root = widget,
                    Id = rows[i].Id,
                    Fill = widget.FindWidget<ImageWidget>(IdPrefix + "fill-" + i),
                    Name = widget.FindWidget<TextWidget>(IdPrefix + "name-" + i),
                    Value = widget.FindWidget<TextWidget>(IdPrefix + "value-" + i),
                };
                if (rows[i].Id == "craft") widget.Clicked += OnCraftClicked;
                if (widget.transform != null) widget.transform.SetAsFirstSibling();   // under the game's other menus
                pills.Add(pill);
            }
            parent = mainUi;
            SP2ServerModPlugin.Log.LogInfo("Match display: " + pills.Count + " bar(s) on the left edge");
        }

        // A bar like the game's own (the throttle and fuel pills): the rounded button, the bar that fills, and two texts.
        private static XElement PillXml(int index, Row row)
        {
            string xml =
                "<Button id='" + IdPrefix + index + "' class='btn btn-flight' anchor='MiddleLeft' position='" +
                LeftMargin.ToString("F0", CultureInfo.InvariantCulture) + " " +
                (FirstOffset - index * Spacing).ToString("F0", CultureInfo.InvariantCulture) + "' width='" +
                PillWidth.ToString("F0", CultureInfo.InvariantCulture) + "' mask='EnabledAndShown' tooltipPosition='Right'" +
                (string.IsNullOrEmpty(row.Tooltip) ? "" : " tooltip='" + row.Tooltip + "'") + ">" +
                "<Image id='" + IdPrefix + "fill-" + index + "' sprite='@SpriteWhite' color='#E39E0D' type='Filled' fillMethod='Horizontal' fillAmount='0' />" +
                "<Text id='" + IdPrefix + "name-" + index + "' fontSize='18' alignment='Left' color='White' margin-left='12' />" +
                "<Text id='" + IdPrefix + "value-" + index + "' alignment='Right' color='White' />" +
                "</Button>";
            return XElement.Parse(xml);
        }

        // Only touches a widget when what it shows changed.
        private static void Refresh()
        {
            for (int i = 0; i < pills.Count && i < rows.Count; i++)
            {
                Pill pill = pills[i];
                Row row = rows[i];

                if (pill.ShownName != row.Name)
                {
                    pill.ShownName = row.Name;
                    if (pill.Name != null) pill.Name.Text = row.Name;
                }
                if (pill.ShownValue != row.Value)
                {
                    pill.ShownValue = row.Value;
                    if (pill.Value != null) pill.Value.Text = row.Value;
                }
                if (pill.ShownFill != row.Fill || pill.ShownColor != row.Color)
                {
                    pill.ShownFill = row.Fill;
                    pill.ShownColor = row.Color;
                    if (pill.Fill != null)
                    {
                        pill.Fill.Image.fillAmount = row.Fill < 0f ? 0f : row.Fill;
                        pill.Fill.Color.Base = row.Color;
                        pill.Fill.Image.color = row.Color;
                    }
                }
            }
        }

        // Drawn under the game's other menus: a widget made later sits above the earlier ones among its siblings, so each bar is kept first.
        private static void KeepBehind()
        {
            if (Time.unscaledTime < nextOrder) return;
            nextOrder = Time.unscaledTime + 0.5f;
            foreach (Pill pill in pills)
            {
                Transform t = pill.Root != null ? pill.Root.transform : null;
                if (t != null && t.GetSiblingIndex() != 0) t.SetAsFirstSibling();
            }
        }

        // Bars that an earlier copy of the plugin (before a reload) left in the flight screen.
        private static void RemoveLeftovers(Widget mainUi)
        {
            for (int guard = 0; guard < 64; guard++)
            {
                Widget old = null;
                foreach (Widget child in mainUi.Widgets)
                {
                    if (child != null && child.Id != null && child.Id.StartsWith(IdPrefix, StringComparison.Ordinal)) { old = child; break; }
                }
                if (old == null) return;
                old.Destroy();
            }
        }

        private static void Clear()
        {
            foreach (Pill pill in pills)
            {
                try
                {
                    if (pill.Root != null) pill.Root.Destroy();
                }
                catch (Exception)
                {
                    // the widget went away with the flight screen
                }
            }
            pills.Clear();
            parent = null;
        }

        public static void Shutdown()
        {
            Clear();
            rows.Clear();
        }
    }
}
