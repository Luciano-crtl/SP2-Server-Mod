using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using BepInEx;
using UnityEngine;

namespace SP2ServerMod
{
    // Reads (and writes, for the sample) the .match.xml files in BepInEx/config/sp2servermod/.
    //
    //   <Match id="arena" name="Arena" description="..." mode="TDM" timer="300">
    //     <Team id="1"><Spawn position="12873,4.5,-29883" rotation="0,45,0" onGround="true" speed="0" spread="15" /></Team>
    //     <Team id="2">...</Team>
    //   </Match>
    //
    // mode: FFA (free for all, default) or TDM. position/rotation are "x,y,z" with a dot for decimals; the MarkSpawnKey writes them for you.
    internal static class MatchStore
    {
        public const string FileSuffix = ".match.xml";

        public static string Folder
        {
            get { return Path.Combine(Paths.ConfigPath, "sp2servermod"); }
        }

        public static List<MatchDefinition> LoadAll()
        {
            List<MatchDefinition> matches = new List<MatchDefinition>();
            try
            {
                Directory.CreateDirectory(Folder);
                string[] files = Directory.GetFiles(Folder, "*" + FileSuffix);
                if (files.Length == 0)
                {
                    WriteSample();
                    files = Directory.GetFiles(Folder, "*" + FileSuffix);
                }
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                HashSet<string> ids = new HashSet<string>();
                foreach (string file in files)
                {
                    MatchDefinition match = Load(file);
                    if (match == null) continue;
                    if (!ids.Add(match.Id))
                    {
                        SP2ServerModPlugin.Log.LogWarning("Match file '" + Path.GetFileName(file) + "' has the id '" + match.Id + "', already used by another file: skipped");
                        continue;
                    }
                    matches.Add(match);
                }
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("Could not read the matches folder: " + e);
            }
            return matches;
        }

        public static MatchDefinition Load(string file)
        {
            string name = Path.GetFileName(file);
            try
            {
                XElement root = XDocument.Load(file).Root;
                if (root == null || root.Name.LocalName != "Match")
                {
                    SP2ServerModPlugin.Log.LogWarning("Match file '" + name + "': the top element must be <Match>: skipped");
                    return null;
                }

                MatchDefinition match = new MatchDefinition();
                match.SourceFile = file;
                string fileId = name.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - FileSuffix.Length) : name;
                match.Id = Sanitize((string)root.Attribute("id") ?? fileId);
                match.Name = (string)root.Attribute("name") ?? match.Id;
                match.Description = (string)root.Attribute("description") ?? "A match made with the SP2 Server Mod.";
                match.Mode = ParseMode((string)root.Attribute("mode"));
                match.TimerSeconds = ParseInt((string)root.Attribute("timer"), 300);
                match.Lives = Math.Max(0, ParseInt((string)root.Attribute("lives"), 0));
                match.KillLimit = Math.Max(0, ParseInt((string)root.Attribute("killLimit"), 0));

                foreach (XElement teamElement in root.Elements("Team"))
                {
                    int number = ParseInt((string)teamElement.Attribute("id"), 1);
                    if (number != 1 && number != 2)
                    {
                        SP2ServerModPlugin.Log.LogWarning("Match '" + match.Id + "': team id " + number + " ignored (only 1 and 2 exist)");
                        continue;
                    }
                    TeamDefinition team = match.Team(number);
                    if (team == null)
                    {
                        team = new TeamDefinition { Number = number };
                        match.Teams.Add(team);
                    }
                    // The first version wrote ONE craft as attributes of <Team> (craft, craftHash, craftSite): still read, as the first of the list.
                    // Since then the crafts are <Craft file= hash= site= label=/> children, as many as the host wants.
                    string legacyFile = CraftProvider.SafeName((string)teamElement.Attribute("craft") ?? "");
                    if (legacyFile.Length > 0)
                        CraftList.Upsert(team.Crafts, new CraftChoice
                        {
                            File = legacyFile,
                            Hash = ((string)teamElement.Attribute("craftHash") ?? "").Trim(),
                            Site = SiteCrafts.ParseId((string)teamElement.Attribute("craftSite") ?? ""),
                        });
                    foreach (XElement craftElement in teamElement.Elements("Craft"))
                    {
                        CraftChoice craft = CraftList.From(craftElement);
                        if (craft != null) CraftList.Upsert(team.Crafts, craft);
                    }
                    foreach (XElement spawnElement in teamElement.Elements("Spawn"))
                    {
                        Vector3 position;
                        if (!TryParseVector((string)spawnElement.Attribute("position"), out position))
                        {
                            SP2ServerModPlugin.Log.LogWarning("Match '" + match.Id + "': a <Spawn> without a valid position was skipped");
                            continue;
                        }
                        Vector3 rotation;
                        if (!TryParseVector((string)spawnElement.Attribute("rotation"), out rotation)) rotation = Vector3.zero;
                        team.Spawns.Add(new SpawnPoint
                        {
                            Position = position,
                            Rotation = rotation,
                            OnGround = ParseBool((string)spawnElement.Attribute("onGround"), true),
                            Speed = ParseFloat((string)spawnElement.Attribute("speed"), 0f),
                            Spread = ParseFloat((string)spawnElement.Attribute("spread"), 15f)
                        });
                    }
                }
                return match;
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogWarning("Match file '" + name + "' could not be read (" + e.Message + "): skipped");
                return null;
            }
        }

