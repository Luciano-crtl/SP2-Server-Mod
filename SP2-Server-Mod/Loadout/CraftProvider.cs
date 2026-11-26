using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Assets.Scripts;
using Assets.Scripts.Craft.CraftFiles;
using Assets.Scripts.Flight;

namespace SP2ServerMod
{
    // Where the crafts of a match come from, and the fingerprint that tells whether everybody has the same one.
    //
    // A match names a craft file for each team ("Tank-A.xml") and the hash of its contents. The file lives in the mod's 'crafts' folder
    // (BepInEx\config\sp2servermod\crafts): the organizer puts the file there with a button of the menu and gives the same folder to the players
    // (a zip, a download...). When a player does not have it there, the game's own craft list is searched for the same name. Either way the hash
    // must match, or the player would fly a different version than the others.
    //
    // The hash is the game's own one (the SHA-256 of the compressed craft XML that the game sends to the server).
    internal static class CraftProvider
    {
        public static string Folder
        {
            get { return Path.Combine(MatchStore.Folder, "crafts"); }
        }

        // The file name, with no folders and with .xml (a name from a file or the menu is never trusted to point somewhere else).
        public static string SafeName(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return "";
            string name = Path.GetFileName(file.Trim().Replace('/', '\\'));
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad.ToString(), "");
            if (name.Length == 0) return "";
            if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) name += ".xml";
            return name;
        }

        private const string ProbeId = "__sp2_probe__.xml";

        // The hash of a craft the way the game computes it when a craft is sent to the server: the craft is read from the game's craft list and
        // compressed. (Writing it to a hidden probe file first makes the game read it back the same way it reads the craft of any player, so
        // the hash is the same one the server gets from a player flying this file.)
        public static string HashOf(XElement craft)
        {
            Assets.Scripts.Game.Instance.CraftDatabase.SaveCraft(ProbeId, craft, false, false);
            return GameHash(ProbeId);
        }

        // The hash of a craft that is in the game's craft list (by its id there).
        public static string GameHash(string id)
        {
            CraftFileInfo info;
            if (!Assets.Scripts.Game.Instance.CraftDatabase.TryGetCraft(id, out info)) return null;
            XElement xml = info.LoadXml(false);
            return xml == null ? null : CraftDatabase.ComputeHash(CraftDatabase.CompressCraftXml(xml));
        }

        // The craft named by a team: the mod's folder first, then the game's own craft list. where says which one it was.
        public static bool TryLoad(string file, out XElement craft, out string where)
        {
            craft = null;
            where = null;
            string name = SafeName(file);
            if (name.Length == 0) return false;

            try
            {
                string path = Path.Combine(Folder, name);
                if (File.Exists(path))
                {
                    craft = XElement.Load(path);
                    where = "the mod's crafts folder";
                    return true;
                }

                CraftFileInfo info;
                if (Assets.Scripts.Game.Instance != null && Assets.Scripts.Game.Instance.CraftDatabase.TryGetCraft(name, out info))
                {
                    craft = info.LoadXml(false);
                    if (craft != null)
                    {
                        where = "your craft list";
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogWarning("Craft '" + name + "' could not be read: " + e.Message);
            }
            return false;
        }

        // What is wrong with a craft of a team, or null when it is fine. Used for the warnings of the menu.
        public static string Check(CraftChoice choice)
        {
            if (choice == null) return null;
            XElement craft;
            string where;
            if (!TryLoad(choice.File, out craft, out where))
            {
                // A craft from the website is downloaded when a player needs it.
                if (!string.IsNullOrEmpty(choice.Site)) return null;
                return "'" + choice.File + "' was not found (put it in " + Folder + ")";
            }
            if (!string.IsNullOrEmpty(choice.Hash) && HashOf(craft) != choice.Hash)
                return "'" + choice.File + "' in " + where + " is not the version the match was made with (its hash is different)";
            return null;
        }

        // Can this computer fly the craft: the file is here (the mod's folder or the player's own list), or it can be downloaded?
        public static bool Available(CraftChoice choice)
        {
            if (choice == null) return false;
            if (!string.IsNullOrEmpty(choice.Site)) return true;
            string name = SafeName(choice.File);
            if (name.Length == 0) return false;
            try
            {
                if (File.Exists(Path.Combine(Folder, name))) return true;
                CraftFileInfo info;
                return Assets.Scripts.Game.Instance != null && Assets.Scripts.Game.Instance.CraftDatabase.TryGetCraft(name, out info);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // The file name for the next craft of a team: <match>-team<N>-<k>.xml with the first k that nobody uses.
        public static string NextFileName(MatchDefinition match, int team)
        {
            TeamDefinition t = match.Team(team);
            for (int k = 1; k < 100; k++)
            {
                string name = SafeName(match.Id + "-team" + team + "-" + k);
                bool used = File.Exists(Path.Combine(Folder, name));
                if (t != null)
                    foreach (CraftChoice existing in t.Crafts)
                        if (string.Equals(existing.File, name, StringComparison.OrdinalIgnoreCase)) used = true;
                if (!used) return name;
            }
            return SafeName(match.Id + "-team" + team + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        }

        // The craft the player is flying now, saved to the mod's folder under a name made from the match and the team.
        // Returns its file name, hash (read back from the file, as the other players will read it) and its own name.
        public static bool SaveFlying(MatchDefinition match, int team, out CraftChoice result, out string error)
        {
            result = null;
            error = null;

            FlightScenePlayer player = FlightSceneScript.Instance != null ? FlightSceneScript.Instance.LocalPlayer : null;
            XElement craft = null;
            try
            {
                craft = player != null && player.Aircraft != null && player.Aircraft.NetworkAircraft != null
                    ? player.Aircraft.NetworkAircraft.GetCraftXml()
                    : null;
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogWarning("The craft you are flying could not be read: " + e.Message);
            }
            if (craft == null)
            {
                error = "You are not flying a craft";
                return false;
            }
            return Store(craft, NextFileName(match, team), out result, out error);
        }

        // A craft from the game's own list (by its file name, as the Change craft list shows it), copied to the mod's folder.
        public static bool CopyFromGame(string id, MatchDefinition match, int team, out CraftChoice result, out string error)
        {
            result = null;
            error = null;
            string name = SafeName(id);
            CraftFileInfo info;
            if (name.Length == 0 || !Assets.Scripts.Game.Instance.CraftDatabase.TryGetCraft(name, out info))
            {
                error = "No craft named '" + id + "' in your craft list";
                return false;
            }
            XElement craft = info.LoadXml(false);
            if (craft == null)
            {
                error = "The craft '" + name + "' could not be loaded";
                return false;
            }
            return Store(craft, NextFileName(match, team), out result, out error);
        }

        internal static bool Store(XElement craft, string name, out CraftChoice result, out string error)
        {
            result = null;
            error = null;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, name);
                craft.Save(path, SaveOptions.DisableFormatting);

                // The hash of what the file gives back, which is what everybody else will load.
                string hash = HashOf(XElement.Load(path));
                result = new CraftChoice { File = name, Hash = hash, Label = CraftList.CleanLabel((string)craft.Attribute("name") ?? "") };
                return true;
            }
            catch (Exception e)
            {
                error = "The craft could not be saved: " + e.Message;
                SP2ServerModPlugin.Log.LogError(error);
                return false;
            }
        }

        // The crafts in the mod's folder (file names), for the menu.
        public static string[] ListFolder()
        {
            try
            {
                if (!Directory.Exists(Folder)) return new string[0];
                return Directory.GetFiles(Folder, "*.xml").Select(Path.GetFileName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch
            {
                return new string[0];
            }
        }
    }
}
