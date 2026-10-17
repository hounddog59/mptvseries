using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4;

namespace WindowPlugins.GUITVSeries
{
    /// <summary>
    /// The Trakt.tv calls MP-TVSeries needs for community ratings, made directly rather than through
    /// TraktAPI.dll. That library maps every reply onto fixed classes, and when Trakt's replies grow new
    /// fields it fails to read them and quietly returns nothing - which is what made every rating lookup
    /// fail, even though Trakt was answering correctly. Here replies are read with MiniJson, which takes the
    /// fields it needs and ignores the rest, so new fields from Trakt can't break it.
    ///
    /// Everything used here is public data, so only a client ID is required. If the user has authorized
    /// MP-TVSeries (see TraktPkceAuth) that token is sent too, paired with MP-TVSeries' own client ID.
    /// TraktAPI.dll's settings are never changed here, so the Trakt plugin itself is unaffected.
    /// </summary>
    public static class TraktRatingsClient
    {
        private const string ApiBase = "https://api.trakt.tv";

        // Trakt allows 1,000 GET requests every 5 minutes (one request per 300 ms on average) - per user when
        // authorized, but per APPLICATION when not, i.e. shared by everyone using this client ID without
        // authorizing. Spacing requests evenly keeps a full first run (a few thousand requests) under the limit
        // instead of racing into it and then sitting in long waits.
        private const int MinMillisecondsBetweenRequests = 320;
        private static readonly object paceLock = new object();
        private static DateTime nextRequestAllowed = DateTime.MinValue;

        public class Rating
        {
            public double Value;
            public int Votes;
        }

        public class SeasonInfo
        {
            public int Number;
            public double Rating;
            public int Votes;
            public string Overview;
            public Dictionary<int, Rating> Episodes = new Dictionary<int, Rating>();
        }

        /// <summary>Trakt's ID for the show with this TheTVDB ID, or null if Trakt doesn't have it (or couldn't be read).</summary>
        public static string FindTraktIdByTvdbId(string tvdbId, out string failureReason)
        {
            failureReason = null;
            int wanted;
            if (!int.TryParse(tvdbId, out wanted))
            {
                failureReason = "invalid TVDb ID";
                return null;
            }

            var results = JsonHelpers.AsArray(Get("/search/tvdb/" + Uri.EscapeDataString(tvdbId) + "?type=show", out failureReason));
            if (results == null) return null;

            foreach (object item in results)
            {
                var result = JsonHelpers.AsObject(item);
                if (result == null || JsonHelpers.GetString(result, "type") != "show") continue;

                var ids = JsonHelpers.AsObject(JsonHelpers.Get(JsonHelpers.AsObject(JsonHelpers.Get(result, "show")), "ids"));
                if (ids == null || JsonHelpers.GetInt(ids, "tvdb") != wanted) continue;

                string traktId = JsonHelpers.GetString(ids, "trakt");
                if (!string.IsNullOrEmpty(traktId)) return traktId;
            }

            failureReason = results.Count == 0 ? "not on Trakt" : "no matching show among " + results.Count + " results";
            return null;
        }

        /// <summary>The show's overall community rating, or null if it couldn't be fetched.</summary>
        public static Rating GetShowRating(string traktId, out string failureReason)
        {
            var obj = JsonHelpers.AsObject(Get("/shows/" + Uri.EscapeDataString(traktId) + "/ratings", out failureReason));
            return obj == null ? null : ReadRating(obj);
        }

        /// <summary>Every season with its rating, overview and per-episode ratings, keyed by season number; null on failure.</summary>
        public static Dictionary<int, SeasonInfo> GetSeasons(string traktId, out string failureReason)
        {
            var seasons = JsonHelpers.AsArray(Get("/shows/" + Uri.EscapeDataString(traktId) + "/seasons?extended=episodes,full", out failureReason));
            if (seasons == null) return null;

            var byNumber = new Dictionary<int, SeasonInfo>();
            foreach (object item in seasons)
            {
                var s = JsonHelpers.AsObject(item);
                if (s == null) continue;

                var rating = ReadRating(s);
                var info = new SeasonInfo
                {
                    Number = JsonHelpers.GetInt(s, "number"),
                    Rating = rating.Value,
                    Votes = rating.Votes,
                    Overview = JsonHelpers.GetString(s, "overview")
                };

                var episodes = JsonHelpers.GetArray(s, "episodes");
                if (episodes != null)
                {
                    foreach (object e in episodes)
                    {
                        var ep = JsonHelpers.AsObject(e);
                        if (ep != null)
                            info.Episodes[JsonHelpers.GetInt(ep, "number")] = ReadRating(ep);
                    }
                }

                byNumber[info.Number] = info;
            }
            return byNumber;
        }

