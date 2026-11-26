using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Craft;
using Assets.Scripts.Craft.Parts;
using Assets.Scripts.Flight;
using Assets.Scripts.Multiplayer;
using FishNet.Serializing;
using HarmonyLib;
using UnityEngine;

namespace SP2ServerMod
{
    // Who has the mod. A player with the mod tells the others "I run the mod" every few seconds (HELLO); the host keeps who did, so it can
    // treat the others as players without the mod (the craft link in the chat, for example, goes only to them).
    //
    // The message rides on the game's own "part network message" channel (the one magnets and targeting pods use): the game delivers it to
    // every other player and skips message types it does not know, so players without the mod ignore it. (The Armor Mod does the same with types
    // 200 to 203; this mod's type is 210, and the two mods' patches on the channel let each other's messages through.)
    internal static class ModHello
    {
        public const byte Message = 210;

        private const float CheckInterval = 1f;
        private const float HeartbeatInterval = 5f;    // how often a client repeats its hello
        private const float ModdedTimeout = 15f;       // a hello is trusted for this long

        private static readonly Dictionary<int, float> ModdedUntil = new Dictionary<int, float>();
        private static float nextCheck;
        private static float nextHeartbeat;
        private static bool failureLogged;

        // Longest wait for a hello before a player is taken as one without the mod: one heartbeat plus a margin for the way over.
        public const float GraceSeconds = HeartbeatInterval + 2f;

        // The local player, and every player whose hello arrived recently.
        public static bool IsModded(int playerId)
        {
            try
            {
                FlightScenePlayer local = FlightSceneScript.Instance != null ? FlightSceneScript.Instance.LocalPlayer : null;
                if (local != null && local.NetworkPlayer != null && local.NetworkPlayer.PlayerId == playerId) return true;
            }
            catch (Exception)
            {
                // the scene is going away
            }

            float until;
            return ModdedUntil.TryGetValue(playerId, out until) && Time.unscaledTime < until;
        }

        public static int ModdedCount
        {
            get { return ModdedUntil.Count(pair => Time.unscaledTime < pair.Value); }
        }

        // Every frame. A client (not the server) says hello; the server has nothing to say: it is the one that listens.
        public static void Tick()
        {
            try
            {
                if (Time.unscaledTime < nextCheck) return;
                nextCheck = Time.unscaledTime + CheckInterval;

                FlightSceneScript scene = FlightSceneScript.Instance;
                FlightSceneNetworkScript net = scene != null ? scene.FlightSceneNetwork : null;
                if (net == null || !net.IsClientStarted || net.IsServerStarted) return;
                if (scene.AllPlayers.Count() < 2) return;   // nobody to tell

                if (Time.unscaledTime >= nextHeartbeat && Send(SP2ServerModPlugin.Version))
                    nextHeartbeat = Time.unscaledTime + HeartbeatInterval;
            }
            catch (Exception e)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    SP2ServerModPlugin.Log.LogWarning("The hello to the host failed (shown once): " + e);
                }
            }
        }

        // The message rides on one of the local player's own parts. It needs a craft, so a player with none yet says hello a little later.
        private static bool Send(string payload)
        {
            FlightScenePlayer player = FlightSceneScript.Instance.LocalPlayer;
            AircraftScript aircraft = player != null ? player.Aircraft : null;
            INetworkAircraft network = aircraft != null ? aircraft.NetworkAircraft : null;
            if (network == null || !network.IsInitialized || !network.IsOwner) return false;
            if (aircraft.Parts == null || aircraft.Parts.Count == 0) return false;

            PartData part = aircraft.Parts[0];
            network.SendPartNetworkMessage(Message, part, (PooledWriter w) => w.WriteString(payload));
            return true;
        }

        // A hello arrived from this player's craft.
        public static void OnMessage(NetworkAircraftScript sender)
        {
            ModdedUntil[sender.PlayerId] = Time.unscaledTime + ModdedTimeout;
        }

        public static void Shutdown()
        {
            ModdedUntil.Clear();
        }
    }

    // The game hands every part message to the part it names. Ours names no useful part, so it is taken here first and never reaches the game's
    // own handling. Any other type goes on untouched (to the game and to the Armor Mod's own patch of this method).
    [HarmonyPatch(typeof(NetworkAircraftScript), "ProcessPartNetworkMessage")]
    internal static class HelloMessagePatch
    {
        private static bool Prefix(NetworkAircraftScript __instance, ArraySegment<byte> data)
        {
            // Message layout: type (1 byte), part id (2 bytes), then what the sender wrote.
            if (data.Array == null || data.Count < 3) return true;
            if (data.Array[data.Offset] != ModHello.Message) return true;

            try
            {
                ModHello.OnMessage(__instance);
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogWarning("Could not read a hello: " + e.Message);
            }
            return false;
        }
    }
}
