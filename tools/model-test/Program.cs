using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Assets.Scripts.Flight.StartLocations;
using Assets.Scripts.Multiplayer.ActivityFramework;
using UnityEngine;

// Round trip of the F2 model outside the game: build a match the way the menu does, save it, read it back, clone it, make the activity XML and
// feed that to the game's own parser. Uses the mod's real code through reflection (its types are internal).
internal static class Program
{
    private static Assembly mod;
    private static int failures;

    private static void Check(string what, bool ok)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) failures++;
    }

    private static object Clone0(Type matchType, object match)
    {
        object copy = matchType.GetMethod("Clone").Invoke(match, null);
        foreach (object team in (IList)Get(copy, "Teams"))
            foreach (object spawn in (IList)Get(team, "Spawns")) Set(spawn, "Speed", 0f);
        return copy;
    }

    // The mod's CompatibilityCheck against the game's real DLLs: the number of things it did not find.
    private static int RunCompat()
    {
        Type check = T("CompatibilityCheck");
        check.GetMethod("Run", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        return (int)check.GetProperty("Missing", BindingFlags.Public | BindingFlags.Static).GetValue(null);
    }

    private static object Prop(object o, string prop)
    {
        return o.GetType().GetProperty(prop).GetValue(o);
    }

    // At most CraftList.Max crafts go in a list.
    private static bool MaxCraftsOk(Type craftList, Func<string, string, string, string, object> choice)
    {
        Type choiceType = choice("a.xml", "", "", "").GetType();
        IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(choiceType));
        int added = 0;
        for (int i = 0; i < 10; i++)
            if ((bool)Call(craftList, "Upsert", list, choice("c" + i + ".xml", "", "", ""))) added++;
        return added == 8 && list.Count == 8;
    }

    private static Type T(string name) { return mod.GetType("SP2ServerMod." + name); }
    private static object Get(object o, string prop) { return o.GetType().GetField(prop).GetValue(o); }
    private static void Set(object o, string prop, object value) { o.GetType().GetField(prop).SetValue(o, value); }
    private static object Call(Type t, string name, params object[] args)
    {
        return t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }

    private static int Main(string[] args)
    {
        string work = Path.Combine(Path.GetTempPath(), "sp2sm-roundtrip-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(work);
        string configPath = work;

        mod = Assembly.Load("SP2ServerMod");
        foreach (string dll in new[] { "Jundroo.Packages.dll", "Jundroo.Common.dll", "FishNet.Runtime.dll" })
            Assembly.LoadFrom(Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common\SimplePlanes 2\SimplePlanes 2_Data\Managed", dll));

        // The plugin's logger, and BepInEx's config folder, which MatchStore.Folder reads.
        BepInEx.Logging.Logger.Listeners.Add(new Listener());
        T("SP2ServerModPlugin").GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("SP2SM"));
        PropertyInfo cfg = typeof(BepInEx.Paths).GetProperty("ConfigPath");
        cfg.GetSetMethod(true).Invoke(null, new object[] { configPath });
        Console.WriteLine("config folder: " + configPath);

        Type store = T("MatchStore");
        Type matchType = T("MatchDefinition");
        Type spawnType = T("SpawnPoint");
        Type modeType = T("MatchMode");

        Console.WriteLine("---- build a match like the menu (New match, name, TDM, 2 spawns each, one in the air)");
        string id = (string)Call(store, "FreeId", "match");
        Check("FreeId gives 'match' in an empty folder (got '" + id + "')", id == "match");

        object match = Activator.CreateInstance(matchType);
        Set(match, "Id", id);
        Set(match, "Name", "Ilha & \"Teste\" <1>");
        Set(match, "Description", "Duas equipes. Acentuação: ção.");
        Set(match, "TimerSeconds", 420);
        Set(match, "Lives", 3);
        Set(match, "KillLimit", 25);
        matchType.GetMethod("GetOrAddTeam").Invoke(match, new object[] { 1 });
        Set(match, "Mode", Enum.Parse(modeType, "TeamDeathmatch"));
        object t1 = matchType.GetMethod("GetOrAddTeam").Invoke(match, new object[] { 1 });
        object t2 = matchType.GetMethod("GetOrAddTeam").Invoke(match, new object[] { 2 });
        Check("two teams exist", Get(match, "Teams") != null && ((IEnumerable)Get(match, "Teams")).Cast<object>().Count() == 2);

        Action<object, Vector3, float, bool, float, float> add = (team, pos, hdg, ground, speed, spread) =>
        {
            object s = Activator.CreateInstance(spawnType);
            Set(s, "Position", pos);
            Set(s, "Rotation", new Vector3(0f, hdg, 0f));
            Set(s, "OnGround", ground);
            Set(s, "Speed", speed);
            Set(s, "Spread", spread);
            ((IList)Get(team, "Spawns")).Add(s);
        };
        add(t1, new Vector3(12873.25f, 4.55f, -29883f), 45f, true, 0f, 15f);
        add(t1, new Vector3(12900.5f, 812.5f, -29850.25f), 90.5f, false, 135f, 30f);
        add(t2, new Vector3(13073f, 4.55f, -29683f), 225f, true, 0f, 15f);
        add(t2, new Vector3(13100f, 4.55f, -29600f), 270f, true, 0f, 0f);

        Console.WriteLine("---- save");
        Check("Save returns true", (bool)Call(store, "Save", match));
        string file = (string)Get(match, "SourceFile");
        Check("file is in the matches folder: " + file, file != null && file.StartsWith(Path.Combine(configPath, "sp2servermod")) && File.Exists(file));
        Check("no .tmp left behind", !File.Exists(file + ".tmp"));
        byte[] bytes = File.ReadAllBytes(file);
        Check("UTF-8 without BOM", !(bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF));
        Console.WriteLine(File.ReadAllText(file));

        Console.WriteLine("---- save again (overwrite)");
        Set(match, "TimerSeconds", 600);
        Check("second Save returns true", (bool)Call(store, "Save", match));
        Check("no .tmp left behind", !File.Exists(file + ".tmp"));

        Console.WriteLine("---- read back");
        object back = Call(store, "Load", file);
        Check("Load returns a match", back != null);
        Check("id", (string)Get(back, "Id") == id);
        Check("name with & \" < > survived", (string)Get(back, "Name") == "Ilha & \"Teste\" <1>");
        Check("description with accents survived", (string)Get(back, "Description") == "Duas equipes. Acentuação: ção.");
        Check("mode TDM", Get(back, "Mode").ToString() == "TeamDeathmatch");
        Check("timer 600 (the overwrite)", (int)Get(back, "TimerSeconds") == 600);
        Check("lives 3", (int)Get(back, "Lives") == 3);
        Check("kill limit 25", (int)Get(back, "KillLimit") == 25);
        Check("same XML after the round trip", XNode.DeepEquals(
            (XElement)Call(store, "ToXml", match), (XElement)Call(store, "ToXml", back)));

        object bt1 = matchType.GetMethod("Team").Invoke(back, new object[] { 1 });
        object bt2 = matchType.GetMethod("Team").Invoke(back, new object[] { 2 });
        IList s1 = (IList)Get(bt1, "Spawns"), s2 = (IList)Get(bt2, "Spawns");
        Check("team 1 has 2 spawns, team 2 has 2", s1.Count == 2 && s2.Count == 2);
        Check("air spawn: position, heading, speed, spread",
            (Vector3)Get(s1[1], "Position") == new Vector3(12900.5f, 812.5f, -29850.25f) &&
            Mathf.Approximately(((Vector3)Get(s1[1], "Rotation")).y, 90.5f) &&
            !(bool)Get(s1[1], "OnGround") && Mathf.Approximately((float)Get(s1[1], "Speed"), 135f) &&
            Mathf.Approximately((float)Get(s1[1], "Spread"), 30f));
        Check("ground spawn with spread 0 kept 0", Mathf.Approximately((float)Get(s2[1], "Spread"), 0f));

        Console.WriteLine("---- FreeId with the file there");
        string next = (string)Call(store, "FreeId", id);
        Check("FreeId('match') is now 'match2' (got '" + next + "')", next == "match2");
        string dirty = (string)Call(store, "FreeId", "../Weird name!");
        Check("FreeId cleans a bad id ('" + dirty + "')", dirty.IndexOfAny(new[] { '/', '\\', ' ', '!' }) < 0);

        Console.WriteLine("---- Clone is deep");
        object copy = matchType.GetMethod("Clone").Invoke(back, null);
        Set(copy, "Id", "copy");
        object ct1 = matchType.GetMethod("Team").Invoke(copy, new object[] { 1 });
        ((IList)Get(ct1, "Spawns")).RemoveAt(0);
        Set(((IList)Get(ct1, "Spawns"))[0], "Speed", 999f);
        Check("original still has 2 spawns in team 1", ((IList)Get(bt1, "Spawns")).Count == 2);
        Check("original spawn speed untouched", Mathf.Approximately((float)Get(s1[1], "Speed"), 135f));

        Console.WriteLine("---- GetOrAddTeam / validation");
        System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>(), warnings = new System.Collections.Generic.List<string>();
        matchType.GetMethod("Validate").Invoke(back, new object[] { errors, warnings });
        Check("a good match has no errors (" + string.Join("; ", errors) + ")", errors.Count == 0);
        Console.WriteLine("   warnings: " + (warnings.Count == 0 ? "(none)" : string.Join(" | ", warnings)));

        object empty = Activator.CreateInstance(matchType);
        Set(empty, "Id", "empty");
        Set(empty, "Name", "Empty");
        matchType.GetMethod("GetOrAddTeam").Invoke(empty, new object[] { 1 });
        errors.Clear(); warnings.Clear();
        matchType.GetMethod("Validate").Invoke(empty, new object[] { errors, warnings });
        Check("a new match without spawns is an error (" + string.Join("; ", errors) + ")", errors.Count > 0);

        Console.WriteLine("---- the activity XML and the game's parser");
        // Quaternion.Euler is a native Unity call and does not run outside the game, so the air spawn's speed is set to 0 for this part
        // (its velocity is the one line the game computes with Unity's math; everything else is plain XML).
        object forActivity = matchType.GetMethod("Clone").Invoke(back, null);
        Set(((IList)Get(matchType.GetMethod("Team").Invoke(forActivity, new object[] { 1 }), "Spawns"))[1], "Speed", 0f);
        XElement activity = (XElement)T("MatchRegistry").GetMethod("BuildActivityXml", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { forActivity });
        Console.WriteLine(activity);
        NetworkedActivityData data = NetworkedActivityData.LoadFromXml(activity);
        Check("parser accepted it: id " + data.Id, data.Id == "SP2SM." + id);
        Check("display name kept", data.DisplayName == "Ilha & \"Teste\" <1>");
        Check("timer 600, teamDeathmatch true, lives 3, killLimit 25 in <Data>",
            (string)data.XmlData.Attribute("timer") == "600" && (string)data.XmlData.Attribute("teamDeathmatch") == "true" &&
            (string)data.XmlData.Attribute("lives") == "3" && (string)data.XmlData.Attribute("killLimit") == "25");
        int locations = 0;
        foreach (XElement group in data.XmlData.Elements("StartLocations"))
            foreach (XElement location in group.Elements())
            {
                StartLocationData s = new StartLocationData(location, StartLocationType.Temp);
                locations++;
                Console.WriteLine("   team " + group.Attribute("teamId")?.Value + " pos=" + s.Position + " rot=" + s.Rotation + " onGround=" + s.StartOnGround + " vel=" + s.InitialVelocity + " spread=" + s.MaxDistributionAmount);
            }
        Check("4 start locations parsed by the game", locations == 4);
        XElement again = XElement.Parse(data.XmlData.ToString(SaveOptions.DisableFormatting));
        Check("network round trip identical", XNode.DeepEquals(again, data.XmlData));

        Console.WriteLine("---- the crafts of a team (a list)");
        Type provider = T("CraftProvider");
        Type craftList = T("CraftList");
        Type choiceType = T("CraftChoice");
        Check("SafeName keeps a plain name and adds .xml", (string)Call(provider, "SafeName", "Tank A") == "Tank A.xml");
        Check("SafeName drops folders", (string)Call(provider, "SafeName", @"..\..\evil\Tank.xml") == "Tank.xml");
        Check("SafeName of nothing is empty", (string)Call(provider, "SafeName", "  ") == "");
        Check("CleanLabel keeps a plain name", (string)Call(craftList, "CleanLabel", "Tank A (late)") == "Tank A (late)");
        Check("CleanLabel drops brackets, bars, tags and line breaks", (string)Call(craftList, "CleanLabel", "<b>[Tank]|A\n") == "bTankA");
        Check("CleanLabel is at most 24 characters", ((string)Call(craftList, "CleanLabel", new string('x', 60))).Length == 24);
        Check("CleanLabel of blank is empty", (string)Call(craftList, "CleanLabel", "   ") == "");

        // Craft files in the mod's crafts folder, as the menu's "Add the craft I'm flying" writes them.
        string craftsFolder = (string)provider.GetProperty("Folder", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        Directory.CreateDirectory(craftsFolder);
        XElement craft = new XElement("Aircraft", new XAttribute("name", "Tank A"), new XElement("Parts", new XElement("Part", new XAttribute("id", 1))));
        craft.Save(Path.Combine(craftsFolder, "tank-a.xml"), SaveOptions.DisableFormatting);
        // HashOf is the game's own CraftDatabase.CompressCraftXml + ComputeHash, which start Unity's profiler (native): it only runs inside the
        // game. Here stand-in values are used to test how hashes are stored and carried.
        string hashA = "QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUE=", hashB = "QkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkI=", hashC = "Q0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0M=";

        object[] loadArgs = { "tank-a", null, null };
        bool found = (bool)provider.GetMethod("TryLoad").Invoke(null, loadArgs);
        Check("TryLoad finds it by name without .xml, in the mod's folder", found && (string)loadArgs[2] == "the mod's crafts folder");
        object[] missingArgs = { "nothing-here.xml", null, null };
        Check("TryLoad of a missing craft is false", !(bool)provider.GetMethod("TryLoad").Invoke(null, missingArgs));

        Func<string, string, string, string, object> choice = (file, hash, siteId, label) =>
        {
            object c = Activator.CreateInstance(choiceType);
            Set(c, "File", file);
            Set(c, "Hash", hash);
            Set(c, "Site", siteId);
            Set(c, "Label", label);
            return c;
        };
        Func<object, object> team1Of = m => matchType.GetMethod("Team").Invoke(m, new object[] { 1 });
        Func<object, object> team2Of = m => matchType.GetMethod("Team").Invoke(m, new object[] { 2 });

        object multi = Clone0(matchType, back);
        Set(multi, "Id", "multi");
        Set(multi, "SourceFile", null);
        IList crafts1 = (IList)Get(team1Of(multi), "Crafts");
        IList crafts2 = (IList)Get(team2Of(multi), "Crafts");
        Check("a team starts with no crafts", crafts1.Count == 0 && !(bool)team1Of(multi).GetType().GetProperty("HasCraft").GetValue(team1Of(multi)));
        Check("Upsert adds three crafts", (bool)Call(craftList, "Upsert", crafts1, choice("tank-a.xml", hashA, "", "Tank A")) &&
                                          (bool)Call(craftList, "Upsert", crafts1, choice("site-AbC123xyz.xml", hashB, "AbC123xyz", "Heavy Tank")) &&
                                          (bool)Call(craftList, "Upsert", crafts1, choice("scout.xml", "", "", "")) && crafts1.Count == 3);
        Check("Upsert of the same file replaces it (case does not matter)", (bool)Call(craftList, "Upsert", crafts1, choice("TANK-A.xml", hashC, "", "Tank A2")) &&
                                                                             crafts1.Count == 3 && (string)Get(crafts1[0], "Hash") == hashC);
        Check("Upsert of the same file again to restore it", (bool)Call(craftList, "Upsert", crafts1, choice("tank-a.xml", hashA, "", "Tank A")) && (string)Get(crafts1[0], "Hash") == hashA);
        Check("IndexOfHash finds a craft by its hash", (int)Call(craftList, "IndexOfHash", crafts1, hashB) == 1 && (int)Call(craftList, "IndexOfHash", crafts1, "nope") == -1 &&
                                                       (int)Call(craftList, "IndexOfHash", crafts1, "") == -1);
        Check("a team has at most 8 crafts", MaxCraftsOk(craftList, choice));
        Call(craftList, "Upsert", crafts2, choice("tank-b.xml", hashB, "", "Tank B"));

        // The title: the craft's own name, or the file without .xml.
        Check("Title is the label, or the file name without .xml", (string)Prop(crafts1[0], "Title") == "Tank A" && (string)Prop(crafts1[2], "Title") == "scout");

        Console.WriteLine("---- saving and reading the list");
        Check("Save with crafts", (bool)Call(store, "Save", multi));
        object reread = Call(store, "Load", (string)Get(multi, "SourceFile"));
        IList r1 = (IList)Get(team1Of(reread), "Crafts"), r2 = (IList)Get(team2Of(reread), "Crafts");
        Check("three crafts of team 1 survive the file, in order, with every field",
            r1.Count == 3 && (string)Get(r1[0], "File") == "tank-a.xml" && (string)Get(r1[0], "Hash") == hashA && (string)Get(r1[0], "Label") == "Tank A" &&
            (string)Get(r1[1], "File") == "site-AbC123xyz.xml" && (string)Get(r1[1], "Site") == "AbC123xyz" && (string)Get(r1[1], "Label") == "Heavy Tank" &&
            (string)Get(r1[2], "File") == "scout.xml" && (string)Get(r1[2], "Hash") == "" && (string)Get(r1[2], "Site") == "");
        Check("team 2 has its one craft", r2.Count == 1 && (string)Get(r2[0], "File") == "tank-b.xml");
        Check("the same XML after the round trip", XNode.DeepEquals((XElement)Call(store, "ToXml", multi), (XElement)Call(store, "ToXml", reread)));
        Console.WriteLine(File.ReadAllText((string)Get(multi, "SourceFile")).Split('\n').Length + " lines in the file");
        Check("the old team without crafts stays without", ((IList)Get(team1Of(back), "Crafts")).Count == 0);

        // A file written by the first version: one craft as attributes of <Team>.
        string legacyPath = Path.Combine(work, "legacy.match.xml");
        File.WriteAllText(legacyPath, "<Match id=\"legacy\"><Team id=\"1\" craft=\"old.xml\" craftHash=\"" + hashA + "\" craftSite=\"AbC123xyz\"><Craft file=\"new.xml\" hash=\"" + hashB + "\"/><Spawn position=\"1,2,3\"/></Team></Match>");
        IList l1 = (IList)Get(team1Of(Call(store, "Load", legacyPath)), "Crafts");
        Check("an old single-craft file is read as the first craft of the list", l1.Count == 2 && (string)Get(l1[0], "File") == "old.xml" && (string)Get(l1[0], "Site") == "AbC123xyz" &&
                                                                                 (string)Get(l1[0], "Hash") == hashA && (string)Get(l1[1], "File") == "new.xml");
        string hostilePath = Path.Combine(work, "hostile.match.xml");
        File.WriteAllText(hostilePath, "<Match id=\"hostile\"><Team id=\"1\"><Craft file=\"evil.xml\"/><Craft file=\"..\\..\\evil.xml\" site=\"../../x\" label=\"&lt;b&gt;Hi\"/><Craft site=\"AbC123xyz\"/><Spawn position=\"1,2,3\"/></Team></Match>");
        IList h1 = (IList)Get(team1Of(Call(store, "Load", hostilePath)), "Crafts");
        Check("a hand-written file is cleaned: folders, ids, labels, doubles and a craft with no file", h1.Count == 1 && (string)Get(h1[0], "File") == "evil.xml" &&
                                                                                                         (string)Get(h1[0], "Site") == "" && (string)Get(h1[0], "Label") == "bHi");

        Console.WriteLine("---- the list is a copy of its own");
        object cloned = matchType.GetMethod("Clone").Invoke(reread, null);
        IList c1 = (IList)Get(team1Of(cloned), "Crafts");
        Check("Clone keeps the crafts", c1.Count == 3 && (string)Get(c1[1], "Site") == "AbC123xyz");
        c1.RemoveAt(0);
        Set(c1[0], "Label", "changed");
        Check("changing the clone leaves the original alone", r1.Count == 3 && (string)Get(r1[1], "Label") == "Heavy Tank");

        Console.WriteLine("---- checking a craft");
        Check("a craft whose file is missing says so: " + Call(provider, "Check", choice("tank-z.xml", "", "", "")),
            ((string)Call(provider, "Check", choice("tank-z.xml", "", "", "")) ?? "").Contains("was not found"));
        Check("a craft from the website that is not here yet has no warning", Call(provider, "Check", choice("site-AbC123xyz.xml", hashA, "AbC123xyz", "")) == null);
        Check("a craft that is here and has no hash has no warning", Call(provider, "Check", choice("tank-a.xml", "", "", "")) == null);
        Check("Available: a file that is here, a website craft, a missing file",
            (bool)Call(provider, "Available", choice("tank-a.xml", "", "", "")) && (bool)Call(provider, "Available", choice("nowhere.xml", "", "AbC123xyz", "")) &&
            !(bool)Call(provider, "Available", choice("nowhere.xml", "", "", "")));
        Check("NextFileName gives the first free <match>-team<N>-<k>.xml", (string)Call(provider, "NextFileName", multi, 1) == "multi-team1-1.xml");
        File.WriteAllText(Path.Combine(craftsFolder, "multi-team1-1.xml"), "<Aircraft/>");
        Check("NextFileName skips a file that exists", (string)Call(provider, "NextFileName", multi, 1) == "multi-team1-2.xml");

        Console.WriteLine("---- the crafts in the activity, the game's parser and the server's rule");
        XElement multiActivity = (XElement)T("MatchRegistry").GetMethod("BuildActivityXml", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null,
            new object[] { Clone0(matchType, reread) });
        Console.WriteLine(multiActivity.Element("Data").Elements("Craft").Count() + " <Craft> elements");
        NetworkedActivityData multiData = NetworkedActivityData.LoadFromXml(multiActivity);
        Check("the game's parser takes an activity whose <Data> has <Craft> elements", multiData != null && multiData.Id == "SP2SM.multi");
        Check("<Data> has four <Craft> elements (three for team 1, one for team 2)",
            multiData.XmlData.Elements("Craft").Count() == 4 && multiData.XmlData.Elements("Craft").Count(e => (string)e.Attribute("team") == "1") == 3);
        IList d1 = (IList)Call(craftList, "Read", multiData.XmlData, 1), d2 = (IList)Call(craftList, "Read", multiData.XmlData, 2);
        Check("Read gives back the crafts of each team, in order, with every field",
            d1.Count == 3 && (string)Get(d1[0], "File") == "tank-a.xml" && (string)Get(d1[1], "Site") == "AbC123xyz" && (string)Get(d1[1], "Label") == "Heavy Tank" &&
            (string)Get(d1[2], "Hash") == "" && d2.Count == 1 && (string)Get(d2[0], "File") == "tank-b.xml");
        Check("the crafts survive the network round trip", XNode.DeepEquals(XElement.Parse(multiData.XmlData.ToString(SaveOptions.DisableFormatting)), multiData.XmlData));
        Check("a match without crafts has no <Craft> elements", !data.XmlData.Elements("Craft").Any());
        object ffaMulti = Clone0(matchType, reread);
        Set(ffaMulti, "Mode", Enum.Parse(modeType, "FreeForAll"));
        NetworkedActivityData ffaMultiData = NetworkedActivityData.LoadFromXml((XElement)T("MatchRegistry").GetMethod("BuildActivityXml", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { ffaMulti }));
        Check("free for all only carries team 1's crafts", ffaMultiData.XmlData.Elements("Craft").Count() == 3 && ffaMultiData.XmlData.Elements("Craft").All(e => (string)e.Attribute("team") == "1"));
        XElement hostileData = new XElement("Data", new XElement("Craft", new XAttribute("team", "1"), new XAttribute("file", @"..\..\evil"), new XAttribute("site", "../x"), new XAttribute("label", "<b>[x]")),
                                                    new XElement("Craft", new XAttribute("team", "2"), new XAttribute("file", "ok.xml")));
        IList hostile = (IList)Call(craftList, "Read", hostileData, 1);
        Check("crafts that arrive over the network are cleaned", hostile.Count == 1 && (string)Get(hostile[0], "File") == "evil.xml" && (string)Get(hostile[0], "Site") == "" && (string)Get(hostile[0], "Label") == "bx");

        // The server accepts any craft of the team.
        Func<IEnumerable<object>, object> typed = items => { IList l = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(choiceType)); foreach (object o in items) l.Add(o); return l; };
        Check("Accepts: a team with no crafts takes anything", (bool)Call(craftList, "Accepts", typed(new object[0]), "whatever"));
        Check("Accepts: the first, the second craft of a team with hashes", (bool)Call(craftList, "Accepts", typed(d1.Cast<object>().Take(2)), hashA) &&
                                                                          (bool)Call(craftList, "Accepts", typed(d1.Cast<object>().Take(2)), hashB));
        Check("Accepts: another hash is refused", !(bool)Call(craftList, "Accepts", typed(d1.Cast<object>().Take(2)), hashC));
        Check("Accepts: a team with a craft that has no hash accepts anything (nothing to compare)", (bool)Call(craftList, "Accepts", d1, hashC));
        Check("Accepts: a hash the team does not have, when all have hashes, is refused", !(bool)Call(craftList, "Accepts", d2, hashA) && (bool)Call(craftList, "Accepts", d2, hashB));

        // The craft a player starts with: the first one they can use.
        Type funcType = typeof(Func<,>).MakeGenericType(choiceType, typeof(bool));
        System.Linq.Expressions.ParameterExpression p = System.Linq.Expressions.Expression.Parameter(choiceType, "c");
        Delegate notTankA = System.Linq.Expressions.Expression.Lambda(funcType,
            System.Linq.Expressions.Expression.NotEqual(System.Linq.Expressions.Expression.Field(p, "File"), System.Linq.Expressions.Expression.Constant("tank-a.xml")), p).Compile();
        Delegate none = System.Linq.Expressions.Expression.Lambda(funcType, System.Linq.Expressions.Expression.Constant(false), p).Compile();
        Check("DefaultIndex: the first craft they can use", (int)Call(craftList, "DefaultIndex", d1, notTankA) == 1 && (int)Call(craftList, "DefaultIndex", d1, none) == 0);

        Console.WriteLine("---- craft from the website");
        Type site = T("SiteCrafts");
        Check("ParseId of a link", (string)Call(site, "ParseId", "https://www.simpleplanes.com/a/AbC123xyz/Some-Tank") == "AbC123xyz");
        Check("ParseId of a feedback link", (string)Call(site, "ParseId", "https://www.simpleplanes.com/Feedback/View/Q9w8e7") == "Q9w8e7");
        Check("ParseId of a bare id", (string)Call(site, "ParseId", "  AbC123xyz ") == "AbC123xyz");
        Check("ParseId of nonsense is empty", (string)Call(site, "ParseId", "../../etc/passwd") == "" && (string)Call(site, "ParseId", "a b") == "" && (string)Call(site, "ParseId", "") == "");
        Check("FileFor", (string)Call(site, "FileFor", "AbC123xyz") == "site-AbC123xyz.xml");

        Console.WriteLine("---- delete keeps a copy");
        Check("Delete returns true", (bool)Call(store, "Delete", back));
        Check("file moved away", !File.Exists(file));
        string deleted = Path.Combine(configPath, "sp2servermod", "deleted");
        Check("a dated copy is in 'deleted'", Directory.Exists(deleted) && Directory.GetFiles(deleted).Length == 1);

        Console.WriteLine("---- an old file (no lives/killLimit) still loads");
        string oldFile = Path.Combine(work, "old.match.xml");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "test.match.xml"), oldFile, true);
        object old = Call(store, "Load", oldFile);
        Check("loads", old != null);
        Check("lives and killLimit default to 0", old != null && (int)Get(old, "Lives") == 0 && (int)Get(old, "KillLimit") == 0);

        Console.WriteLine("---- F4: the referee's decision");
        Type logic = T("RefereeLogic");
        Type entryType = T("RefereeEntry");
        Func<string, int, int, int, object> entry = (name, team, kills, deaths) =>
        {
            object e = Activator.CreateInstance(entryType);
            entryType.GetField("Name").SetValue(e, name);
            entryType.GetField("Team").SetValue(e, team);
            entryType.GetField("Kills").SetValue(e, kills);
            entryType.GetField("Deaths").SetValue(e, deaths);
            return e;
        };
        Func<object[], int, int, bool, int, string> decide = (list, lives, kills, teams, most) =>
        {
            Array array = Array.CreateInstance(entryType, list.Length);
            for (int i = 0; i < list.Length; i++) array.SetValue(list[i], i);
            return (string)logic.GetMethod("Decide").Invoke(null, new object[] { array, lives, kills, teams, most });
        };

        // No limits: the referee never ends the match (the timer does).
        Check("no lives, no kill limit: goes on", decide(new[] { entry("A", 1, 50, 50), entry("B", 1, 0, 90) }, 0, 0, false, 2) == null);
        // Free for all, lives.
        Check("FFA 1 life: both alive goes on", decide(new[] { entry("A", 1, 0, 0), entry("B", 1, 0, 0) }, 1, 0, false, 2) == null);
        Check("FFA 1 life: one dies, the other wins: " + decide(new[] { entry("A", 1, 1, 0), entry("B", 1, 0, 1) }, 1, 0, false, 2),
            (decide(new[] { entry("A", 1, 1, 0), entry("B", 1, 0, 1) }, 1, 0, false, 2) ?? "").StartsWith("A is the last"));
        Check("FFA 3 lives: 2 deaths is still alive", decide(new[] { entry("A", 1, 1, 0), entry("B", 1, 0, 2) }, 3, 0, false, 2) == null);
        Check("FFA 3 lives: 3 deaths is out", decide(new[] { entry("A", 1, 1, 0), entry("B", 1, 0, 3) }, 3, 0, false, 2) != null);
        Check("FFA 3 players, one out: goes on", decide(new[] { entry("A", 1, 0, 0), entry("B", 1, 0, 1), entry("C", 1, 0, 0) }, 1, 0, false, 3) == null);
        Check("a lone player is not 'the last one standing'", decide(new[] { entry("A", 1, 0, 0) }, 1, 0, false, 1) == null);
        Check("a player left alone after others left wins", decide(new[] { entry("A", 1, 0, 0) }, 1, 0, false, 3) != null);
        Check("the only player dies: everybody is out", (decide(new[] { entry("A", 1, 0, 1) }, 1, 0, false, 1) ?? "").Contains("Draw"));
        Check("all out at the same time: draw", (decide(new[] { entry("A", 1, 0, 1), entry("B", 1, 0, 1) }, 1, 0, false, 2) ?? "").Contains("Draw"));
        // Kill limit.
        Check("FFA kill limit not reached", decide(new[] { entry("A", 1, 9, 0), entry("B", 1, 3, 0) }, 0, 10, false, 2) == null);
        Check("FFA kill limit reached: " + decide(new[] { entry("A", 1, 10, 0), entry("B", 1, 3, 0) }, 0, 10, false, 2),
            (decide(new[] { entry("A", 1, 10, 0), entry("B", 1, 3, 0) }, 0, 10, false, 2) ?? "").StartsWith("A wins with 10"));
        Check("FFA kill limit: the higher one wins when both pass", (decide(new[] { entry("A", 1, 10, 0), entry("B", 1, 12, 0) }, 0, 10, false, 2) ?? "").StartsWith("B wins"));
        // Teams.
        Check("TDM kill limit: team kills add up", decide(new[] { entry("A", 1, 6, 0), entry("B", 1, 5, 0), entry("C", 2, 4, 0) }, 0, 10, true, 3) == "Team 1 wins with 11 kills!");
        Check("TDM kill limit not reached", decide(new[] { entry("A", 1, 4, 0), entry("B", 1, 5, 0), entry("C", 2, 4, 0) }, 0, 10, true, 3) == null);
        Check("TDM lives: team 1 all out, team 2 wins", decide(new[] { entry("A", 1, 0, 1), entry("B", 1, 0, 1), entry("C", 2, 0, 0) }, 1, 0, true, 3) == "Team 2 wins: team 1 has no lives left!");
        Check("TDM lives: one of team 1 alive goes on", decide(new[] { entry("A", 1, 0, 1), entry("B", 1, 0, 0), entry("C", 2, 0, 0) }, 1, 0, true, 3) == null);
        Check("TDM lives: nobody alive is a draw", (decide(new[] { entry("A", 1, 0, 1), entry("C", 2, 0, 1) }, 1, 0, true, 2) ?? "").Contains("Draw"));
        Check("TDM with only one team present does not end", decide(new[] { entry("A", 1, 0, 0), entry("B", 1, 0, 0) }, 1, 0, true, 2) == null);
        Check("nobody playing: nothing to decide", decide(new object[0], 1, 5, false, 0) == null);

        Console.WriteLine("---- F5: the messages");
        Func<int, string, int, int, int, object> entryId = (id, name, team, kills, deaths) =>
        {
            object e = entry(name, team, kills, deaths);
            entryType.GetField("Id").SetValue(e, id);
            return e;
        };
        Func<object[], object[], int, bool, string[]> changes = (before, after, lives, teams) =>
        {
            Type dictType = typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(int), entryType);
            object dict = Activator.CreateInstance(dictType);
            foreach (object b in before) dictType.GetMethod("set_Item").Invoke(dict, new object[] { entryType.GetField("Id").GetValue(b), b });
            Array array = Array.CreateInstance(entryType, after.Length);
            for (int i = 0; i < after.Length; i++) array.SetValue(after[i], i);
            object result = logic.GetMethod("Changes").Invoke(null, new object[] { dict, array, lives, teams });
            return ((System.Collections.Generic.List<string>)result).ToArray();
        };
        Check("start text: " + (string)logic.GetMethod("StartText").Invoke(null, new object[] { 300, 3, 10, true }),
            (string)logic.GetMethod("StartText").Invoke(null, new object[] { 300, 3, 10, true }) == "The match begins: time 5:00, two teams, 3 lives each, first to 10 kills.");
        Check("start text without limits", (string)logic.GetMethod("StartText").Invoke(null, new object[] { 90, 0, 0, false }) == "The match begins: time 1:30, every player for themselves.");
        string[] oneLeft = changes(new[] { entryId(1, "A", 1, 0, 0), entryId(2, "B", 1, 0, 0) }, new[] { entryId(1, "A", 1, 1, 0), entryId(2, "B", 1, 0, 1) }, 3, false);
        Check("a death with lives left names the killer and the lives: " + string.Join("|", oneLeft), oneLeft.Length == 1 && oneLeft[0] == "B was shot down by A: 2 lives left.");
        string[] lastLife = changes(new[] { entryId(1, "A", 1, 0, 0), entryId(2, "B", 1, 0, 0) }, new[] { entryId(1, "A", 1, 0, 0), entryId(2, "B", 1, 0, 1) }, 1, false);
        Check("a death on the last life: out, with who remains and no killer when nobody scored: " + string.Join("|", lastLife), lastLife.Length == 1 && lastLife[0] == "B is out of lives. 1 player left.");
        string[] outBy = changes(new[] { entryId(1, "A", 1, 0, 0), entryId(2, "B", 1, 0, 0), entryId(3, "C", 1, 0, 0) }, new[] { entryId(1, "A", 1, 1, 0), entryId(2, "B", 1, 0, 1), entryId(3, "C", 1, 0, 0) }, 1, false);
        Check("out of lives by a killer: " + string.Join("|", outBy), outBy.Length == 1 && outBy[0] == "B is out of lives (shot down by A). 2 players left.");
        string[] teamOut = changes(new[] { entryId(1, "A", 1, 0, 0), entryId(2, "B", 2, 0, 0), entryId(3, "C", 2, 0, 0) }, new[] { entryId(1, "A", 1, 0, 0), entryId(2, "B", 2, 0, 1), entryId(3, "C", 2, 0, 0) }, 1, true);
        Check("team match says who is alive per team: " + string.Join("|", teamOut), teamOut.Length == 1 && teamOut[0] == "B is out of lives. Alive: team 1 1, team 2 1.");
        Check("no lives: no death messages", changes(new[] { entryId(1, "A", 1, 0, 0) }, new[] { entryId(1, "A", 1, 0, 5) }, 0, false).Length == 0);
        Check("nothing happened: no messages", changes(new[] { entryId(1, "A", 1, 2, 1) }, new[] { entryId(1, "A", 1, 2, 1) }, 3, false).Length == 0);
        Check("a player who just joined says nothing", changes(new object[0], new[] { entryId(1, "A", 1, 0, 2) }, 3, false).Length == 0);
        string[] two = changes(new[] { entryId(1, "A", 1, 0, 0), entryId(2, "B", 1, 0, 0), entryId(3, "C", 1, 0, 0) }, new[] { entryId(1, "A", 1, 2, 1), entryId(2, "B", 1, 0, 1), entryId(3, "C", 1, 1, 0) }, 3, false);
        Check("two deaths at once name nobody: " + string.Join("|", two), two.Length == 2 && !two[0].Contains(" by ") && !two[1].Contains(" by "));

        Console.WriteLine("---- the craft link in the chat");
        string linkText = (string)logic.GetMethod("CraftLinkText").Invoke(null, new object[] { 2, "AbC123xyz", null, 1, 1 });
        Console.WriteLine("   " + linkText);
        Check("the link text names the team and the address", linkText.StartsWith("Team 2's craft: https://www.simpleplanes.com/a/AbC123xyz "));
        // The game's own chat code, which turns an address on simpleplanes.com into a clickable link.
        Type chatMessages = typeof(Assets.Scripts.Game).Assembly.GetType("Assets.Scripts.Multiplayer.ChatMessages");
        object chat = Activator.CreateInstance(chatMessages);
        string shown = (string)chatMessages.GetMethod("ReplaceUrlsInMessage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chat, new object[] { linkText });
        Console.WriteLine("   the chat shows: " + shown);
        Check("the game's chat makes it a link", shown.Contains("<link=https://www.simpleplanes.com/a/AbC123xyz>"));
        Check("the text after the address is untouched", shown.EndsWith("  (open it, download it, then Change craft and restart)"));
        string md = (string)chatMessages.GetMethod("ReplaceUrlsInMessage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chat, new object[] { "[Vehicle](https://www.simpleplanes.com/a/AbC123xyz)" });
        Console.WriteLine("   with a label the chat shows: " + md);
        Check("a label in [text](address) form is not supported by the chat: the address still shows in full", md.Contains(">https://www.simpleplanes.com/a/AbC123xyz</link>"));

        string manyText = (string)logic.GetMethod("CraftLinkText").Invoke(null, new object[] { 2, "AbC123xyz", "Heavy Tank", 2, 3 });
        Console.WriteLine("   " + manyText);
        Check("a team with several crafts gets a line for each, with the name and the number", manyText.StartsWith("Team 2 craft 2 of 3, Heavy Tank: https://www.simpleplanes.com/a/AbC123xyz "));
        string manyShown = (string)chatMessages.GetMethod("ReplaceUrlsInMessage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chat, new object[] { manyText });
        Check("the game's chat makes that one a link too", manyShown.Contains("<link=https://www.simpleplanes.com/a/AbC123xyz>") && manyShown.StartsWith("Team 2 craft 2 of 3, Heavy Tank: "));

        Console.WriteLine("---- F4: the rules about the match the game is running");
        Check("the mod's compatibility check, run against the real game files, finds the F4 members",
            RunCompat() == 0);

        Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");
        try { Directory.Delete(work, true); } catch { }
        return failures == 0 ? 0 : 1;
    }

    private sealed class Listener : BepInEx.Logging.ILogListener
    {
        public void LogEvent(object sender, BepInEx.Logging.LogEventArgs e) { Console.WriteLine("   [log] " + e.Level + ": " + e.Data); }
        public void Dispose() { }
    }
}