        // ---- Writing ---------------------------------------------------------------

        public static XElement ToXml(MatchDefinition match)
        {
            XElement root = new XElement("Match",
                new XAttribute("id", match.Id),
                new XAttribute("name", match.Name ?? match.Id),
                new XAttribute("description", match.Description ?? string.Empty),
                new XAttribute("mode", match.Mode == MatchMode.TeamDeathmatch ? "TDM" : "FFA"),
                new XAttribute("timer", match.TimerSeconds));
            if (match.Lives > 0) root.Add(new XAttribute("lives", match.Lives));
            if (match.KillLimit > 0) root.Add(new XAttribute("killLimit", match.KillLimit));

            foreach (TeamDefinition team in match.Teams)
            {
                XElement teamElement = new XElement("Team", new XAttribute("id", team.Number));
                foreach (CraftChoice craft in team.Crafts)
                {
                    XElement craftElement = new XElement("Craft", new XAttribute("file", craft.File));
                    if (!string.IsNullOrEmpty(craft.Hash)) craftElement.Add(new XAttribute("hash", craft.Hash));
                    if (!string.IsNullOrEmpty(craft.Site)) craftElement.Add(new XAttribute("site", craft.Site));
                    if (!string.IsNullOrEmpty(craft.Label)) craftElement.Add(new XAttribute("label", craft.Label));
                    teamElement.Add(craftElement);
                }
                foreach (SpawnPoint spawn in team.Spawns)
                {
                    teamElement.Add(new XElement("Spawn",
                        new XAttribute("position", Format(spawn.Position, "0.##")),
                        new XAttribute("rotation", Format(spawn.Rotation, "0.#")),
                        new XAttribute("onGround", spawn.OnGround ? "true" : "false"),
                        new XAttribute("speed", spawn.Speed.ToString("0.#", CultureInfo.InvariantCulture)),
                        new XAttribute("spread", spawn.Spread.ToString("0.#", CultureInfo.InvariantCulture))));
                }
                root.Add(teamElement);
            }
            return root;
        }

        // Writes the match to its file (the one it was read from, or a new one named after its id). The file is written whole under another
        // name first and then put in place, so a failure half way never leaves a broken match file.
        public static bool Save(MatchDefinition match)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (string.IsNullOrEmpty(match.SourceFile)) match.SourceFile = Path.Combine(Folder, match.Id + FileSuffix);

                string temp = match.SourceFile + ".tmp";
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
                using (XmlWriter writer = XmlWriter.Create(temp, settings))
                    new XDocument(ToXml(match)).Save(writer);

