using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.Tmdb
{
    /// <summary>
    /// Requests to TMDB (The Movie Database, api.themoviedb.org/3), which is free for non-commercial use with
    /// attribution. Replies are read with MiniJson, like TheTVDB's, so a new field can't break anything.
    ///
    /// The key: a key built into this copy (APIKey.resx, "TmdbKey" - either a short API key or the long
    /// "API Read Access Token" starting with "eyJ"), otherwise the TMDB key MP-TVSeries has always shipped for
    /// its artwork chooser (TmdbAPI.TmdbURIs).
    /// </summary>
    static class TmdbClient
    {
        const string BaseUrl = "https://api.themoviedb.org/3";

        /// <summary>Where TMDB's pictures are; add a size ("w300", "original"...) and the image's file_path.</summary>
        public const string ImageBase = "https://image.tmdb.org/t/p/";

        // TMDB allows roughly 40-50 requests a second; staying well below keeps a big first import polite
        const int MinMillisecondsBetweenRequests = 60;
        static readonly object sPaceLock = new object();
        static DateTime sNextRequestAllowed = DateTime.MinValue;

        static string sBuiltInKey;

        static TmdbClient()
        {
            try
            {
                sBuiltInKey = new System.Resources.ResourceManager("WindowPlugins.GUITVSeries.Online_Parsing_Classes.APIKey", typeof(TmdbClient).Assembly).GetString("TmdbKey");
            }
            catch (Exception)
            {
                sBuiltInKey = null;
            }
        }

        static string Key
        {
            get
            {
                if (!string.IsNullOrEmpty(sBuiltInKey) && !sBuiltInKey.StartsWith("PUT-YOUR")) return sBuiltInKey.Trim();
                return TmdbAPI.TmdbURIs.ApiKey;
            }
        }

        /// <summary>Full address of a TMDB picture, or empty when there is none.</summary>
        public static string ImageUrl(string size, string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            return ImageBase + size + (filePath.StartsWith("/") ? filePath : "/" + filePath);
        }

        /// <summary>True if TMDB answers with this copy's key (used as the "can we download anything" check).</summary>
        public static bool Verify()
        {
            object reply = Get("/configuration");
            bool ok = JsonHelpers.AsObject(reply) != null;
            MPTVSeriesLog.Write(ok ? "TMDB: connected" : "TMDB: could not connect - nothing will be downloaded until it can");
            return ok;
        }

        /// <summary>
        /// GET a TMDB path (with its query, e.g. "/tv/1399?language=en") and return the parsed JSON, or null when TMDB
        /// doesn't have it or couldn't be reached (logged).
        /// </summary>
        public static object Get(string pathAndQuery)
        {
            string key = Key;
            bool bearer = key.StartsWith("eyJ");
            string url = BaseUrl + pathAndQuery;
            if (!bearer) url += (url.IndexOf('?') >= 0 ? "&" : "?") + "api_key=" + Uri.EscapeDataString(key);
            string logUrl = BaseUrl + pathAndQuery;

            const int maxAttempts = 4;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                WaitForTurn();
                MPTVSeriesLog.Write("TMDB: GET " + logUrl, MPTVSeriesLog.LogLevel.Debug);
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)0xc00; // TLS 1.2, as everywhere else in this plugin

                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.Accept = "application/json";
                    request.UserAgent = Settings.UserAgent;
                    request.Timeout = 30000;
                    if (bearer) request.Headers.Add("Authorization", "Bearer " + key);

                    string body;
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        body = reader.ReadToEnd();

                    return MiniJson.Parse(body);
                }
                catch (WebException wex)
                {
                    var httpResponse = wex.Response as HttpWebResponse;
                    int status = httpResponse == null ? 0 : (int)httpResponse.StatusCode;

                    if (status == 404)
                    {
                        MPTVSeriesLog.Write("TMDB: not found: " + logUrl, MPTVSeriesLog.LogLevel.Debug);
                        return null;
                    }
                    if (status == 401)
                    {
                        MPTVSeriesLog.Write("TMDB: the key was refused (401) - " + logUrl);
                        return null;
                    }
                    if ((status == 429 || status >= 500 || status == 0) && attempt < maxAttempts)
                    {
                        int waitSeconds;
                        if (httpResponse == null || !int.TryParse(httpResponse.Headers["Retry-After"], out waitSeconds) || waitSeconds < 1)
                            waitSeconds = 2 * attempt;
                        waitSeconds = Math.Min(waitSeconds, 60);
                        MPTVSeriesLog.Write(string.Format("TMDB: {0}, trying again in {1} seconds", status == 429 ? "asked to slow down" : "no answer (" + wex.Message + ")", waitSeconds));
                        Thread.Sleep(waitSeconds * 1000);
                        continue;
                    }
                    MPTVSeriesLog.Write("TMDB: request failed: " + logUrl + " - " + wex.Message);
                    return null;
                }
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write("TMDB: request failed: " + logUrl + " - " + ex.Message);
                    return null;
                }
            }
            return null;
        }

        static void WaitForTurn()
        {
            TimeSpan wait;
            lock (sPaceLock)
            {
                DateTime now = DateTime.UtcNow;
                DateTime slot = sNextRequestAllowed > now ? sNextRequestAllowed : now;
                sNextRequestAllowed = slot.AddMilliseconds(MinMillisecondsBetweenRequests);
                wait = slot - now;
            }
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }
    }
}
