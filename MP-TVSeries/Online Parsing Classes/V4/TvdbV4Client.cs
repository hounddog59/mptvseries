using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4
{
    /// <summary>
    /// Client for TheTVDB API v4 (https://api4.thetvdb.com/v4).
    ///
    /// Auth model (confirmed against TVDB's own docs, not the old v3 scheme):
    ///   POST /login  body: { "apikey": "...", "pin": "..." }  (omit "pin" entirely if you
    ///   don't have a subscriber PIN)
    ///   -> { "status": "success", "data": { "token": "<JWT>" } }
    ///   Token is valid for ~1 month; send as "Authorization: Bearer <token>" on every
    ///   subsequent call. We re-login automatically on a 401.
    ///
    /// NOTE: this environment has no network access to api4.thetvdb.com, so none of this
    /// has been exercised against the live API. The plumbing (login, bearer auth, retry,
    /// pagination) follows TVDB's documented contract, but double-check the very first
    /// live run against the MPTVSeriesLog debug output before relying on it.
    /// </summary>
    static class TvdbV4Client
    {
        public const string BaseUrl = "https://api4.thetvdb.com/v4";

        static string sToken = null;
        static string sTokenFor = null; // key|PIN the token was issued for
        static DateTime sTokenObtainedAt = DateTime.MinValue;
        static readonly object sLock = new object();

        // Re-login proactively a little before the documented 1-month expiry.
        static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(25);

        /// <summary>
        /// Ensures we have a valid bearer token, logging in if we don't have one yet or it's
        /// likely expired. Thread-safe (worker_DoWork spins up background threads).
        /// </summary>
        static void EnsureLoggedIn(string apiKey, string pin, bool forceRelogin)
        {
            lock (sLock)
            {
                string loginFor = (apiKey ?? "") + "|" + (pin ?? "");
                bool needsLogin = forceRelogin
                                   || string.IsNullOrEmpty(sToken)
                                   || loginFor != sTokenFor // the key or PIN was changed in Configuration
                                   || (DateTime.UtcNow - sTokenObtainedAt) > TokenLifetime;

                if (!needsLogin) return;

                MPTVSeriesLog.Write("TVDB v4: logging in" + (forceRelogin ? " (forced re-login after 401)" : ""), MPTVSeriesLog.LogLevel.Debug);

                var body = new StringBuilder();
                body.Append('{');
                body.Append("\"apikey\":\"").Append(JsonEscape(apiKey)).Append('"');
                if (!string.IsNullOrEmpty(pin))
                {
                    body.Append(",\"pin\":\"").Append(JsonEscape(pin)).Append('"');
                }
                body.Append('}');

                string responseJson;
                try
                {
                    responseJson = PostRaw(BaseUrl + "/login", body.ToString());
                }
                catch (WebException ex)
                {
                    MPTVSeriesLog.Write("TVDB v4: login request failed: " + ex.Message);
                    throw;
                }

                var parsed = JsonHelpers.AsObject(MiniJson.Parse(responseJson));
                var data = JsonHelpers.AsObject(JsonHelpers.Get(parsed, "data"));
                string token = JsonHelpers.GetString(data, "token");

                if (string.IsNullOrEmpty(token))
                {
                    MPTVSeriesLog.Write("TVDB v4: login response did not contain a token. Raw response: " + responseJson);
                    throw new ApplicationException("TVDB v4 login failed - no token in response");
                }

                sToken = token;
                sTokenFor = loginFor;
                sTokenObtainedAt = DateTime.UtcNow;
                MPTVSeriesLog.Write("TVDB v4: login successful", MPTVSeriesLog.LogLevel.Debug);
            }
        }

        static string JsonEscape(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// Attempts a login and returns whether it succeeded. Used to answer the
        /// "is TheTVDB reachable/are we configured correctly" question that
        /// DBOnlineMirror.IsMirrorsAvailable represents elsewhere in the plugin - v4 has no
        /// "mirrors" to check anymore, so a successful login is the closest equivalent.
        /// </summary>
        public static bool VerifyLogin(string apiKey, string pin)
        {
            try
            {
                EnsureLoggedIn(apiKey, pin, false);
                return !string.IsNullOrEmpty(sToken);
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("TVDB v4: login verification failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// GETs a path (e.g. "/search?query=lost&type=series") and returns the parsed
        /// "data" node (the envelope's "status"/"data" wrapper is unwrapped here).
        /// </summary>
        public static object GetData(string apiKey, string pin, string pathAndQuery)
        {
            EnsureLoggedIn(apiKey, pin, false);

            string url = pathAndQuery.StartsWith("http") ? pathAndQuery : BaseUrl + pathAndQuery;
            string json;
            try
            {
                json = GetRaw(url, sToken);
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // token expired/invalid - relogin once and retry
                    EnsureLoggedIn(apiKey, pin, true);
                    json = GetRaw(url, sToken);
                }
                else
                {
                    MPTVSeriesLog.Write("TVDB v4: GET failed for '" + url + "': " + ex.Message);
                    return null;
                }
            }

            var parsed = JsonHelpers.AsObject(MiniJson.Parse(json));
            if (parsed == null) return null;

            string status = JsonHelpers.GetString(parsed, "status");
            if (!string.IsNullOrEmpty(status) && status != "success")
            {
                string message = JsonHelpers.GetString(parsed, "message");
                MPTVSeriesLog.Write("TVDB v4: API returned status='" + status + "' for '" + url + "'. Message: " + message);
                return null;
            }

            return JsonHelpers.Get(parsed, "data");
        }

        /// <summary>
        /// Follows the "links.next" pagination pattern used by list endpoints (e.g.
        /// /series/{id}/episodes/{season-type}/{lang}), returning every page's "episodes"
        /// (or whichever array key you specify) concatenated together.
        /// </summary>
        public static List<object> GetAllPages(string apiKey, string pin, string firstPathAndQuery, string arrayKey)
        {
            var results = new List<object>();
            string nextPath = firstPathAndQuery;
            int safetyLimit = 200; // avoid an infinite loop if "links.next" ever misbehaves

            while (!string.IsNullOrEmpty(nextPath) && safetyLimit-- > 0)
            {
                EnsureLoggedIn(apiKey, pin, false);
                string url = nextPath.StartsWith("http") ? nextPath : BaseUrl + nextPath;

                string json;
                try
                {
                    json = GetRaw(url, sToken);
                }
                catch (WebException ex)
                {
                    var resp = ex.Response as HttpWebResponse;
                    if (resp != null && resp.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        EnsureLoggedIn(apiKey, pin, true);
                        json = GetRaw(url, sToken);
                    }
                    else
                    {
                        MPTVSeriesLog.Write("TVDB v4: paged GET failed for '" + url + "': " + ex.Message);
                        break;
                    }
                }

                var parsed = JsonHelpers.AsObject(MiniJson.Parse(json));
                var data = JsonHelpers.AsObject(JsonHelpers.Get(parsed, "data"));

                // Some list endpoints put the array directly under "data", others nest it
                // under a named key (e.g. { "data": { "episodes": [...] } }).
                List<object> page = JsonHelpers.GetArray(data, arrayKey);
                if (page == null)
                {
                    // data itself might already be the array
                    page = JsonHelpers.AsArray(JsonHelpers.Get(parsed, "data"));
                }
                if (page != null) results.AddRange(page);

                var links = JsonHelpers.AsObject(JsonHelpers.Get(parsed, "links"));
                string next = JsonHelpers.GetString(links, "next");
                nextPath = string.IsNullOrEmpty(next) ? null : next;
            }

            return results;
        }

        /// <summary>
        /// POSTs a JSON body to a path (e.g. "/user/favorites") while logged in. Returns whether TheTVDB accepted it.
        /// </summary>
        public static bool PostData(string apiKey, string pin, string path, string jsonBody)
        {
            string url = BaseUrl + path;
            try
            {
                EnsureLoggedIn(apiKey, pin, false);
                try
                {
                    PostRaw(url, jsonBody, sToken);
                }
                catch (WebException ex)
                {
                    var resp = ex.Response as HttpWebResponse;
                    if (resp == null || resp.StatusCode != HttpStatusCode.Unauthorized) throw;
                    // token expired/invalid - relogin once and retry
                    EnsureLoggedIn(apiKey, pin, true);
                    PostRaw(url, jsonBody, sToken);
                }
                return true;
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("TVDB v4: POST failed for '" + url + "': " + ex.Message);
                return false;
            }
        }

        static string GetRaw(string url, string bearerToken)
        {
            MPTVSeriesLog.Write("TVDB v4: GET " + url, MPTVSeriesLog.LogLevel.Debug);
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)0xc00; // TLS 1.2, matches rest of the plugin

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "application/json";
            request.UserAgent = Settings.UserAgent;
            request.Timeout = 60000;
            if (!string.IsNullOrEmpty(bearerToken))
                request.Headers.Add("Authorization", "Bearer " + bearerToken);

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        static string PostRaw(string url, string jsonBody, string bearerToken = null)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)0xc00;

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.UserAgent = Settings.UserAgent;
            request.Timeout = 60000;
            if (!string.IsNullOrEmpty(bearerToken))
                request.Headers.Add("Authorization", "Bearer " + bearerToken);

            byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
            request.ContentLength = bytes.Length;
            using (var requestStream = request.GetRequestStream())
            {
                requestStream.Write(bytes, 0, bytes.Length);
            }

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