                if (File.Exists(match.SourceFile)) File.Replace(temp, match.SourceFile, null);
                else File.Move(temp, match.SourceFile);
                return true;
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("Could not save match '" + match.Id + "': " + e.Message);
                return false;
            }
        }

        // Deleting moves the file into a 'deleted' folder next to the others (with the date in its name), so nothing is lost by a wrong click.
        public static bool Delete(MatchDefinition match)
        {
            try
            {
                if (string.IsNullOrEmpty(match.SourceFile) || !File.Exists(match.SourceFile)) return true;
                string folder = Path.Combine(Folder, "deleted");
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, Path.GetFileName(match.SourceFile) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                File.Move(match.SourceFile, target);
                SP2ServerModPlugin.Log.LogInfo("Match '" + match.Id + "' deleted; its file was kept as " + target);
                return true;
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("Could not delete match '" + match.Id + "': " + e.Message);
                return false;
            }
        }

        // An id that no loaded match and no file in the folder uses yet: the wanted one, or with a number after it.
        public static string FreeId(string wanted)
        {
            string baseId = Sanitize(wanted);
            string id = baseId;
            for (int n = 2; n < 1000; n++)
            {
                bool used = File.Exists(Path.Combine(Folder, id + FileSuffix));
                foreach (MatchDefinition match in MatchRegistry.Loaded)
                    if (string.Equals(match.Id, id, StringComparison.OrdinalIgnoreCase)) used = true;
                if (!used) return id;
                id = baseId + n.ToString(CultureInfo.InvariantCulture);
            }
            return baseId + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        // ---- Number formats (always a dot, never the computer's locale) -------------

        public static string Format(Vector3 v, string format)
        {
            return v.x.ToString(format, CultureInfo.InvariantCulture) + "," + v.y.ToString(format, CultureInfo.InvariantCulture) + "," + v.z.ToString(format, CultureInfo.InvariantCulture);
        }

        private static bool TryParseVector(string text, out Vector3 value)
        {
            value = Vector3.zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split(',');
            if (parts.Length != 3) return false;
            float x, y, z;
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
            if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
            value = new Vector3(x, y, z);
            return true;
        }

        private static MatchMode ParseMode(string text)
        {
            string mode = (text ?? string.Empty).Trim().ToLowerInvariant();
            return mode == "tdm" || mode == "team" || mode == "teamdeathmatch" ? MatchMode.TeamDeathmatch : MatchMode.FreeForAll;
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static float ParseFloat(string text, float fallback)
        {
            float value;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static bool ParseBool(string text, bool fallback)
        {
            bool value;
            return bool.TryParse(text, out value) ? value : fallback;
        }

        // The id goes into the activity's id: letters, digits, '-' and '_' only.
        private static string Sanitize(string id)
        {
            StringBuilder clean = new StringBuilder();
            foreach (char c in id ?? string.Empty)
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') clean.Append(c);
            return clean.Length == 0 ? "match" : clean.ToString();
        }

        // ---- The sample the first run writes ----------------------------------------

        private static void WriteSample()
        {
            string path = Path.Combine(Folder, "example" + FileSuffix);
            string text =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<!-- A match for the SP2 Server Mod. Edit it, or copy it to a new .match.xml file (the file name is the match's id unless id= is given).\n" +
                "     mode: FFA (every player for themselves) or TDM (two teams). timer: seconds.\n" +
                "     Spawns: in flight, press the MarkSpawnKey (Insert by default) where you want one: it writes the <Spawn> line for you (log, screen, clipboard).\n" +
                "     Keep spawns at least 10 m apart. Press the ReloadMatchesKey (End) after saving to see your changes without restarting the game.\n" +
                "     The positions below (one of the game's own deathmatch spawns for team 1, a point 200 m away for team 2) only show the format: replace them. -->\n" +
                "<Match id=\"example\" name=\"Example match (edit me)\" description=\"Shoot the other players. A starting point for your own match.\" mode=\"TDM\" timer=\"300\">\n" +
                "  <Team id=\"1\">\n" +
                "    <Spawn position=\"12873,4.55,-29883\" rotation=\"0,45,0\" onGround=\"true\" speed=\"0\" spread=\"15\" />\n" +
                "  </Team>\n" +
                "  <Team id=\"2\">\n" +
                "    <Spawn position=\"13073,4.55,-29683\" rotation=\"0,225,0\" onGround=\"true\" speed=\"0\" spread=\"15\" />\n" +
                "  </Team>\n" +
                "</Match>\n";
            File.WriteAllText(path, text, new UTF8Encoding(false));
            SP2ServerModPlugin.Log.LogInfo("Wrote a sample match: " + path);
        }
    }
}