        /// <summary>
        /// Trakt IDs of shows updated since the given date, with when each was updated - so unchanged shows can be
        /// skipped. Returns null if the list couldn't be fetched, in which case every show should be checked.
        /// </summary>
        public static Dictionary<string, string> GetShowsUpdatedSince(DateTime sinceUtc)
        {
            const int pageSize = 1000;
            var updated = new Dictionary<string, string>();
            string date = sinceUtc.ToString("yyyy-MM-dd");

            for (int page = 1; page <= 50; page++)
            {
                MPTVSeriesLog.Write(string.Format("Requesting list of recently updated series from trakt.tv, Page = '{0}', Since = '{1}'", page, date));

                string failureReason;
                var items = JsonHelpers.AsArray(Get(string.Format("/shows/updates/{0}?page={1}&limit={2}", date, page, pageSize), out failureReason));
                if (items == null)
                {
                    MPTVSeriesLog.Write("Could not get the list of recently updated series from trakt.tv (" + failureReason + "), checking every series instead");
                    return null;
                }

                foreach (object item in items)
                {
                    var u = JsonHelpers.AsObject(item);
                    var ids = JsonHelpers.AsObject(JsonHelpers.Get(JsonHelpers.AsObject(JsonHelpers.Get(u, "show")), "ids"));
                    string traktId = ids == null ? null : JsonHelpers.GetString(ids, "trakt");
                    if (!string.IsNullOrEmpty(traktId))
                        updated[traktId] = JsonHelpers.GetString(u, "updated_at");
                }

                if (items.Count < pageSize) break;
            }
            return updated;
        }

        /// <summary>Blocks until this request may go out, keeping requests at least MinMillisecondsBetweenRequests apart.</summary>
        private static void WaitForTurn()
        {
            TimeSpan wait;
            lock (paceLock)
            {
                DateTime now = DateTime.UtcNow;
                DateTime slot = nextRequestAllowed > now ? nextRequestAllowed : now;
                nextRequestAllowed = slot.AddMilliseconds(MinMillisecondsBetweenRequests);
                wait = slot - now;
            }
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }

        private static Rating ReadRating(Dictionary<string, object> obj)
        {
            object value = JsonHelpers.Get(obj, "rating");
            return new Rating
            {
                Value = value is double ? (double)value : 0.0,
                Votes = JsonHelpers.GetInt(obj, "votes")
            };
        }

        /// <summary>
        /// GET request returning the parsed JSON, or null with a reason. Waits and retries when Trakt asks it to
        /// slow down (a full first run makes a few thousand requests, which can reach Trakt's rate limit).
        /// </summary>
        private static object Get(string pathAndQuery, out string failureReason)
        {
            failureReason = null;

            const int maxAttempts = 4;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                WaitForTurn();

                string clientId, accessToken;
                TraktPkceAuth.GetRequestCredentials(out clientId, out accessToken);

                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)0xc00; // TLS 1.2, as everywhere else in this plugin

                    var request = (HttpWebRequest)WebRequest.Create(ApiBase + pathAndQuery);
                    request.Method = "GET";
                    request.Accept = "application/json";
                    request.UserAgent = "MP-TVSeries";
                    request.Timeout = 30000;
                    request.Headers.Add("trakt-api-version", "2");
                    request.Headers.Add("trakt-api-key", clientId);
                    if (!string.IsNullOrEmpty(accessToken))
                        request.Headers.Add("Authorization", "Bearer " + accessToken);

                    string body;
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new System.IO.StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        body = reader.ReadToEnd();

                    try
                    {
                        return MiniJson.Parse(body);
                    }
                    catch (Exception ex)
                    {
                        failureReason = "reply could not be read: " + ex.Message;
                        return null;
                    }
                }
                catch (WebException wex)
                {
                    var httpResponse = wex.Response as HttpWebResponse;
                    int status = httpResponse == null ? 0 : (int)httpResponse.StatusCode;

                    if (status == 429 && attempt < maxAttempts)
                    {
                        int waitSeconds;
                        if (!int.TryParse(httpResponse.Headers["Retry-After"], out waitSeconds) || waitSeconds < 1)
                            waitSeconds = 10;
                        // a full 5-minute window at most; Trakt says how long, so wait that long rather than retry early
                        waitSeconds = Math.Min(waitSeconds, 310);
                        MPTVSeriesLog.Write(string.Format("trakt.tv asked to slow down, waiting {0} seconds", waitSeconds));
                        Thread.Sleep(waitSeconds * 1000);
                        continue;
                    }

                    // our own token was rejected: renew it once and try again
                    if (status == 401 && attempt < maxAttempts && TraktPkceAuth.TryRenewOwnToken())
                        continue;

                    failureReason = status == 404 ? "not found on Trakt" : (status == 0 ? wex.Message : "HTTP " + status + " " + wex.Message);
                    return null;
                }
                catch (Exception ex)
                {
                    failureReason = ex.Message;
                    return null;
                }
            }

            failureReason = "trakt.tv kept asking to slow down";
            return null;
        }
    }
}
