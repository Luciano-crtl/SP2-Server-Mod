using System;
using System.Collections.Generic;
using Assets.Scripts.Flight;
using UnityEngine;
using UnityEngine.Rendering;

namespace SP2ServerMod
{
    // A tall vertical beam at a spawn, to find it from far away (and to see exactly where it is). One beam per spawn, switched on and off from the
    // mod's menu. Drawn with a LineRenderer in world space, repositioned every frame from the spawn's absolute coordinates (the game moves its
    // world around the player: "floating origin"), and thickened with the distance so it stays visible from kilometers.
    internal static class SpawnBeams
    {
        private sealed class Beam
        {
            public GameObject Object;
            public LineRenderer Line;
        }

        private static readonly string[] ShaderNames =
        {
            "Sprites/Default",
            "Hidden/Internal-Colored",
            "Unlit/Color",
            "Universal Render Pipeline/Unlit",
            "UI/Default",
        };

        private static readonly Dictionary<string, Beam> On = new Dictionary<string, Beam>();
        private static Material material;
        private static bool shaderSearched;
        private static bool noShaderLogged;

        public static int Count
        {
            get { return On.Count; }
        }

        // Every spawn of every loaded match has a beam on.
        public static bool AllOn
        {
            get
            {
                int total = 0;
                foreach (MatchDefinition match in MatchRegistry.Loaded)
                    foreach (TeamDefinition team in match.Teams)
                        for (int i = 0; i < team.Spawns.Count; i++)
                        {
                            total++;
                            if (!On.ContainsKey(KeyOf(match, team, i))) return false;
                        }
                return total > 0;
            }
        }

        public static string KeyOf(MatchDefinition match, TeamDefinition team, int index)
        {
            return match.Id + "|" + team.Number + "|" + index;
        }

        public static bool IsOn(string key)
        {
            return On.ContainsKey(key);
        }

        public static void Set(string key, bool on)
        {
            if (on)
            {
                if (!On.ContainsKey(key)) On[key] = new Beam();
            }
            else
            {
                Beam beam;
                if (On.TryGetValue(key, out beam))
                {
                    Destroy(beam);
                    On.Remove(key);
                }
            }
        }

        // A spawn was deleted: its beam goes, and the beams of the spawns after it (numbered one lower now) stay with their own spawns.
        public static void RemoveSpawn(MatchDefinition match, TeamDefinition team, int index)
        {
            List<KeyValuePair<string, Beam>> kept = new List<KeyValuePair<string, Beam>>();
            foreach (KeyValuePair<string, Beam> pair in On)
            {
                string[] parts = pair.Key.Split('|');
                int teamNumber, number;
                if (parts.Length == 3 && parts[0] == match.Id && int.TryParse(parts[1], out teamNumber) && teamNumber == team.Number &&
                    int.TryParse(parts[2], out number))
                {
                    if (number == index)
                    {
                        Destroy(pair.Value);
                        continue;
                    }
                    if (number > index)
                    {
                        kept.Add(new KeyValuePair<string, Beam>(match.Id + "|" + team.Number + "|" + (number - 1), pair.Value));
                        continue;
                    }
                }
                kept.Add(pair);
            }
            On.Clear();
            foreach (KeyValuePair<string, Beam> pair in kept) On[pair.Key] = pair.Value;
        }

        // A match was deleted: all its beams go.
        public static void RemoveMatch(string matchId)
        {
            List<string> keys = new List<string>();
            foreach (string key in On.Keys)
                if (key.StartsWith(matchId + "|", StringComparison.Ordinal)) keys.Add(key);
            foreach (string key in keys) Set(key, false);
        }

        public static void SetAll(bool on)
        {
            if (!on)
            {
                foreach (Beam beam in On.Values) Destroy(beam);
                On.Clear();
                return;
            }
            foreach (MatchDefinition match in MatchRegistry.Loaded)
                foreach (TeamDefinition team in match.Teams)
                    for (int i = 0; i < team.Spawns.Count; i++)
                        Set(KeyOf(match, team, i), true);
        }

