using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4;

namespace WindowPlugins.GUITVSeries
{
    /// <summary>
    /// Lets a user authorize MP-TVSeries on their own Trakt.tv account, and holds the client ID MP-TVSeries
    /// uses for its own Trakt requests (see TraktRatingsClient).
    ///
    /// Uses Trakt's device flow (the one Trakt recommends for media centers) with PKCE: the app shows a short
    /// code, the user enters it at trakt.tv/activate on any device, and the app waits for the approval. No
    /// client secret is involved anywhere, which is what makes this safe for a publicly distributed build - a
    /// secret is meant to stay private to one developer, whereas the client ID can be shared by every install
    /// while each user authorizes their own account. No redirect address is involved either.
    ///
    /// Community ratings are public data and work without authorization; when the user has authorized, their
    /// token is sent along too. A Trakt token only works with the client ID that issued it, so the token stored
    /// here is always used together with <see cref="ClientId"/>.
    ///
    /// Users (or forks) can use their own application, registered at trakt.tv/oauth/applications, by entering its
    /// client ID in Configuration; otherwise <see cref="DefaultClientId"/> is used. Register it with the redirect URI below, which Trakt asks for when creating an
    /// application; the device flow itself doesn't use it, but renewing a token sends it.
    /// </summary>
    public static class TraktPkceAuth
    {
        /// <summary>Client ID of the Trakt application MP-TVSeries ships with (public by design - Trakt client IDs are meant to be built into apps).</summary>
        public const string DefaultClientId = "HcSFeM7V1gNx3N5CdIyZYyVxBDZ0gnX00sNfcGYIP2M";

        /// <summary>The client ID in use: the user's own Trakt application if one is entered in Configuration, otherwise the default.</summary>
        public static string ClientId
        {
            get
            {
                string own = DBOption.GetOptions(DBOption.cTraktOwnClientId);
                return string.IsNullOrEmpty(own) || own.Trim().Length == 0 ? DefaultClientId : own.Trim();
            }
        }

        /// <summary>
        /// Called when the user changes their own client ID: a Trakt authorization only works with the application that
        /// issued it, so the stored one is dropped (ratings carry on without it; the user can authorize again).
        /// </summary>
        public static void ClientIdChanged()
        {
            if (string.IsNullOrEmpty((string)DBOption.GetOptions(DBOption.cTraktAccessToken)) &&
                string.IsNullOrEmpty((string)DBOption.GetOptions(DBOption.cTraktRefreshToken))) return;
            DBOption.SetOptions(DBOption.cTraktAccessToken, "");
            DBOption.SetOptions(DBOption.cTraktRefreshToken, "");
            MPTVSeriesLog.Write("Trakt: client ID changed - the previous authorization was removed (authorize again if you want it)");
        }

        private const string DeviceCodeUrl = "https://api.trakt.tv/oauth/device/code";
        private const string DeviceTokenUrl = "https://api.trakt.tv/oauth/device/token";
        private const string TokenUrl = "https://api.trakt.tv/oauth/token";
        private const string RedirectUri = "urn:ietf:wg:oauth:2.0:oob";

        /// <summary>What the user needs to see to complete step one, plus what's needed to finish it.</summary>
        public class DeviceCodeInfo
        {
            public string UserCode;
            public string VerificationUrl;
            public int ExpiresInSeconds;
            public int IntervalSeconds;

            internal string DeviceCode;
            internal string CodeVerifier;
        }

        public enum PollStatus
        {
            Pending,     // the user hasn't approved it yet - keep waiting
            SlowDown,    // Trakt asks for a longer interval between checks
            Authorized,  // approved: tokens are saved and in use
            Failed       // denied, expired, or something went wrong - see the error message
        }

        /// <summary>Short description of which authorization is currently in effect, for the settings screen.</summary>
        public static string GetStatusText()
        {
            return string.IsNullOrEmpty(DBOption.GetOptions(DBOption.cTraktAccessToken))
                ? "Not authorized - ratings work, but authorizing makes them faster"
                : "Authorized through MP-TVSeries";
        }

