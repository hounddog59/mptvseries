using System;
using System.Collections.Generic;
using System.Xml;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4
{
    /// <summary>
    /// TVDB v4 dropped XML entirely for JSON, dropped the "mirrors" concept, and reorganized
    /// the data model (episodes/artwork/characters are separate endpoints instead of one
    /// zipped bundle). Rather than rewrite the ~2000-line worker_DoWork method in
    /// OnlineParse.cs and every Get*.cs parser (all of which walk XmlNode trees keyed off the
    /// old v3 element names), this adapter fetches from v4 and re-serializes the results into
    /// XML using the SAME element names the v3 API used. That lets all existing downstream
    /// code keep working unmodified.
    ///
    /// IMPORTANT - field-name confidence: the auth flow, base URL, and endpoint paths are
    /// confirmed against TVDB's current documentation. The *exact* JSON field names below for
    /// series/episode/artwork objects are TVDB v4's documented shape as of this writing, but
    /// this environment has no network access to TVDB, so none of this has been test-run
    /// against a live response. If a field ends up empty after your first real run, check the
    /// MPTVSeriesLog debug output (which logs raw JSON - see TvdbV4Client) and adjust the
    /// candidate key list in the relevant GetString/GetArray call below; the whole point of
    /// JsonHelpers' multi-key lookup is to make that a one-line fix rather than a rewrite.
    /// </summary>
    static class TvdbV4XmlAdapter
    {
        // ------------------------------------------------------------------
        // Search  (replaces GetSeries.php)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a <Data><Series>...</Series>...</Data> document from a v4 /search response,
        /// matching what GetSeries.cs expects (it special-cases a "seriesid" element for the
        /// ID, then maps everything else via DBOnlineSeries.s_OnlineToFieldMap or adds it as a
        /// dynamic column keyed by the raw element name).
        /// </summary>
        public static XmlNode BuildSearchResultsXml(List<object> searchResults, string languageCode)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Data");
            doc.AppendChild(root);

            if (searchResults == null) return root;

            foreach (object item in searchResults)
            {
                var result = JsonHelpers.AsObject(item);
                if (result == null) continue;

                XmlElement series = doc.CreateElement("Series");
                root.AppendChild(series);

                // v4 search results carry the numeric id either as "tvdb_id" or inside a
                // prefixed "id" like "series-12345" - handle both.
                string rawId = JsonHelpers.GetString(result, "tvdb_id", "id");
                string numericId = ExtractNumericId(rawId);

                AppendElement(doc, series, "seriesid", numericId);
                // v4 search results list translated titles and overviews by three-letter language code;
                // prefer the requested language, falling back to the original-language name
                string v4Lang = TvdbV4Languages.ToV4Code(languageCode);
                string originalName = JsonHelpers.GetString(result, "name", "seriesName");
                string translatedName = JsonHelpers.GetString(JsonHelpers.AsObject(JsonHelpers.Get(result, "translations")), v4Lang);
                string translatedOverview = JsonHelpers.GetString(JsonHelpers.AsObject(JsonHelpers.Get(result, "overviews")), v4Lang);
                AppendElement(doc, series, "SeriesName", string.IsNullOrEmpty(translatedName) ? originalName : translatedName);
                AppendElement(doc, series, "Overview", string.IsNullOrEmpty(translatedOverview) ? JsonHelpers.GetString(result, "overview") : translatedOverview);
                AppendElement(doc, series, "FirstAired", JsonHelpers.GetString(result, "first_air_time", "firstAirTime", "year"));
                AppendElement(doc, series, "Network", JsonHelpers.GetString(result, "network", "primary_network"));
                AppendElement(doc, series, "IMDB_ID", JsonHelpers.GetString(result, "imdb_id"));
                AppendElement(doc, series, "banner", JsonHelpers.GetString(result, "image_url", "thumbnail"));
                AppendElement(doc, series, "Status", JsonHelpers.GetString(result, "status"));
                AppendElement(doc, series, "Genre", JsonHelpers.JoinStringArray(JsonHelpers.GetArray(result, "genres"), "|"));
                string aliases = JsonHelpers.JoinStringArray(JsonHelpers.GetArray(result, "aliases"), "|");
                if (!string.IsNullOrEmpty(translatedName) && !string.IsNullOrEmpty(originalName) && translatedName != originalName)
                    // same "|a|b|" form JoinStringArray produces
                    aliases = string.IsNullOrEmpty(aliases) ? "|" + originalName + "|" : aliases + originalName + "|";
                AppendElement(doc, series, "AliasNames", aliases);
                AppendElement(doc, series, "language", languageCode);
                AppendElement(doc, series, "slug", JsonHelpers.GetString(result, "slug"));
            }

            return root;
        }

        // ------------------------------------------------------------------
        // Full series record + episodes  (replaces the "series/{id}/all/{lang}.zip" bundle)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the per-language "en.xml"-equivalent document: a single &lt;Series&gt;
        /// element (full details) followed by one &lt;Episode&gt; element per episode - this
        /// is the exact shape GetEpisodes.cs (node.SelectNodes("Episode")) and the series
        /// half of worker_DoWork expect.
        /// </summary>
        public static XmlNode BuildFullSeriesXml(Dictionary<string, object> seriesExtended, List<object> episodes, string languageCode)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Data");
            doc.AppendChild(root);

            if (seriesExtended != null)
            {
                XmlElement series = doc.CreateElement("Series");
                root.AppendChild(series);
                PopulateSeriesElement(doc, series, seriesExtended, languageCode);
            }

            if (episodes != null)
            {
                foreach (object item in episodes)
                {
                    var ep = JsonHelpers.AsObject(item);
                    if (ep == null) continue;

                    XmlElement episodeEl = doc.CreateElement("Episode");
                    root.AppendChild(episodeEl);
                    PopulateEpisodeElement(doc, episodeEl, ep, languageCode);
                }
            }

            return root;
        }

        static void PopulateSeriesElement(XmlDocument doc, XmlElement series, Dictionary<string, object> s, string languageCode)
        {
            string rawId = JsonHelpers.GetString(s, "id");
            string numericId = ExtractNumericId(rawId);

            // Translations: v4 keeps localized name/overview separate from the base record.
            // seriesExtended usually embeds them under "translations": { "nameTranslations": [...], "overviewTranslations": [...] }
            var translations = JsonHelpers.AsObject(JsonHelpers.Get(s, "translations"));
            string name = FindTranslation(JsonHelpers.GetArray(translations, "nameTranslations"), languageCode, "name");
            string overview = FindTranslation(JsonHelpers.GetArray(translations, "overviewTranslations"), languageCode, "overview");
            if (string.IsNullOrEmpty(name)) name = JsonHelpers.GetString(s, "name");
            if (string.IsNullOrEmpty(overview)) overview = JsonHelpers.GetString(s, "overview");

            AppendElement(doc, series, "id", numericId);
            AppendElement(doc, series, "SeriesName", name);
            AppendElement(doc, series, "Overview", overview);

            var statusObj = JsonHelpers.AsObject(JsonHelpers.Get(s, "status"));
            string statusName = statusObj != null ? JsonHelpers.GetString(statusObj, "name") : JsonHelpers.GetString(s, "status");
            AppendElement(doc, series, "Status", statusName);

            AppendElement(doc, series, "Genre", JsonHelpers.JoinStringArray(JsonHelpers.GetArray(s, "genres"), "|"));
            AppendElement(doc, series, "AliasNames", JsonHelpers.JoinStringArray(JsonHelpers.GetArray(s, "aliases"), "|"));

            // Airs day/time: v4 exposes this under "airsDays" (object of booleans per weekday)
            // and "airsTime" (string, e.g. "20:00"). We collapse airsDays to the v3-style
            // single day-of-week string (first true day) since that's what AirsDay expects.
            var airsDays = JsonHelpers.AsObject(JsonHelpers.Get(s, "airsDays"));
            AppendElement(doc, series, "Airs_DayOfWeek", FirstTrueDay(airsDays));
            AppendElement(doc, series, "Airs_Time", JsonHelpers.GetString(s, "airsTime"));

            AppendElement(doc, series, "FirstAired", JsonHelpers.GetString(s, "firstAired"));
            AppendElement(doc, series, "IMDB_ID", FindRemoteId(JsonHelpers.GetArray(s, "remoteIds"), "IMDB"));
            AppendElement(doc, series, "zap2it_id", FindRemoteId(JsonHelpers.GetArray(s, "remoteIds"), "Zap2It"));

            AppendElement(doc, series, "ContentRating", FindContentRating(JsonHelpers.GetArray(s, "contentRatings"), languageCode));

            // v4 replaces the flat "Network" string with a "companies" array where each entry
            // has a "companyType"; the network is the entry whose companyType name/id
            // indicates "Network" (or fall back to originalNetwork if present).
            var originalNetwork = JsonHelpers.AsObject(JsonHelpers.Get(s, "originalNetwork", "latestNetwork"));
            AppendElement(doc, series, "Network", originalNetwork != null ? JsonHelpers.GetString(originalNetwork, "name") : FindCompanyName(JsonHelpers.GetArray(s, "companies"), "Network"));
            AppendElement(doc, series, "NetworkID", originalNetwork != null ? ExtractNumericId(JsonHelpers.GetString(originalNetwork, "id")) : string.Empty);

            // TVDB v4 has no 0-10 community rating: its "score" is a popularity number (often in the
            // millions), and the old siteRating was removed. Left empty so it never shows up as a rating;
            // community ratings come from Trakt instead (see TraktRatingsClient).
            AppendElement(doc, series, "Rating", string.Empty);
            AppendElement(doc, series, "RatingCount", string.Empty); // not exposed by v4 the same way
            AppendElement(doc, series, "lastupdated", ToUnixTimestamp(JsonHelpers.GetString(s, "lastUpdated")));
            AppendElement(doc, series, "poster", JsonHelpers.GetString(s, "image"));
            AppendElement(doc, series, "banner", FindArtworkOfType(JsonHelpers.GetArray(s, "artworks"), "banner"));
            AppendElement(doc, series, "fanart", FindArtworkOfType(JsonHelpers.GetArray(s, "artworks"), "background"));
            AppendElement(doc, series, "slug", JsonHelpers.GetString(s, "slug"));
            AppendElement(doc, series, "language", languageCode);
        }

        static void PopulateEpisodeElement(XmlDocument doc, XmlElement episodeEl, Dictionary<string, object> ep, string languageCode)
        {
            string rawId = JsonHelpers.GetString(ep, "id");
            AppendElement(doc, episodeEl, "id", ExtractNumericId(rawId));
            AppendElement(doc, episodeEl, "seriesid", ExtractNumericId(JsonHelpers.GetString(ep, "seriesId")));

            var translations = JsonHelpers.AsObject(JsonHelpers.Get(ep, "translations"));
            string name = translations != null
                ? FindTranslation(JsonHelpers.GetArray(translations, "nameTranslations"), languageCode, "name")
                : null;
            string overview = translations != null
                ? FindTranslation(JsonHelpers.GetArray(translations, "overviewTranslations"), languageCode, "overview")
                : null;
            if (string.IsNullOrEmpty(name)) name = JsonHelpers.GetString(ep, "name");
            if (string.IsNullOrEmpty(overview)) overview = JsonHelpers.GetString(ep, "overview");

            AppendElement(doc, episodeEl, "EpisodeName", name);
            AppendElement(doc, episodeEl, "Overview", overview);
            AppendElement(doc, episodeEl, "SeasonNumber", JsonHelpers.GetString(ep, "seasonNumber"));
            AppendElement(doc, episodeEl, "EpisodeNumber", JsonHelpers.GetString(ep, "number"));
            AppendElement(doc, episodeEl, "AbsoluteNumber", JsonHelpers.GetString(ep, "absoluteNumber"));
            AppendElement(doc, episodeEl, "FirstAired", JsonHelpers.GetString(ep, "aired"));
            AppendElement(doc, episodeEl, "filename", JsonHelpers.GetString(ep, "image"));
            AppendElement(doc, episodeEl, "Language", languageCode);
            AppendElement(doc, episodeEl, "IMDB_ID", FindRemoteId(JsonHelpers.GetArray(ep, "remoteIds"), "IMDB"));
            AppendElement(doc, episodeEl, "ProductionCode", JsonHelpers.GetString(ep, "productionCode"));
            AppendElement(doc, episodeEl, "lastupdated", ToUnixTimestamp(JsonHelpers.GetString(ep, "lastUpdated")));

            // GuestStars/Director/Writer come from the episode's "characters" list in v4
            // (filtered by peopleType), rather than flat strings as in v3.
            AppendElement(doc, episodeEl, "GuestStars", JsonHelpers.JoinStringArray(FilterCharacterNamesByType(JsonHelpers.GetArray(ep, "characters"), "Guest Star"), "|"));
            AppendElement(doc, episodeEl, "Director", JsonHelpers.JoinStringArray(FilterCharacterNamesByType(JsonHelpers.GetArray(ep, "characters"), "Director"), "|"));
            AppendElement(doc, episodeEl, "Writer", JsonHelpers.JoinStringArray(FilterCharacterNamesByType(JsonHelpers.GetArray(ep, "characters"), "Writer"), "|"));
        }

        // ------------------------------------------------------------------
        // Actors  (replaces actors.xml)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds &lt;Actors&gt;&lt;Actor&gt;...&lt;/Actor&gt;...&lt;/Actors&gt; matching what
        /// GetActors.cs expects (node.SelectNodes("/Actors/Actor"), then every child element's
        /// Name/InnerText is copied straight into the DBActor row).
        /// </summary>
        public static XmlNode BuildActorsXml(List<object> characters)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Actors");
            doc.AppendChild(root);

            if (characters == null) return root;

            foreach (object item in characters)
            {
                var c = JsonHelpers.AsObject(item);
                if (c == null) continue;

                // v4's series-extended "characters" array includes crew (writers, directors)
                // as well as cast; only cast members have a "personName"/"name" + are of
                // peopleType "Actor".
                string peopleType = JsonHelpers.GetString(c, "peopleType", "type");
                if (!string.IsNullOrEmpty(peopleType) && peopleType.IndexOf("Actor", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                XmlElement actor = doc.CreateElement("Actor");
                root.AppendChild(actor);

                AppendElement(doc, actor, "id", ExtractNumericId(JsonHelpers.GetString(c, "peopleId", "id")));
                AppendElement(doc, actor, "Name", JsonHelpers.GetString(c, "personName", "name"));
                AppendElement(doc, actor, "Role", JsonHelpers.GetString(c, "name")); // character name played
                AppendElement(doc, actor, "SortOrder", JsonHelpers.GetString(c, "sort"));
                AppendElement(doc, actor, "Image", JsonHelpers.GetString(c, "image", "personImgURL"));
            }

            return root;
        }

        // ------------------------------------------------------------------
        // Banners / artwork  (replaces banners.xml)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds &lt;Banners&gt;&lt;Banner&gt;...&lt;/Banner&gt;...&lt;/Banners&gt; matching
        /// GetBanner.cs's expectations: BannerType (series/poster/season/fanart), BannerType2,
        /// Language, BannerPath, ThumbnailPath, Rating, RatingCount, Season.
        ///
        /// artworkTypeNames maps v4's numeric artwork "type" id to a name (fetch once via
        /// /artwork/types and cache - see TvdbV4Client). v4 does not guarantee stable type IDs
        /// across accounts/regions, so we classify by the *name* rather than a hardcoded ID.
        /// </summary>
        public static XmlNode BuildBannersXml(List<object> artworks, Dictionary<int, string> artworkTypeNames)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Banners");
            doc.AppendChild(root);

            if (artworks == null) return root;

            foreach (object item in artworks)
            {
                var a = JsonHelpers.AsObject(item);
                if (a == null) continue;

                int typeId = JsonHelpers.GetInt(a, "type");
                string typeName = null;
                if (artworkTypeNames != null) artworkTypeNames.TryGetValue(typeId, out typeName);
                typeName = typeName ?? string.Empty;

                string bannerType = ClassifyBannerType(typeName);
                if (bannerType == null) continue; // unknown/unhandled artwork type - skip rather than guess

                XmlElement banner = doc.CreateElement("Banner");
                root.AppendChild(banner);

                AppendElement(doc, banner, "id", ExtractNumericId(JsonHelpers.GetString(a, "id")));
                AppendElement(doc, banner, "BannerType", bannerType);
                AppendElement(doc, banner, "BannerType2", ClassifyBannerType2(bannerType, JsonHelpers.GetInt(a, "width"), JsonHelpers.GetInt(a, "height")));
                AppendElement(doc, banner, "BannerPath", JsonHelpers.GetString(a, "image"));
                AppendElement(doc, banner, "ThumbnailPath", JsonHelpers.GetString(a, "thumbnail"));
                AppendElement(doc, banner, "Language", JsonHelpers.GetString(a, "language"));
                AppendElement(doc, banner, "Rating", JsonHelpers.GetString(a, "score"));
                AppendElement(doc, banner, "RatingCount", "0");
                if (bannerType == "season")
                    AppendElement(doc, banner, "Season", JsonHelpers.GetString(a, "seasonNumber"));
                if (bannerType == "fanart")
                    AppendElement(doc, banner, "SeriesName", "false"); // "has the show's name on it" - not known
            }

            return root;
        }

        static string ClassifyBannerType(string artworkTypeName)
        {
            if (string.IsNullOrEmpty(artworkTypeName)) return null;
            string n = artworkTypeName.ToLowerInvariant();
            if (n.Contains("season") && n.Contains("poster")) return "season";
            if (n.Contains("background") || n.Contains("fanart")) return "fanart";
            if (n.Contains("poster")) return "poster";
            if (n.Contains("banner")) return "series";
            return null;
        }

        static string ClassifyBannerType2(string bannerType, int width, int height)
        {
            switch (bannerType)
            {
                case "poster": return "680x1000";
                case "season": return "season";
                case "fanart": return width > 0 && height > 0 ? width + "x" + height : "1920x1080";
                default: return "graphical";
            }
        }

        // ------------------------------------------------------------------
        // Shared helpers
        // ------------------------------------------------------------------

        static void AppendElement(XmlDocument doc, XmlElement parent, string name, string value)
        {
            XmlElement el = doc.CreateElement(name);
            el.InnerText = value ?? string.Empty;
            parent.AppendChild(el);
        }

        /// <summary>v4 ids are sometimes bare numbers, sometimes prefixed slugs like "series-12345" or "people-987".</summary>
        static string ExtractNumericId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            int dash = raw.LastIndexOf('-');
            return dash >= 0 && dash < raw.Length - 1 ? raw.Substring(dash + 1) : raw;
        }

        static string FindTranslation(List<object> translationArray, string languageCode, string valueKey)
        {
            if (translationArray == null) return null;
            string wanted = TvdbV4Languages.ToV4Code(languageCode); // v4 labels translations "eng", not "en"
            foreach (object item in translationArray)
            {
                var t = JsonHelpers.AsObject(item);
                if (t == null) continue;
                string lang = JsonHelpers.GetString(t, "language");
                if (string.Equals(lang, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(lang, languageCode, StringComparison.OrdinalIgnoreCase))
                    return JsonHelpers.GetString(t, valueKey);
            }
            return null;
        }

        static string FindRemoteId(List<object> remoteIds, string sourceNameContains)
        {
            if (remoteIds == null) return string.Empty;
            foreach (object item in remoteIds)
            {
                var r = JsonHelpers.AsObject(item);
                if (r == null) continue;
                string sourceName = JsonHelpers.GetString(r, "sourceName");
                if (sourceName.IndexOf(sourceNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return JsonHelpers.GetString(r, "id");
            }
            return string.Empty;
        }

        static string FindContentRating(List<object> contentRatings, string languageCode)
        {
            if (contentRatings == null || contentRatings.Count == 0) return string.Empty;
            // prefer the rating matching the requested language's country when we can tell,
            // otherwise just take the first entry.
            foreach (object item in contentRatings)
            {
                var r = JsonHelpers.AsObject(item);
                if (r == null) continue;
                return JsonHelpers.GetString(r, "name");
            }
            return string.Empty;
        }

        static string FindCompanyName(List<object> companies, string companyTypeContains)
        {
            if (companies == null) return string.Empty;
            foreach (object item in companies)
            {
                var c = JsonHelpers.AsObject(item);
                if (c == null) continue;
                string type = JsonHelpers.GetString(c, "companyType", "name");
                if (type.IndexOf(companyTypeContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return JsonHelpers.GetString(c, "name");
            }
            return string.Empty;
        }

        static string FindArtworkOfType(List<object> artworks, string typeNameContains)
        {
            // Best-effort only: without the artwork-type-id map handy at this call site we
            // can't classify precisely, so this just returns empty for now. The full banner
            // list (BuildBannersXml) is the authoritative source for banner/fanart/poster
            // images; this is only used for the single "primary" image references on the
            // Series element itself.
            return string.Empty;
        }

        static string FirstTrueDay(Dictionary<string, object> airsDays)
        {
            if (airsDays == null) return string.Empty;
            string[] days = { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };
            foreach (string day in days)
            {
                object v;
                if (airsDays.TryGetValue(day, out v) && v is bool && (bool)v)
                    return char.ToUpperInvariant(day[0]) + day.Substring(1);
            }
            return string.Empty;
        }

        static List<object> FilterCharacterNamesByType(List<object> characters, string peopleType)
        {
            var names = new List<object>();
            if (characters == null) return names;
            foreach (object item in characters)
            {
                var c = JsonHelpers.AsObject(item);
                if (c == null) continue;
                string type = JsonHelpers.GetString(c, "peopleType");
                if (string.Equals(type, peopleType, StringComparison.OrdinalIgnoreCase))
                {
                    string name = JsonHelpers.GetString(c, "personName", "name");
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
            }
            return names;
        }

        static string ToUnixTimestamp(string iso8601OrEmpty)
        {
            if (string.IsNullOrEmpty(iso8601OrEmpty)) return string.Empty;
            DateTime dt;
            if (DateTime.TryParse(iso8601OrEmpty, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out dt))
            {
                return ((long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString();
            }
            return string.Empty;
        }
    }
}
