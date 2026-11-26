using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Assets.Scripts.Net;

namespace SP2ServerMod
{
    // Crafts published on the SimplePlanes website. The game itself downloads a craft by its id with
    // Game.GetDownloadAircraftUrl(id) and the game's WebRequest, and reads the bytes with Utility.LoadCraftXmlFromBytes: this does the same without
    // the game's dialog (the answer "0" means there is no such craft). The craft is saved in the mod's crafts folder as site-<id>.xml, so after
    // the first download it is a file like any other (and its hash is read from that file).
    internal static class SiteCrafts
    {
        private const float TimeoutSeconds = 60f;

        private sealed class Job
        {
            public string Id;
            public WebRequest Request;
            public float Started;
            public Action<CraftChoice, string> Done;   // the craft (File, Hash, Label and Site filled in), or the error
        }

        private static readonly List<Job> Jobs = new List<Job>();

        private static readonly Regex FromLink = new Regex(@"(?:/a/|/Feedback/View/)([a-zA-Z0-9]+)", RegexOptions.Compiled);
        private static readonly Regex PlainId = new Regex(@"^[a-zA-Z0-9]+$", RegexOptions.Compiled);

        // The id inside a link of the website (https://www.simpleplanes.com/a/AbC123/Name), or the text itself when it already is an id.
        // Anything else is an empty string. (Only letters and digits survive, so the id is safe to use in a file name.)
        public static string ParseId(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = text.Trim();
            Match link = FromLink.Match(text);
            if (link.Success) return link.Groups[1].Value;
            return PlainId.IsMatch(text) ? text : "";
        }

        public static string FileFor(string id)
        {
            return "site-" + id + ".xml";
        }

        public static bool IsDownloading(string id)
        {
            foreach (Job job in Jobs)
                if (job.Id == id) return true;
            return false;
        }

        // Starts the download; done runs later, from Tick, with the file name and hash (or an error message).
        public static void Start(string id, Action<CraftChoice, string> done)
        {
            foreach (Job running in Jobs)
            {
                if (running.Id != id) continue;
                Action<CraftChoice, string> previous = running.Done;
                running.Done = (c, e) =>
                {
                    previous(c, e);
                    done(c, e);
                };
                return;
            }

            try
            {
                string url = Assets.Scripts.Game.GetDownloadAircraftUrl(id, 0);
                Jobs.Add(new Job { Id = id, Request = WebRequest.Get(url), Started = UnityEngine.Time.unscaledTime, Done = done });
                SP2ServerModPlugin.Log.LogInfo("Downloading craft '" + id + "' from the website");
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("The download of craft '" + id + "' could not start: " + e.Message);
                done(null, "The download could not start (see the log)");
            }
        }

        // Every frame: the finished downloads are saved and reported.
        public static void Tick()
        {
            if (Jobs.Count == 0) return;

            for (int i = Jobs.Count - 1; i >= 0; i--)
            {
                Job job = Jobs[i];
                string error = null;
                CraftChoice result = null;
                bool finished = false;

                try
                {
                    if (job.Request.IsDone)
                    {
                        finished = true;
                        if (job.Request.HasError)
                        {
                            error = "The download failed. Check your internet connection.";
                            SP2ServerModPlugin.Log.LogWarning("Craft '" + job.Id + "' download error: " + job.Request.Error);
                        }
                        else
                        {
                            byte[] bytes = job.Request.Bytes;
                            if (bytes == null || bytes.Length == 0 || (bytes.Length <= 3 && job.Request.Text == "0"))
                                error = "There is no craft '" + job.Id + "' on the website.";
                            else
                            {
                                XElement craft = Assets.Scripts.Utility.LoadCraftXmlFromBytes(bytes);
                                if (craft == null) error = "The craft '" + job.Id + "' from the website could not be read.";
                                else
                                {
                                    string saveError;
                                    if (!CraftProvider.Store(craft, FileFor(job.Id), out result, out saveError)) error = saveError;
                                    else
                                    {
                                        result.Site = job.Id;
                                        SP2ServerModPlugin.Log.LogInfo("Craft '" + job.Id + "' downloaded and saved as " + result.File);
                                    }
                                }
                            }
                        }
                    }
                    else if (UnityEngine.Time.unscaledTime - job.Started > TimeoutSeconds)
                    {
                        finished = true;
                        job.Request.IsCanceled = true;
                        error = "The download of craft '" + job.Id + "' took too long.";
                    }
                }
                catch (Exception e)
                {
                    finished = true;
                    error = "The download of craft '" + job.Id + "' failed (see the log).";
                    SP2ServerModPlugin.Log.LogError("Craft '" + job.Id + "' download: " + e);
                }

                if (!finished) continue;
                Jobs.RemoveAt(i);
                try
                {
                    job.Done(result, error);
                }
                catch (Exception e)
                {
                    SP2ServerModPlugin.Log.LogError("Craft '" + job.Id + "' download callback: " + e);
                }
            }
        }

        // The plugin is unloading: downloads in flight are dropped.
        public static void Shutdown()
        {
            Jobs.Clear();
        }
    }
}