        /// <summary>
        /// Step 1: asks Trakt for a code the user can enter at trakt.tv/activate. Returns null (with an
        /// error message) if Trakt couldn't be reached or refused.
        /// </summary>
        public static DeviceCodeInfo RequestDeviceCode(out string errorMessage)
        {
            errorMessage = null;

            string codeVerifier = GenerateCodeVerifier();
            string body = string.Format(
                "{{\"client_id\":\"{0}\",\"code_challenge\":\"{1}\",\"code_challenge_method\":\"S256\"}}",
                JsonEscape(ClientId),
                JsonEscape(ComputeCodeChallenge(codeVerifier)));

            string response, detail;
            HttpStatusCode status;
            if (!PostJson(DeviceCodeUrl, body, out response, out status, out detail))
            {
                MPTVSeriesLog.Write("Trakt device code request failed: " + detail);
                errorMessage = detail;
                return null;
            }

            try
            {
                var obj = JsonHelpers.AsObject(MiniJson.Parse(response));
                string deviceCode = obj == null ? null : JsonHelpers.GetString(obj, "device_code");
                string userCode = obj == null ? null : JsonHelpers.GetString(obj, "user_code");
                if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(userCode))
                {
                    errorMessage = "Trakt's response didn't include a code.";
                    return null;
                }

                string verificationUrl = JsonHelpers.GetString(obj, "verification_url");
                int expiresIn = JsonHelpers.GetInt(obj, "expires_in");
                int interval = JsonHelpers.GetInt(obj, "interval");

                return new DeviceCodeInfo
                {
                    UserCode = userCode,
                    VerificationUrl = string.IsNullOrEmpty(verificationUrl) ? "https://trakt.tv/activate" : verificationUrl,
                    ExpiresInSeconds = expiresIn > 0 ? expiresIn : 600,
                    IntervalSeconds = interval > 0 ? interval : 5,
                    DeviceCode = deviceCode,
                    CodeVerifier = codeVerifier
                };
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Trakt device code response couldn't be read: " + ex.Message);
                errorMessage = "Trakt's response couldn't be read.";
                return null;
            }
        }

        /// <summary>
        /// Step 2: call this every <see cref="DeviceCodeInfo.IntervalSeconds"/> until it returns something
        /// other than Pending. On Authorized the tokens are already saved and in use.
        /// </summary>
        public static PollStatus PollForToken(DeviceCodeInfo info, out string errorMessage)
        {
            errorMessage = null;

            string body = string.Format(
                "{{\"code\":\"{0}\",\"client_id\":\"{1}\",\"code_verifier\":\"{2}\"}}",
                JsonEscape(info.DeviceCode),
                JsonEscape(ClientId),
                JsonEscape(info.CodeVerifier));

            string response, detail;
            HttpStatusCode status;
            if (PostJson(DeviceTokenUrl, body, out response, out status, out detail))
            {
                if (StoreTokens(response, out errorMessage))
                    return PollStatus.Authorized;
                return PollStatus.Failed;
            }

            switch ((int)status)
            {
                case 400: return PollStatus.Pending;
                case 429: return PollStatus.SlowDown;
                case 404: errorMessage = "Trakt didn't recognise the code."; break;
                case 409: errorMessage = "That code has already been used."; break;
                case 410: errorMessage = "The code expired."; break;
                case 418: errorMessage = "Access was denied on Trakt."; break;
                default:
                    MPTVSeriesLog.Write("Trakt authorization check failed: " + detail);
                    errorMessage = detail;
                    break;
            }
            return PollStatus.Failed;
        }

        /// <summary>
        /// Client ID and (optional) token for MP-TVSeries' own direct requests (see TraktRatingsClient). Always
        /// MP-TVSeries' own client ID, plus its own token if the user authorized it - the two always match.
        /// Doesn't touch TraktAPI.dll's settings, so the Trakt plugin is unaffected.
        /// </summary>
        public static void GetRequestCredentials(out string clientId, out string accessToken)
        {
            clientId = ClientId;
            accessToken = DBOption.GetOptions(DBOption.cTraktAccessToken);
            if (string.IsNullOrEmpty(accessToken)) accessToken = null;
        }

        public static bool IsAuthorized()
        {
            return !string.IsNullOrEmpty(DBOption.GetOptions(DBOption.cTraktAccessToken));
        }

