using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace SP2ServerMod
{
    // The game changes between updates (a member can vanish or change type, and code compiled against the old one then dies only when it runs).
    // This looks, by reflection, at everything the mod uses and writes [COMPAT] lines to the log: ask for them in any report after a game update.
    internal static class CompatibilityCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static int Missing { get; private set; }

        public static void Run()
        {
            Missing = 0;
            try
            {
                Assembly game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Game");
                if (game == null)
                {
                    Fail("the game's assembly (Game) is not loaded");
                    return;
                }

                // The activity registry: where the mod's matches go.
                Type manager = Find(game, "Assets.Scripts.Multiplayer.ActivityFramework.NetworkedActivityManager");
                Method(manager, "LoadActivitiesFromXml", typeof(XElement));
                Method(manager, "GetRegisteredActivity", typeof(string));
                Property(manager, "RegisteredActivities");
                Field(manager, "_registeredActivities", typeof(IDictionary));
                Type main = Find(game, "Assets.Scripts.Game");
                Property(main, "NetworkedActivityManager");

                Type data = Find(game, "Assets.Scripts.Multiplayer.ActivityFramework.NetworkedActivityData");
                Method(data, "LoadFromXml", typeof(XElement));
                Find(game, "Assets.Scripts.Multiplayer.ActivityFramework.Activities.Deathmatch.DeathmatchActivityScript");
                Find(game, "Assets.Scripts.Flight.StartLocations.StartLocationData");

                // Where the player is (to mark spawns) and the on-screen message.
                Type player = Find(game, "Assets.Scripts.Flight.FlightScenePlayer");
                Property(player, "GlobalPosition");
                Property(player, "Rotation");
                Property(player, "Aircraft");
                Type scene = Find(game, "Assets.Scripts.Flight.FlightSceneScript");
                Property(scene, "Instance");
                Property(scene, "LocalPlayer");
                Property(scene, "FlightUI");
                Type ui = Find(game, "Assets.Scripts.Flight.UI.FlightUIScript");
                Method(ui, "ShowMessage", typeof(string), typeof(float), typeof(bool));

                // The menu: the flight menu's button, the layout and script lookup of the game's UI library, the dialog, the world position.
                Type menu = Find(game, "Assets.Scripts.Flight.UI.Panels.MenuPanelScript");
                MethodNamed(menu, "OnWidgetInitialized");
                Type loader = Find(game, "Jundroo.Juicy.ResourceLoader");
                MethodNamed(loader, "LoadXml");
                MethodNamed(loader, "GetScriptType");
                Type userInterface = Find(game, "Assets.Scripts.UI.UserInterface");
                MethodNamed(userInterface, "CreateDialog");
                Find(game, "Assets.Scripts.UI.PanelDialogScript");
                Type utility = Find(game, "Assets.Scripts.Utility");
                MethodNamed(utility, "ConvertAbsoluteToFloatingOriginPosition");
                // Phase F3: the craft of the player (loadout per team).
                Type networkPlayer = Find(game, "Assets.Scripts.Multiplayer.NetworkPlayerScript");
                Property(networkPlayer, "CraftId");
                Property(player, "NetworkPlayer");
                Property(player, "NetworkedActivity");
                Property(player, "IsLoadingCraft");
                Method(ui, "RestartHere");
                Property(ui, "LoadCraftCooldown");
                Type activity = Find(game, "Assets.Scripts.Multiplayer.ActivityFramework.NetworkedActivityScript");
                Property(activity, "Data");
                Method(activity, "GetPlayer", player);
                Type activityPlayer = Find(game, "Assets.Scripts.Multiplayer.ActivityFramework.NetworkedActivityPlayer");
                Property(activityPlayer, "Team");
                Type activityTeam = Find(game, "Assets.Scripts.Multiplayer.ActivityFramework.NetworkedActivityTeam");
                Property(activityTeam, "Id");
                Type activityData = Find(game, "Assets.Scripts.Multiplayer.ActivityFramework.NetworkedActivityData");
                Property(activityData, "XmlData");
                Property(activityData, "Id");
                Type crafts = Find(game, "Assets.Scripts.Craft.CraftFiles.CraftDatabase");
                Method(crafts, "SaveCraft", typeof(string), typeof(XElement), typeof(bool), typeof(bool));
                Method(crafts, "TryGetCraft", typeof(string), Find(game, "Assets.Scripts.Craft.CraftFiles.CraftFileInfo").MakeByRefType());
                Method(crafts, "CompressCraftXml", typeof(XElement));
                Method(crafts, "ComputeHash", typeof(byte[]));
                Property(main, "CraftDatabase");
                Type craftFile = Find(game, "Assets.Scripts.Craft.CraftFiles.CraftFileInfo");
                Method(craftFile, "LoadXml", typeof(bool));
                Type networkAircraft = Find(game, "Assets.Scripts.Multiplayer.NetworkAircraftScript");
                MethodNamed(networkAircraft, "GetCraftXml");

                // Phase F4: the rules on the server.
                MethodNamed(activity, "OnUpdateServer");
                MethodNamed(activity, "CanJoinActivity");
                MethodNamed(activity, "EndActivity");
                MethodNamed(activity, "ShowMessageToAllPlayers");
                Property(activity, "Players");
                Property(activity, "State");
                Method(activityPlayer, "GetScore", typeof(string));
                Property(activityPlayer, "State");
                Method(player, "SpawnAircraft", typeof(bool));
                Property(player, "NetworkedActivity");
                Type sceneNetwork = Find(game, "Assets.Scripts.Multiplayer.FlightSceneNetworkScript");
                MethodNamed(sceneNetwork, "ShowMessageToTargetPlayer");
                Property(scene, "FlightSceneNetwork");
                Method(scene, "GetPlayer", typeof(int));
                if (!networkPlayer.GetMethods(Any).Any(m => m.Name.StartsWith("RpcLogic___RpcSpawnPlayerAircraft___")))
                    Fail("method NetworkPlayerScript.RpcLogic___RpcSpawnPlayerAircraft___* (the server refusal of a spawn)");
                if (!networkAircraft.GetMethods(Any).Any(m => m.Name.StartsWith("RpcLogic___RpcLoadRemoteCraftServer___")))
                    Fail("method NetworkAircraftScript.RpcLogic___RpcLoadRemoteCraftServer___* (the server craft check)");
                MethodNamed(networkAircraft, "RequestDespawn");

                // The chat line for the craft link: the server's own way to answer a chat message to one player.
                Type sceneNet = Find(game, "Assets.Scripts.Multiplayer.FlightSceneNetworkScript");
                if (sceneNet != null && !sceneNet.GetMethods(Any).Any(m => m.Name == "SendChatMessageToTarget"))
                    Fail("method FlightSceneNetworkScript.SendChatMessageToTarget (the craft link in the chat)");

                // Phase F6: the spectator camera and the keys it must hold back.
                Type cameraManager = Find(game, "Assets.Scripts.Flight.Cameras.CameraManagerScript");
                Method(cameraManager, "EnterKillCam", Find(game, "Assets.Scripts.Craft.Parts.PartScript"));
                MethodNamed(cameraManager, "ExitKillCam");
                Property(cameraManager, "IsKillCam");
                Property(cameraManager, "Controller");
                Type gameInput = Find(game, "Assets.Scripts.Input.GameInput");
                MethodNamed(gameInput, "GetButtonDownIfEnabled");
                MethodNamed(gameInput, "GetButtonDown");
                Type gameInputs = Find(game, "Assets.Scripts.Input.GameInputs");
                Property(gameInputs, "Instance");
                Property(gameInputs, "NextView");
                Property(gameInputs, "PreviousView");
                Type aircraftScript = Find(game, "Assets.Scripts.Craft.AircraftScript");
                Property(aircraftScript, "MainCockpit");
                Property(aircraftScript, "CriticallyDamaged");
                Property(player, "Aircraft");

                // Who has the mod (the hello on the game's part message channel).
                Property(sceneNet, "IsClientStarted");
                Property(sceneNet, "IsServerStarted");
                Property(scene, "AllPlayers");
                MethodNamed(networkAircraft, "ProcessPartNetworkMessage");
                MethodNamed(Find(game, "Assets.Scripts.Multiplayer.INetworkAircraft"), "SendPartNetworkMessage");

                // Phase F5: what the player sees (the bars of the match, markers).
                Property(activity, "TimerValue");
                Property(activityPlayer, "PlayerId");
                Property(ui, "RootWidget");
                Property(main, "Instance");

                // Downloading a craft from the website (the game's own way, without its dialog).
                MethodNamed(main, "GetDownloadAircraftUrl");
                Type web = Find(game, "Assets.Scripts.Net.WebRequest");
                MethodNamed(web, "Get");
                Property(web, "IsDone");
                Property(web, "Bytes");
                Property(web, "Error");
                Method(utility, "LoadCraftXmlFromBytes", typeof(byte[]));

                Type cameras = Find(game, "Assets.Scripts.Flight.Cameras.CameraManagerScript");
                Property(cameras, "Instance");
                Property(cameras, "MainCamera");
            }
            catch (Exception e)
            {
                Fail("the check itself failed: " + e.Message);
            }

            if (Missing == 0) SP2ServerModPlugin.Log.LogInfo("[COMPAT] everything the mod uses was found");
            else SP2ServerModPlugin.Log.LogWarning("[COMPAT] " + Missing + " thing(s) missing: the mod may not work with this version of the game");
        }

        private static Type Find(Assembly game, string name)
        {
            Type type = game.GetType(name);
            if (type == null)
            {
                // Not in the game's own assembly (the UI library and some helpers live in others).
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(name);
                    if (type != null) break;
                }
            }
            if (type == null) Fail("type " + name);
            return type;
        }

        // Any overload with that name (for methods with several, or generic ones).
        private static void MethodNamed(Type type, string name)
        {
            if (type == null) return;
            if (!type.GetMethods(Any).Any(m => m.Name == name)) Fail("method " + type.Name + "." + name);
        }

        private static void Method(Type type, string name, params Type[] parameters)
        {
            if (type == null) return;
            if (type.GetMethod(name, Any, null, parameters, null) == null) Fail("method " + type.Name + "." + name);
        }

        private static void Property(Type type, string name)
        {
            if (type == null) return;
            if (type.GetProperty(name, Any) == null) Fail("property " + type.Name + "." + name);
        }

        private static void Field(Type type, string name, Type assignableTo)
        {
            if (type == null) return;
            FieldInfo field = type.GetField(name, Any);
            if (field == null) Fail("field " + type.Name + "." + name);
            else if (!assignableTo.IsAssignableFrom(field.FieldType)) Fail("field " + type.Name + "." + name + " is a " + field.FieldType.Name);
        }

        private static void Fail(string what)
        {
            Missing++;
            SP2ServerModPlugin.Log.LogWarning("[COMPAT] missing: " + what);
        }
    }
}
