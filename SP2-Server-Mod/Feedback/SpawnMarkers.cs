using System;
using System.Globalization;
using System.Xml.Linq;
using Assets.Scripts.Flight;
using Assets.Scripts.Multiplayer.ActivityFramework;
using UnityEngine;

namespace SP2ServerMod
{
    // Markers in the world (phase F5), for a player who has the mod: before the match starts, the spawns of the player's own team, with the
    // distance, so they can be found from the air. Read from the activity's own data (the StartLocations every player has), drawn with the
    // game's GUI layer over the flight view. Gone as soon as the match starts (everybody is then taken to their spawn).
    internal static class SpawnMarkers
    {
        private static Texture2D ring;
        private static GUIStyle label;

        public static void OnGui()
        {
            if (!SP2ServerModPlugin.SpawnMarkersEnabled.Value || Event.current == null || Event.current.type != EventType.Repaint) return;

            FlightSceneScript scene = FlightSceneScript.Instance;
            FlightScenePlayer player = scene != null ? scene.LocalPlayer : null;
            NetworkedActivityScript activity = player != null ? player.NetworkedActivity : null;
            if (!MatchRules.IsOurs(activity)) return;
            if (activity.State != NetworkedActivityState.Created && activity.State != NetworkedActivityState.Initialized) return;

            NetworkedActivityPlayer member = activity.GetPlayer(player);
            string team = member != null && member.Team != null && member.Team.Id == NetworkedActivityTeamIds.Team2 ? "Team2" : "Team1";
            XElement data = activity.Data != null ? activity.Data.XmlData : null;
            if (data == null) return;

            Camera camera = Assets.Scripts.Flight.Cameras.CameraManagerScript.Instance != null
                ? Assets.Scripts.Flight.Cameras.CameraManagerScript.Instance.MainCamera
                : null;
            if (camera == null) camera = Camera.main;
            if (camera == null) return;

            EnsureStyle();
            Color color = team == "Team2" ? new Color(1f, 0.35f, 0.3f, 0.95f) : new Color(0.35f, 0.7f, 1f, 0.95f);
            Vector3 here = player.GlobalPosition;

            foreach (XElement group in data.Elements("StartLocations"))
            {
                if ((string)group.Attribute("teamId") != team) continue;
                int number = 0;
                foreach (XElement location in group.Elements("Location"))
                {
                    number++;
                    Vector3 position;
                    if (!TryParse((string)location.Attribute("position"), out position)) continue;

                    Vector3 world = Assets.Scripts.Utility.ConvertAbsoluteToFloatingOriginPosition(position);
                    Vector3 screen = camera.WorldToScreenPoint(world);
                    if (screen.z <= 0f) continue;   // behind the camera

                    float distance = Vector3.Distance(here, position);
                    float x = screen.x, y = Screen.height - screen.y;
                    GUI.color = color;
                    GUI.DrawTexture(new Rect(x - 12f, y - 12f, 24f, 24f), ring);
                    label.normal.textColor = color;
                    GUI.Label(new Rect(x - 80f, y + 12f, 160f, 22f), "Spawn " + number + "  " + Distance(distance), label);
                }
            }
            GUI.color = Color.white;
        }

        private static void EnsureStyle()
        {
            if (ring == null)
            {
                const int size = 32;
                ring = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                for (int py = 0; py < size; py++)
                    for (int px = 0; px < size; px++)
                    {
                        float d = Vector2.Distance(new Vector2(px + 0.5f, py + 0.5f), new Vector2(size / 2f, size / 2f));
                        float a = Mathf.Clamp01(1.4f - Mathf.Abs(d - 12.5f)) ;   // a ring about 2 px wide
                        ring.SetPixel(px, py, new Color(1f, 1f, 1f, a));
                    }
                ring.Apply();
            }
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 14, fontStyle = FontStyle.Bold };
            }
        }

        private static string Distance(float meters)
        {
            return meters < 1000f ? meters.ToString("0", CultureInfo.InvariantCulture) + " m" : (meters / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        private static bool TryParse(string text, out Vector3 value)
        {
            value = Vector3.zero;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split(',');
            float x, y, z;
            if (parts.Length != 3 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
            value = new Vector3(x, y, z);
            return true;
        }

        public static void Shutdown()
        {
            if (ring != null) UnityEngine.Object.Destroy(ring);
            ring = null;
            label = null;
        }
    }
}