        /// <summary>
        /// Removes MP-TVSeries' Trakt authorization from this install, and asks Trakt to revoke the token too (so
        /// it also disappears from the user's connected apps on trakt.tv). The local removal always happens; the
        /// revoke is best effort - if Trakt can't be reached the token simply expires on its own.
        /// </summary>
        public static void RemoveAuthorization()
        {
            string token = DBOption.GetOptions(DBOption.cTraktAccessToken);
            if (!string.IsNullOrEmpty(token))
            {
                string response, detail;
                HttpStatusCode status;
                string body = string.Format("{{\"token\":\"{0}\",\"client_id\":\"{1}\"}}", JsonEscape(token), JsonEscape(ClientId));
                if (PostJson("https://api.trakt.tv/oauth/revoke", body, out response, out status, out detail))
                    MPTVSeriesLog.Write("Trakt: authorization revoked");
                else
                    MPTVSeriesLog.Write("Trakt: could not revoke the authorization at trakt.tv (" + detail + ") - removed from this install anyway");
            }

            DBOption.SetOptions(DBOption.cTraktAccessToken, string.Empty);
            DBOption.SetOptions(DBOption.cTraktRefreshToken, string.Empty);
            MPTVSeriesLog.Write("Trakt: authorization removed - community ratings continue without it");
        }

        /// <summary>Renews MP-TVSeries' own token after Trakt rejected it. False if there's nothing to renew or it failed.</summary>
        public static bool TryRenewOwnToken()
        {
            if (string.IsNullOrEmpty(DBOption.GetOptions(DBOption.cTraktAccessToken))) return false;
            return HandleTokenExpired();
        }

        private static bool HandleTokenExpired()
        {
            string refreshToken = DBOption.GetOptions(DBOption.cTraktRefreshToken);
            if (string.IsNullOrEmpty(refreshToken)) return false;

            string body = string.Format(
                "{{\"refresh_token\":\"{0}\",\"client_id\":\"{1}\",\"redirect_uri\":\"{2}\",\"grant_type\":\"refresh_token\"}}",
                JsonEscape(refreshToken),
                JsonEscape(ClientId),
                JsonEscape(RedirectUri));

            string response, detail, error;
            HttpStatusCode status;
            if (!PostJson(TokenUrl, body, out response, out status, out detail))
            {
                MPTVSeriesLog.Write("Trakt token renewal failed: " + detail);
                return false;
            }
            return StoreTokens(response, out error);
        }

        private static bool StoreTokens(string responseText, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                var obj = JsonHelpers.AsObject(MiniJson.Parse(responseText));
                string accessToken = obj == null ? null : JsonHelpers.GetString(obj, "access_token");
                string refreshToken = obj == null ? null : JsonHelpers.GetString(obj, "refresh_token");

                if (string.IsNullOrEmpty(accessToken))
                {
                    errorMessage = "Trakt's response didn't include an access token.";
                    return false;
                }

                DBOption.SetOptions(DBOption.cTraktAccessToken, accessToken);
                if (!string.IsNullOrEmpty(refreshToken))
                    DBOption.SetOptions(DBOption.cTraktRefreshToken, refreshToken);

                return true;
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Trakt token response couldn't be read: " + ex.Message);
                errorMessage = "Trakt's response couldn't be read.";
                return false;
            }
        }

        /// <summary>
        /// POSTs JSON. Returns true on a 2xx response; otherwise false, with the HTTP status (0 if there
        /// was no response at all) and a description of what went wrong.
        /// </summary>
        private static bool PostJson(string url, string jsonBody, out string responseText, out HttpStatusCode status, out string errorDetail)
        {
            responseText = null;
            status = 0;
            errorDetail = null;

            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)0xc00;

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.UserAgent = "MP-TVSeries";
                request.Headers.Add("trakt-api-version", "2");
                request.Headers.Add("trakt-api-key", ClientId);

                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                request.ContentLength = bodyBytes.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(bodyBytes, 0, bodyBytes.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    status = response.StatusCode;
                    using (var reader = new System.IO.StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        responseText = reader.ReadToEnd();
                }
                return true;
            }
            catch (WebException wex)
            {
                errorDetail = wex.Message;
                var httpResponse = wex.Response as HttpWebResponse;
                if (httpResponse != null)
                {
                    status = httpResponse.StatusCode;
                    try
                    {
                        using (var reader = new System.IO.StreamReader(httpResponse.GetResponseStream(), Encoding.UTF8))
                            responseText = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(responseText))
                            errorDetail = wex.Message + " - " + responseText;
                    }
                    catch (Exception) { /* extra detail is best-effort only */ }
                }
                return false;
            }
            catch (Exception ex)
            {
                errorDetail = ex.Message;
                return false;
            }
        }

        private static string GenerateCodeVerifier()
        {
            // 32 random bytes, base64url-encoded: 43 characters, inside PKCE's required 43-128 range
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string ComputeCodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
                return Base64UrlEncode(sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)));
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static string JsonEscape(string s)
        {
            return (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