        // Every frame. In a flight the beams that are on exist and follow their spawns; outside a flight they stay "on" and come back with the next one.
        public static void Tick()
        {
            if (On.Count == 0) return;
            FlightSceneScript scene = FlightSceneScript.Instance;
            if (scene == null) return;

            Camera camera = Assets.Scripts.Flight.Cameras.CameraManagerScript.Instance != null
                ? Assets.Scripts.Flight.Cameras.CameraManagerScript.Instance.MainCamera
                : null;
            if (camera == null) camera = Camera.main;
            Vector3 viewer = camera != null ? camera.transform.position : Vector3.zero;
            float height = SP2ServerModPlugin.BeamHeight.Value;

            List<string> gone = null;
            foreach (KeyValuePair<string, Beam> pair in On)
            {
                SpawnPoint spawn;
                Color color;
                if (!Find(pair.Key, out spawn, out color))
                {
                    if (gone == null) gone = new List<string>();
                    gone.Add(pair.Key);   // the match or the spawn no longer exists (the file was edited)
                    continue;
                }

                Beam beam = pair.Value;
                if (beam.Line == null && !Create(beam, color)) continue;

                Vector3 ground = Assets.Scripts.Utility.ConvertAbsoluteToFloatingOriginPosition(spawn.Position);
                beam.Line.SetPosition(0, ground + Vector3.down * 3f);
                beam.Line.SetPosition(1, ground + Vector3.up * height);

                float width = Mathf.Max(0.6f, Vector3.Distance(viewer, ground) * 0.0035f);
                beam.Line.startWidth = width;
                beam.Line.endWidth = width;
            }

            if (gone != null)
                foreach (string key in gone)
                    Set(key, false);
        }

        // The plugin is being unloaded: every beam and the material go.
        public static void Shutdown()
        {
            foreach (Beam beam in On.Values) Destroy(beam);
            On.Clear();
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
            shaderSearched = false;
        }

        private static bool Find(string key, out SpawnPoint spawn, out Color color)
        {
            spawn = null;
            color = Color.white;
            string[] parts = key.Split('|');
            if (parts.Length != 3) return false;
            int teamNumber, index;
            if (!int.TryParse(parts[1], out teamNumber) || !int.TryParse(parts[2], out index)) return false;

            foreach (MatchDefinition match in MatchRegistry.Loaded)
            {
                if (match.Id != parts[0]) continue;
                TeamDefinition team = match.Team(teamNumber);
                if (team == null || index < 0 || index >= team.Spawns.Count) return false;
                spawn = team.Spawns[index];
                color = ColorOf(match, teamNumber);
                return true;
            }
            return false;
        }

        // Team 1 blue, team 2 red, a free-for-all match orange.
        public static Color ColorOf(MatchDefinition match, int team)
        {
            if (match.Mode == MatchMode.FreeForAll) return new Color(1f, 0.7f, 0.1f, 1f);
            return team == 2 ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(0.2f, 0.6f, 1f, 1f);
        }

        private static bool Create(Beam beam, Color color)
        {
            if (!EnsureMaterial()) return false;

            GameObject go = new GameObject("SP2ServerModSpawnBeam");
            int layer = LayerMask.NameToLayer("Mod Layer 1");   // an empty layer the game's main camera draws
            go.layer = layer >= 0 ? layer : 0;

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.startColor = new Color(color.r, color.g, color.b, 0.95f);   // opaque at the foot, fading toward the sky
            line.endColor = new Color(color.r, color.g, color.b, 0.12f);

            beam.Object = go;
            beam.Line = line;
            return true;
        }

        private static bool EnsureMaterial()
        {
            if (material != null) return true;
            if (shaderSearched) return false;
            shaderSearched = true;
            foreach (string name in ShaderNames)
            {
                Shader shader = Shader.Find(name);
                if (shader == null) continue;
                material = new Material(shader);
                material.color = Color.white;   // the beam's own color comes from the line
                SP2ServerModPlugin.Log.LogInfo("Spawn beams use shader '" + name + "'");
                return true;
            }
            if (!noShaderLogged)
            {
                noShaderLogged = true;
                SP2ServerModPlugin.Log.LogWarning("Spawn beams cannot be drawn: none of the tried shaders exists in this build");
            }
            return false;
        }

        private static void Destroy(Beam beam)
        {
            if (beam != null && beam.Object != null) UnityEngine.Object.Destroy(beam.Object);
            if (beam != null)
            {
                beam.Object = null;
                beam.Line = null;
            }
        }
    }
}
