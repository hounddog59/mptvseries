using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using WindowPlugins.GUITVSeries.Online_Parsing_Classes.Tmdb;
using WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.Tvmaze
{
    /// <summary>
    /// Show information from TVmaze, the backup for shows TMDB doesn't have (some documentaries, and series TMDB
    /// lists differently). Used automatically when TMDB is the source; writes the same cache files in the same
    /// shape as TMDB and TheTVDB ({lang}.xml, banners.xml, actors.xml), so nothing else needs to know.
    ///
    /// Shows keep TheTVDB's numbers: TVmaze records them (externals.thetvdb) and can look shows up by them
    /// (/lookup/shows?thetvdb=). A show TVmaze has without the number is matched by name and year, like TMDB's.
    /// TVmaze's text is in English.
    /// </summary>
    static class TvmazeSeriesFetcher
    {
        // TheTVDB number -> TVmaze number, remembered for this session (0 = not on TVmaze)
        static readonly Dictionary<int, int> sTvmazeByTvdb = new Dictionary<int, int>();
        static readonly HashSet<string> sNamesSearched = new HashSet<string>();
        static readonly object sIdLock = new object();

        const int MaxSearchResults = 10;

        // ------------------------------------------------------------------
        // Search: adds TVmaze's matches that the search results don't have yet
        // ------------------------------------------------------------------

        public static void AppendSearchResults(XmlDocument doc, XmlElement root, string seriesName, string languageCode)
        {
            try
            {
                var have = new HashSet<string>();
                foreach (XmlNode id in root.SelectNodes("Series/seriesid")) have.Add(id.InnerText);
                if (have.Count >= MaxSearchResults) return;

                List<object> results = JsonHelpers.AsArray(TvmazeClient.Get("/search/shows?q=" + Uri.EscapeDataString(seriesName ?? string.Empty)));
                if (results == null) return;

                foreach (object item in results)
                {
                    if (have.Count >= MaxSearchResults) break;
                    var show = JsonHelpers.AsObject(JsonHelpers.Get(JsonHelpers.AsObject(item), "show"));
                    if (show == null) continue;

                    int tvdbId = JsonHelpers.GetInt(JsonHelpers.AsObject(JsonHelpers.Get(show, "externals")), "thetvdb");
                    if (tvdbId <= 0 || !have.Add(tvdbId.ToString())) continue; // the library needs TheTVDB's number
                    Remember(tvdbId, JsonHelpers.GetInt(show, "id"));

                    XmlElement series = doc.CreateElement("Series");
                    root.AppendChild(series);
                    TmdbSeriesFetcher.Append(doc, series, "seriesid", tvdbId.ToString());
                    TmdbSeriesFetcher.Append(doc, series, "SeriesName", JsonHelpers.GetString(show, "name"));
                    TmdbSeriesFetcher.Append(doc, series, "Overview", PlainText(JsonHelpers.GetString(show, "summary")));
                    TmdbSeriesFetcher.Append(doc, series, "FirstAired", JsonHelpers.GetString(show, "premiered"));
                    TmdbSeriesFetcher.Append(doc, series, "Network", NetworkName(show));
                    TmdbSeriesFetcher.Append(doc, series, "IMDB_ID", JsonHelpers.GetString(JsonHelpers.AsObject(JsonHelpers.Get(show, "externals")), "imdb"));
                    TmdbSeriesFetcher.Append(doc, series, "banner", string.Empty);
                    TmdbSeriesFetcher.Append(doc, series, "Status", StatusFor(JsonHelpers.GetString(show, "status")));
                    TmdbSeriesFetcher.Append(doc, series, "Genre", JsonHelpers.JoinStringArray(JsonHelpers.GetArray(show, "genres"), "|"));
                    TmdbSeriesFetcher.Append(doc, series, "AliasNames", string.Empty);
                    TmdbSeriesFetcher.Append(doc, series, "language", languageCode);
                    TmdbSeriesFetcher.Append(doc, series, "slug", string.Empty);
                }
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("TVmaze: search failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Full series
        // ------------------------------------------------------------------

        /// <summary>
        /// Fetches everything for one show (known by its TheTVDB number) and writes {lang}.xml, banners.xml and
        /// actors.xml into the cache. Returns true if the show was found and saved.
        /// </summary>
        public static bool FetchAndCacheSeries(int tvdbId, string languageCode)
        {
            int tvmazeId = TvmazeIdFor(tvdbId);
            if (tvmazeId <= 0) tvmazeId = FindByName(tvdbId);
            if (tvmazeId <= 0)
            {
                MPTVSeriesLog.Write(string.Format("TVmaze: no show on TVmaze has TheTVDB number {0}, and none matched its name - nothing downloaded for it", tvdbId));
                return false;
            }

            var show = JsonHelpers.AsObject(TvmazeClient.Get(string.Format("/shows/{0}?embed%5B%5D=cast&embed%5B%5D=seasons", tvmazeId)));
            if (show == null)
            {
                MPTVSeriesLog.Write(string.Format("TVmaze: could not get show {0} (TheTVDB {1})", tvmazeId, tvdbId));
                return false;
            }
            List<object> episodes = JsonHelpers.AsArray(TvmazeClient.Get(string.Format("/shows/{0}/episodes?specials=1", tvmazeId))) ?? new List<object>();
            List<object> images = JsonHelpers.AsArray(TvmazeClient.Get(string.Format("/shows/{0}/images", tvmazeId))) ?? new List<object>();

            string lang = string.IsNullOrEmpty(languageCode) ? "en" : languageCode;
            string cacheDir = Path.Combine(Settings.GetPath(Settings.Path.config), string.Format(@"Cache\{0}\{1}", tvdbId, lang));
            var embedded = JsonHelpers.AsObject(JsonHelpers.Get(show, "_embedded"));

            Helper.SaveXmlCache(Path.Combine(cacheDir, lang + ".xml"), BuildSeriesXml(tvdbId, show, episodes, images, lang));
            Helper.SaveXmlCache(Path.Combine(cacheDir, "actors.xml"), BuildActorsXml(JsonHelpers.GetArray(embedded, "cast")));
            Helper.SaveXmlCache(Path.Combine(cacheDir, "banners.xml"), BuildBannersXml(tvdbId, images, JsonHelpers.GetArray(embedded, "seasons")));
            OnlineSource.MarkFetched(tvdbId);

            MPTVSeriesLog.Write(string.Format("TVmaze: \"{0}\" (TheTVDB {1}) downloaded from TVmaze - {2} episodes", JsonHelpers.GetString(show, "name"), tvdbId, episodes.Count));
            return true;
        }

        internal static XmlNode BuildSeriesXml(int tvdbId, Dictionary<string, object> show, List<object> episodes, List<object> images, string languageCode)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Data");
            doc.AppendChild(root);

            XmlElement series = doc.CreateElement("Series");
            root.AppendChild(series);

            var externals = JsonHelpers.AsObject(JsonHelpers.Get(show, "externals"));
            var schedule = JsonHelpers.AsObject(JsonHelpers.Get(show, "schedule"));
            List<object> days = JsonHelpers.GetArray(schedule, "days");

            Append(doc, series, "id", tvdbId.ToString());
            Append(doc, series, "SeriesName", JsonHelpers.GetString(show, "name"));
            Append(doc, series, "Overview", PlainText(JsonHelpers.GetString(show, "summary")));
            Append(doc, series, "Status", StatusFor(JsonHelpers.GetString(show, "status")));
            Append(doc, series, "Genre", JsonHelpers.JoinStringArray(JsonHelpers.GetArray(show, "genres"), "|"));
            Append(doc, series, "AliasNames", string.Empty);
            Append(doc, series, "Airs_DayOfWeek", days != null && days.Count > 0 ? Convert.ToString(days[0]) : string.Empty);
            Append(doc, series, "Airs_Time", JsonHelpers.GetString(schedule, "time"));
            Append(doc, series, "FirstAired", JsonHelpers.GetString(show, "premiered"));
            Append(doc, series, "IMDB_ID", JsonHelpers.GetString(externals, "imdb"));
            Append(doc, series, "zap2it_id", string.Empty);
            Append(doc, series, "ContentRating", string.Empty);
            Append(doc, series, "Network", NetworkName(show));
            Append(doc, series, "NetworkID", NetworkId(show));

            // community ratings come from Trakt, as with the other sources
            Append(doc, series, "Rating", string.Empty);
            Append(doc, series, "RatingCount", string.Empty);
            Append(doc, series, "lastupdated", string.Empty);
            Append(doc, series, "poster", ImageOf(show, "original"));
            Append(doc, series, "banner", FirstImageUrl(images, "banner"));
            Append(doc, series, "fanart", FirstImageUrl(images, "background"));
            Append(doc, series, "slug", string.Empty);
            Append(doc, series, "language", languageCode);

            // TVmaze lists specials inside the season they aired in, without an episode number; the library keeps
            // them in season 0 (as TheTVDB does), numbered in the order they aired
            int specials = 0;
            var seen = new HashSet<string>();
            foreach (object item in episodes)
            {
                var ep = JsonHelpers.AsObject(item);
                if (ep == null) continue;

                string type = JsonHelpers.GetString(ep, "type");
                bool isSpecial = JsonHelpers.Get(ep, "number") == null || (type != null && type.Contains("special"));
                int seasonNumber = isSpecial ? 0 : JsonHelpers.GetInt(ep, "season");
                int episodeNumber = isSpecial ? ++specials : JsonHelpers.GetInt(ep, "number");
                if (!seen.Add(seasonNumber + "x" + episodeNumber)) continue; // listed twice

                XmlElement episodeEl = doc.CreateElement("Episode");
                root.AppendChild(episodeEl);
                Append(doc, episodeEl, "id", JsonHelpers.GetString(ep, "id"));
                Append(doc, episodeEl, "seriesid", tvdbId.ToString());
                Append(doc, episodeEl, "EpisodeName", JsonHelpers.GetString(ep, "name"));
                Append(doc, episodeEl, "Overview", PlainText(JsonHelpers.GetString(ep, "summary")));
                Append(doc, episodeEl, "SeasonNumber", seasonNumber.ToString());
                Append(doc, episodeEl, "EpisodeNumber", episodeNumber.ToString());
                Append(doc, episodeEl, "AbsoluteNumber", string.Empty);
                Append(doc, episodeEl, "FirstAired", JsonHelpers.GetString(ep, "airdate"));
                Append(doc, episodeEl, "filename", ImageOf(ep, "original"));
                Append(doc, episodeEl, "Language", languageCode);
                Append(doc, episodeEl, "IMDB_ID", string.Empty);
                Append(doc, episodeEl, "ProductionCode", string.Empty);
                Append(doc, episodeEl, "lastupdated", string.Empty);
                Append(doc, episodeEl, "GuestStars", string.Empty);
                Append(doc, episodeEl, "Director", string.Empty);
                Append(doc, episodeEl, "Writer", string.Empty);
            }
            return root;
        }

        internal static XmlNode BuildActorsXml(List<object> cast)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Actors");
            doc.AppendChild(root);
            if (cast == null) return root;

            int order = 0;
            foreach (object item in cast)
            {
                if (order >= 50) break; // the main cast
                var c = JsonHelpers.AsObject(item);
                var person = JsonHelpers.AsObject(JsonHelpers.Get(c, "person"));
                if (person == null) continue;
                var character = JsonHelpers.AsObject(JsonHelpers.Get(c, "character"));

                XmlElement actor = doc.CreateElement("Actor");
                root.AppendChild(actor);
                Append(doc, actor, "id", JsonHelpers.GetString(person, "id"));
                Append(doc, actor, "Name", JsonHelpers.GetString(person, "name"));
                Append(doc, actor, "Role", JsonHelpers.GetString(character, "name"));
                Append(doc, actor, "SortOrder", (order++).ToString());
                Append(doc, actor, "Image", ImageOf(person, "medium"));
            }
            return root;
        }

        internal static XmlNode BuildBannersXml(int tvdbId, List<object> images, List<object> seasons)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Banners");
            doc.AppendChild(root);

            foreach (object item in images ?? new List<object>())
            {
                var image = JsonHelpers.AsObject(item);
                var resolutions = JsonHelpers.AsObject(JsonHelpers.Get(image, "resolutions"));
                var original = JsonHelpers.AsObject(JsonHelpers.Get(resolutions, "original"));
                var medium = JsonHelpers.AsObject(JsonHelpers.Get(resolutions, "medium"));
                string url = JsonHelpers.GetString(original, "url");
                if (string.IsNullOrEmpty(url)) continue;
                string thumb = JsonHelpers.GetString(medium, "url");
                if (string.IsNullOrEmpty(thumb)) thumb = url;

                switch (JsonHelpers.GetString(image, "type"))
                {
                    case "poster":
                        TmdbSeriesFetcher.AppendBanner(doc, root, url, "poster", "680x1000", url, thumb, "en", null, null, string.Empty, "0");
                        break;
                    case "banner":
                        TmdbSeriesFetcher.AppendBanner(doc, root, url, "series", "graphical", url, thumb, "en", null, null, string.Empty, "0");
                        break;
                    case "background":
                        // labels as the Fanart "Resolution" choice knows them (1280x720 / 1920x1080)
                        int width = JsonHelpers.GetInt(original, "width"), height = JsonHelpers.GetInt(original, "height");
                        string resolution = width >= 1920 ? "1920x1080" : width > 0 && height > 0 ? width + "x" + height : "1280x720";
                        TmdbSeriesFetcher.AppendBanner(doc, root, url, "fanart", resolution, url, thumb, string.Empty, null, null, string.Empty, "0");
                        break;
                    // "typography" (title lettering) and untyped old pictures are left out
                }
            }

            foreach (object item in seasons ?? new List<object>())
            {
                var season = JsonHelpers.AsObject(item);
                string url = ImageOf(season, "original");
                if (string.IsNullOrEmpty(url)) continue;
                string thumb = ImageOf(season, "medium");
                TmdbSeriesFetcher.AppendBanner(doc, root, url, "season", "season", url, string.IsNullOrEmpty(thumb) ? url : thumb, "en", null,
                                               JsonHelpers.GetString(season, "number"), string.Empty, "0");
            }

            // more wide banners from fanart.tv, by TheTVDB number
            TmdbSeriesFetcher.AppendFanartTvBanners(doc, root, tvdbId);
            return root;
        }

        // ------------------------------------------------------------------
        // TheTVDB <-> TVmaze numbers
        // ------------------------------------------------------------------

        static int TvmazeIdFor(int tvdbId)
        {
            lock (sIdLock)
            {
                int known;
                if (sTvmazeByTvdb.TryGetValue(tvdbId, out known)) return known;
            }

            bool notFound;
            var show = JsonHelpers.AsObject(TvmazeClient.Get("/lookup/shows?thetvdb=" + tvdbId, out notFound));
            int tvmazeId = JsonHelpers.GetInt(show, "id");
            if (show != null || notFound) Remember(tvdbId, tvmazeId); // don't remember a failed request as "not on TVmaze"
            return tvmazeId;
        }

        /// <summary>
        /// For a show TVmaze has without its TheTVDB number: look it up by the names the library knows it by, and use
        /// it only when exactly one TVmaze show has that name and started in the same year (give or take one), and
        /// isn't linked to a different TheTVDB show. Returns TVmaze's number, or 0.
        /// </summary>
        static int FindByName(int tvdbId)
        {
            int year;
            List<string> names = TmdbSeriesFetcher.NamesToSearch(tvdbId, out year);
            foreach (string name in names)
            {
                lock (sIdLock) if (!sNamesSearched.Add(tvdbId + "|" + name)) continue; // each name once per session
                List<object> results = JsonHelpers.AsArray(TvmazeClient.Get("/search/shows?q=" + Uri.EscapeDataString(name)));
                if (results == null) continue;

                string wanted = TmdbSeriesFetcher.Normalize(name);
                var matches = new List<Dictionary<string, object>>();
                foreach (object item in results)
                {
                    var show = JsonHelpers.AsObject(JsonHelpers.Get(JsonHelpers.AsObject(item), "show"));
                    if (show == null) continue;
                    if (TmdbSeriesFetcher.Normalize(TmdbSeriesFetcher.CleanName(JsonHelpers.GetString(show, "name"))) != wanted) continue;
                    int showYear = TmdbSeriesFetcher.YearOf(JsonHelpers.GetString(show, "premiered"));
                    if (year > 0 && (showYear == 0 || Math.Abs(showYear - year) > 1)) continue;
                    matches.Add(show);
                }
                if (matches.Count != 1) continue; // none, or too close to call

                int linkedTvdb = JsonHelpers.GetInt(JsonHelpers.AsObject(JsonHelpers.Get(matches[0], "externals")), "thetvdb");
                if (linkedTvdb > 0 && linkedTvdb != tvdbId) continue; // that one belongs to another TheTVDB show

                int tvmazeId = JsonHelpers.GetInt(matches[0], "id");
                MPTVSeriesLog.Write(string.Format("TVmaze: \"{0}\" (TheTVDB {1}) has no TheTVDB number on TVmaze - matched by name{2} to TVmaze show {3}",
                    name, tvdbId, year > 0 ? " and year" : string.Empty, tvmazeId));
                Remember(tvdbId, tvmazeId);
                return tvmazeId;
            }
            return 0;
        }

        static void Remember(int tvdbId, int tvmazeId)
        {
            if (tvdbId <= 0) return;
            lock (sIdLock) sTvmazeByTvdb[tvdbId] = tvmazeId;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>TVmaze's status in the words TheTVDB used, which the plugin and skins understand.</summary>
        internal static string StatusFor(string status)
        {
            switch (status)
            {
                case "Running":
                case "To Be Determined": return "Continuing";
                case "Ended": return "Ended";
                case "In Development": return "Upcoming";
                default: return status ?? string.Empty;
            }
        }

        static readonly Regex sParagraphEnd = new Regex(@"</p>\s*<p>|<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex sTag = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        /// <summary>TVmaze's descriptions are HTML ("&lt;p&gt;A &lt;b&gt;show&lt;/b&gt;...&lt;/p&gt;"); this makes them plain text.</summary>
        internal static string PlainText(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            string text = sParagraphEnd.Replace(html, "\n");
            text = sTag.Replace(text, string.Empty);
            return WebUtility.HtmlDecode(text).Trim();
        }

        static string NetworkName(Dictionary<string, object> show)
        {
            string name = JsonHelpers.GetString(JsonHelpers.AsObject(JsonHelpers.Get(show, "network")), "name");
            return string.IsNullOrEmpty(name) ? JsonHelpers.GetString(JsonHelpers.AsObject(JsonHelpers.Get(show, "webChannel")), "name") : name;
        }

        static string NetworkId(Dictionary<string, object> show)
        {
            return JsonHelpers.GetString(JsonHelpers.AsObject(JsonHelpers.Get(show, "network")), "id");
        }

        /// <summary>The "image" of a show, episode, season or person in the size given ("medium" / "original").</summary>
        static string ImageOf(Dictionary<string, object> o, string size)
        {
            var image = JsonHelpers.AsObject(JsonHelpers.Get(o, "image"));
            string url = JsonHelpers.GetString(image, size);
            return string.IsNullOrEmpty(url) ? JsonHelpers.GetString(image, "original") : url;
        }

        static string FirstImageUrl(List<object> images, string type)
        {
            if (images == null) return string.Empty;
            string first = null;
            foreach (object item in images)
            {
                var image = JsonHelpers.AsObject(item);
                if (image == null || JsonHelpers.GetString(image, "type") != type) continue;
                string url = JsonHelpers.GetString(JsonHelpers.AsObject(JsonHelpers.Get(JsonHelpers.AsObject(JsonHelpers.Get(image, "resolutions")), "original")), "url");
                if (string.IsNullOrEmpty(url)) continue;
                if (JsonHelpers.GetString(image, "main") == "1") return url; // TVmaze's main picture of that kind
                if (first == null) first = url;
            }
            return first ?? string.Empty;
        }

        static void Append(XmlDocument doc, XmlElement parent, string name, string value)
        {
            TmdbSeriesFetcher.Append(doc, parent, name, value);
        }
    }
}
