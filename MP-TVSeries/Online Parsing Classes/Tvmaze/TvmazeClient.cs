using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.Tvmaze
{
    /// <summary>
    /// Requests to TVmaze (api.tvmaze.com), the backup for shows TMDB doesn't have. TVmaze is free and needs no
    /// key; its information is shared under CC BY-SA, with credit to TVmaze (on the About tab). Replies are read
    /// with MiniJson, like TheTVDB's and TMDB's.
    /// </summary>
    static class TvmazeClient
    {
        const string BaseUrl = "https://api.tvmaze.com";

        // TVmaze allows at least 20 requests every 10 seconds from one address; one every 550 ms stays below that
        const int MinMillisecondsBetweenRequests = 550;
        static readonly object sPaceLock = new object();
        static DateTime sNextRequestAllowed = DateTime.MinValue;

        /// <summary>
        /// GET a TVmaze path (with its query, e.g. "/shows/1?embed=cast") and return the parsed JSON, or null.
        /// notFound is true when TVmaze answered that it doesn't have it (as opposed to not answering at all).
        /// </summary>
        public static object Get(string pathAndQuery, out bool notFound)
        {
            notFound = false;
            string url = BaseUrl + pathAndQuery;

            const int maxAttempts = 4;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                WaitForTurn();
                MPTVSeriesLog.Write("TVmaze: GET " + url, MPTVSeriesLog.LogLevel.Debug);
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)0xc00; // TLS 1.2, as everywhere else in this plugin

                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.Accept = "application/json";
                    request.UserAgent = Settings.UserAgent;
                    request.Timeout = 30000;
                    request.AllowAutoRedirect = true; // a lookup answers with a redirect to the show

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
                        notFound = true;
                        MPTVSeriesLog.Write("TVmaze: not found: " + url, MPTVSeriesLog.LogLevel.Debug);
                        return null;
                    }
                    if ((status == 429 || status >= 500 || status == 0) && attempt < maxAttempts)
                    {
                        int waitSeconds;
                        if (httpResponse == null || !int.TryParse(httpResponse.Headers["Retry-After"], out waitSeconds) || waitSeconds < 1)
                            waitSeconds = status == 429 ? 5 * attempt : 2 * attempt;
                        waitSeconds = Math.Min(waitSeconds, 60);
                        MPTVSeriesLog.Write(string.Format("TVmaze: {0}, trying again in {1} seconds", status == 429 ? "asked to slow down" : "no answer (" + wex.Message + ")", waitSeconds));
                        Thread.Sleep(waitSeconds * 1000);
                        continue;
                    }
                    MPTVSeriesLog.Write("TVmaze: request failed: " + url + " - " + wex.Message);
                    return null;
                }
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write("TVmaze: request failed: " + url + " - " + ex.Message);
                    return null;
                }
            }
            return null;
        }

        public static object Get(string pathAndQuery)
        {
            bool notFound;
            return Get(pathAndQuery, out notFound);
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
