using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace SP2ServerMod
{
    // One craft a team may fly. A team has a list of them (the host adds as many as he wants, up to CraftList.Max); every player with the mod
    // picks which one to fly, and the server accepts any of them.
    internal sealed class CraftChoice
    {
        public string File = "";    // the file in the mod's crafts folder (or the player's own craft list)
        public string Hash = "";    // the game's hash of its contents: everybody must have exactly this version
        public string Site = "";    // the id of the craft on the SimplePlanes website, when it came from there: players without the file download it
        public string Label = "";   // the craft's own name, for buttons and messages

        public CraftChoice Clone()
        {
            return (CraftChoice)MemberwiseClone();
        }

        // What to call it on screen: the craft's name, or its file name.
        public string Title
        {
            get
            {
                if (!string.IsNullOrEmpty(Label)) return Label;
                string name = File ?? "";
                return name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
            }
        }
    }

    // The crafts of a team: reading, writing, and the rule the server uses. Nothing from the game in here (it is tested outside it).
    internal static class CraftList
    {
        public const int Max = 8;
        public const int MaxLabel = 24;

        // A craft's name made safe to put in a message or a button: letters, digits, spaces and a few marks; no brackets, no control characters.
        public static string CleanLabel(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            StringBuilder clean = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '(' || c == ')' || c == '\'') clean.Append(c);
                else if (char.IsWhiteSpace(c) && clean.Length > 0 && clean[clean.Length - 1] != ' ') clean.Append(' ');
                if (clean.Length >= MaxLabel) break;
            }
            return clean.ToString().Trim();
        }

        // Adds a craft, or replaces the one with the same file. False when the list is full.
        public static bool Upsert(IList<CraftChoice> list, CraftChoice craft)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].File, craft.File, StringComparison.OrdinalIgnoreCase))
                {
                    list[i] = craft;
                    return true;
                }
            }
            if (list.Count >= Max) return false;
            list.Add(craft);
            return true;
        }

        // A craft with this hash (a copy of one already in the list, whatever its file name is).
        public static int IndexOfHash(IList<CraftChoice> list, string hash)
        {
            if (string.IsNullOrEmpty(hash)) return -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Hash == hash) return i;
            return -1;
        }

        // ---- In the activity's <Data> (travels to every player) ---------------------------

        // <Craft team="1" file="tank.xml" hash="..." site="..." label="Tank"/>, one for each craft, in order.
        public static void Write(XElement data, int team, IList<CraftChoice> crafts)
        {
            foreach (CraftChoice craft in crafts)
            {
                XElement element = new XElement("Craft", new XAttribute("team", team), new XAttribute("file", craft.File));
                if (!string.IsNullOrEmpty(craft.Hash)) element.Add(new XAttribute("hash", craft.Hash));
                if (!string.IsNullOrEmpty(craft.Site)) element.Add(new XAttribute("site", craft.Site));
                if (!string.IsNullOrEmpty(craft.Label)) element.Add(new XAttribute("label", craft.Label));
                data.Add(element);
            }
        }

        // The crafts of one team from the activity's <Data>. Files and ids are cleaned (they come over the network).
        public static List<CraftChoice> Read(XElement data, int team)
        {
            List<CraftChoice> list = new List<CraftChoice>();
            if (data == null) return list;
            foreach (XElement element in data.Elements("Craft"))
            {
                int number;
                if (!int.TryParse((string)element.Attribute("team"), out number) || number != team) continue;
                CraftChoice craft = From(element);
                if (craft != null) Upsert(list, craft);
            }
            return list;
        }

        // A <Craft> element (in a .match.xml team or in the activity) as a craft, or null without a usable file.
        public static CraftChoice From(XElement element)
        {
            string file = CraftProvider.SafeName((string)element.Attribute("file") ?? "");
            if (file.Length == 0) return null;
            return new CraftChoice
            {
                File = file,
                Hash = ((string)element.Attribute("hash") ?? "").Trim(),
                Site = SiteCrafts.ParseId((string)element.Attribute("site") ?? ""),
                Label = CleanLabel((string)element.Attribute("label") ?? ""),
            };
        }

        // ---- The server's rule ------------------------------------------------------------

        // Does a craft with this hash count as one of the team's? A team with no crafts accepts any; so does one with a craft that has no hash
        // (nothing to compare it with).
        public static bool Accepts(IList<CraftChoice> crafts, string hash)
        {
            if (crafts == null || crafts.Count == 0) return true;
            foreach (CraftChoice craft in crafts)
            {
                if (string.IsNullOrEmpty(craft.Hash)) return true;
                if (craft.Hash == hash) return true;
            }
            return false;
        }

        // The craft to start with for a player: the first one they can use (the file is on their computer, or it can be downloaded).
        public static int DefaultIndex(IList<CraftChoice> crafts, Func<CraftChoice, bool> available)
        {
            for (int i = 0; i < crafts.Count; i++)
                if (available(crafts[i])) return i;
            return 0;
        }
    }
}
