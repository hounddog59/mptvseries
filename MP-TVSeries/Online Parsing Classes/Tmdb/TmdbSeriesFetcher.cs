using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.Tmdb
{
    /// <summary>
    /// Show information from TMDB, for people without a TheTVDB subscription. Works exactly like
    /// TvdbV4SeriesFetcher: the same two entry points (Search, FetchAndCacheSeries), and the same cache files in
    /// the same shape ({lang}.xml with the series and its episodes, banners.xml, actors.xml), so nothing else in
    /// the plugin needs to know where the information came from.
    ///
    /// Shows keep TheTVDB's numbers: TMDB records the TheTVDB number of most shows (external_ids.tvdb_id), and
    /// shows are looked up by it (/find/{id}?external_source=tvdb_id). A show TMDB has without a TheTVDB number
    /// can't be stored in the library yet; it is left out of search results, with a line in the log.
    ///
    /// TMDB has no wide banners; those come from fanart.tv, which knows shows by their TheTVDB number too.
    /// </summary>
    static class TmdbSeriesFetcher
    {
        // TheTVDB number <-> TMDB number, remembered for this session (each costs a request to find out)
        static readonly Dictionary<int, int> sTmdbByTvdb = new Dictionary<int, int>();
        static readonly Dictionary<int, int> sTvdbByTmdb = new Dictionary<int, int>();
        static readonly object sIdLock = new object();

        // picture sizes: close to TheTVDB's (posters 680x1000, episode pictures 400x225) without downloading
        // TMDB's full-size originals for every one
        const string PosterSize = "w780";
        const string PosterThumbSize = "w342";
        const string StillSize = "w300";
        const string ProfileSize = "w185";
        const string BackdropThumbSize = "w300";

        // ------------------------------------------------------------------
        // Search
        // ------------------------------------------------------------------

        /// <summary>Searches TMDB and returns the results in the same &lt;Data&gt;&lt;Series&gt; shape as TheTVDB's search.</summary>
        public static XmlNode Search(string seriesName, string languageCode)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Data");
            doc.AppendChild(root);

            string lang = LanguageParam(languageCode);
            var reply = JsonHelpers.AsObject(TmdbClient.Get(string.Format("/search/tv?query={0}&language={1}&include_adult=false&page=1",
                Uri.EscapeDataString(seriesName ?? string.Empty), lang)));
            List<object> results = JsonHelpers.GetArray(reply, "results");
            if (results == null) return root;

            int used = 0;
            foreach (object item in results)
            {
                if (used >= 10) break; // each result costs one more request (for its TheTVDB number)
                var r = JsonHelpers.AsObject(item);
                if (r == null) continue;
                used++;

                int tmdbId = JsonHelpers.GetInt(r, "id");
                string name = JsonHelpers.GetString(r, "name");
                string firstAired = JsonHelpers.GetString(r, "first_air_date");

                int tvdbId = TvdbIdFor(tmdbId);
                if (tvdbId <= 0)
                {
                    MPTVSeriesLog.Write(string.Format("TMDB: \"{0}\"{1} has no TheTVDB number on TMDB, so it can't be added yet - left out of the results",
                        name, firstAired.Length >= 4 ? " (" + firstAired.Substring(0, 4) + ")" : string.Empty));
                    continue;
                }

                XmlElement series = doc.CreateElement("Series");
                root.AppendChild(series);
                Append(doc, series, "seriesid", tvdbId.ToString());
                Append(doc, series, "SeriesName", name);
                Append(doc, series, "Overview", JsonHelpers.GetString(r, "overview"));
                Append(doc, series, "FirstAired", firstAired);
                Append(doc, series, "Network", string.Empty);
                Append(doc, series, "IMDB_ID", string.Empty);
                Append(doc, series, "banner", string.Empty);
                Append(doc, series, "Status", string.Empty);
                Append(doc, series, "Genre", string.Empty);
                string originalName = JsonHelpers.GetString(r, "original_name");
                Append(doc, series, "AliasNames", !string.IsNullOrEmpty(originalName) && originalName != name ? "|" + originalName + "|" : string.Empty);
                Append(doc, series, "language", languageCode);
                Append(doc, series, "slug", string.Empty);
            }
            return root;
        }

        // ------------------------------------------------------------------
        // Full series: details, every season's episodes, pictures and cast
        // ------------------------------------------------------------------

        /// <summary>
        /// Fetches everything for one show (known by its TheTVDB number) and writes {lang}.xml, banners.xml and
        /// actors.xml into the cache, exactly where TheTVDB's would go. Returns true if the show itself was fetched.
        /// </summary>
        public static bool FetchAndCacheSeries(int tvdbId, string languageCode)
        {
            MPTVSeriesLog.Write(string.Format("TMDB: fetching show with TheTVDB number {0} ({1})", tvdbId, languageCode));

            int tmdbId = TmdbIdFor(tvdbId);
            if (tmdbId <= 0) tmdbId = FindByName(tvdbId);
            if (tmdbId <= 0)
            {
                MPTVSeriesLog.Write(string.Format("TMDB: no show on TMDB has TheTVDB number {0}, and none matched its name - nothing downloaded for it", tvdbId));
                return false;
            }

            string lang = LanguageParam(languageCode);
            string imageLanguages = ImageLanguages(lang);

            var details = JsonHelpers.AsObject(TmdbClient.Get(string.Format(
                "/tv/{0}?language={1}&append_to_response=external_ids,content_ratings,aggregate_credits,images,translations&include_image_language={2}",
                tmdbId, lang, imageLanguages)));
            if (details == null)
            {
                MPTVSeriesLog.Write(string.Format("TMDB: could not get show {0} (TheTVDB {1})", tmdbId, tvdbId));
                return false;
            }

            // every season, specials (season 0) included
            var seasons = new List<Dictionary<string, object>>();
            var seasonsEnglish = new Dictionary<int, Dictionary<string, object>>();
            List<object> seasonList = JsonHelpers.GetArray(details, "seasons");
            if (seasonList != null)
            {
                foreach (object s in seasonList)
                {
                    int number = JsonHelpers.GetInt(JsonHelpers.AsObject(s), "season_number");
                    var season = JsonHelpers.AsObject(TmdbClient.Get(string.Format(
                        "/tv/{0}/season/{1}?language={2}&append_to_response=images&include_image_language={3}", tmdbId, number, lang, imageLanguages)));
                    if (season == null) continue;
                    seasons.Add(season);

                    // not English: TMDB leaves untranslated episode descriptions empty and names as "Episode 5", so
                    // get the English season too when anything is missing, to fill those in
                    if (lang != "en" && NeedsEnglishFallback(season))
                    {
                        var english = JsonHelpers.AsObject(TmdbClient.Get(string.Format("/tv/{0}/season/{1}?language=en", tmdbId, number)));
                        if (english != null) seasonsEnglish[number] = english;
                    }
                }
            }

            string cacheDir = Path.Combine(Settings.GetPath(Settings.Path.config), string.Format(@"Cache\{0}\{1}", tvdbId, languageCode));

            Helper.SaveXmlCache(Path.Combine(cacheDir, languageCode + ".xml"), BuildSeriesXml(tvdbId, details, seasons, seasonsEnglish, languageCode));
            Helper.SaveXmlCache(Path.Combine(cacheDir, "actors.xml"), BuildActorsXml(JsonHelpers.AsObject(JsonHelpers.Get(details, "aggregate_credits"))));
            Helper.SaveXmlCache(Path.Combine(cacheDir, "banners.xml"), BuildBannersXml(tvdbId, details, seasons, languageCode));
            OnlineSource.MarkFetched(tvdbId);
            return true;
        }

        static XmlNode BuildSeriesXml(int tvdbId, Dictionary<string, object> d, List<Dictionary<string, object>> seasons,
                                      Dictionary<int, Dictionary<string, object>> seasonsEnglish, string languageCode)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Data");
            doc.AppendChild(root);

            XmlElement series = doc.CreateElement("Series");
            root.AppendChild(series);

            // name/description in the chosen language, else English, else the original
            var english = EnglishTranslation(d);
            string name = JsonHelpers.GetString(d, "name");
            if (string.IsNullOrEmpty(name)) name = JsonHelpers.GetString(english, "name");
            if (string.IsNullOrEmpty(name)) name = JsonHelpers.GetString(d, "original_name");
            string overview = JsonHelpers.GetString(d, "overview");
            if (string.IsNullOrEmpty(overview)) overview = JsonHelpers.GetString(english, "overview");

            Append(doc, series, "id", tvdbId.ToString());
            Append(doc, series, "SeriesName", name);
            Append(doc, series, "Overview", overview);
            Append(doc, series, "Status", StatusFor(JsonHelpers.GetString(d, "status")));
            Append(doc, series, "Genre", JsonHelpers.JoinStringArray(JsonHelpers.GetArray(d, "genres"), "|"));

            string originalName = JsonHelpers.GetString(d, "original_name");
            Append(doc, series, "AliasNames", !string.IsNullOrEmpty(originalName) && originalName != name ? "|" + originalName + "|" : string.Empty);

            // TMDB has no regular airing day/time; the day of the next (or last) episode is the best guess
            var nextEpisode = JsonHelpers.AsObject(JsonHelpers.Get(d, "next_episode_to_air", "last_episode_to_air"));
            Append(doc, series, "Airs_DayOfWeek", DayOfWeek(JsonHelpers.GetString(nextEpisode, "air_date")));
            Append(doc, series, "Airs_Time", string.Empty);

            Append(doc, series, "FirstAired", JsonHelpers.GetString(d, "first_air_date"));
            var externalIds = JsonHelpers.AsObject(JsonHelpers.Get(d, "external_ids"));
            Append(doc, series, "IMDB_ID", JsonHelpers.GetString(externalIds, "imdb_id"));
            Append(doc, series, "zap2it_id", string.Empty);
            Append(doc, series, "ContentRating", ContentRatingFor(JsonHelpers.AsObject(JsonHelpers.Get(d, "content_ratings")), languageCode));

            var network = FirstObject(JsonHelpers.GetArray(d, "networks"));
            Append(doc, series, "Network", JsonHelpers.GetString(network, "name"));
            Append(doc, series, "NetworkID", JsonHelpers.GetString(network, "id"));

            // community ratings come from Trakt, as with TheTVDB
            Append(doc, series, "Rating", string.Empty);
            Append(doc, series, "RatingCount", string.Empty);
            Append(doc, series, "lastupdated", string.Empty);
            Append(doc, series, "poster", TmdbClient.ImageUrl(PosterSize, JsonHelpers.GetString(d, "poster_path")));
            Append(doc, series, "banner", string.Empty);
            Append(doc, series, "fanart", TmdbClient.ImageUrl("w1280", JsonHelpers.GetString(d, "backdrop_path")));
            Append(doc, series, "slug", string.Empty);
            Append(doc, series, "language", languageCode);

            foreach (var season in seasons)
            {
                int seasonNumber = JsonHelpers.GetInt(season, "season_number");
                Dictionary<string, object> englishSeason;
                seasonsEnglish.TryGetValue(seasonNumber, out englishSeason);
                var englishEpisodes = new Dictionary<int, Dictionary<string, object>>();
                List<object> englishList = JsonHelpers.GetArray(englishSeason, "episodes");
                if (englishList != null)
                    foreach (object e in englishList)
                    {
                        var eo = JsonHelpers.AsObject(e);
                        if (eo != null) englishEpisodes[JsonHelpers.GetInt(eo, "episode_number")] = eo;
                    }

                List<object> episodes = JsonHelpers.GetArray(season, "episodes");
                if (episodes == null) continue;
                foreach (object item in episodes)
                {
                    var ep = JsonHelpers.AsObject(item);
                    if (ep == null) continue;

                    int episodeNumber = JsonHelpers.GetInt(ep, "episode_number");
                    Dictionary<string, object> epEnglish;
                    englishEpisodes.TryGetValue(episodeNumber, out epEnglish);

                    string epName = JsonHelpers.GetString(ep, "name");
                    string epEnglishName = JsonHelpers.GetString(epEnglish, "name");
                    if ((string.IsNullOrEmpty(epName) || IsPlaceholderName(epName)) && !string.IsNullOrEmpty(epEnglishName) && !IsPlaceholderName(epEnglishName))
                        epName = epEnglishName;
                    string epOverview = JsonHelpers.GetString(ep, "overview");
                    if (string.IsNullOrEmpty(epOverview)) epOverview = JsonHelpers.GetString(epEnglish, "overview");

                    XmlElement episodeEl = doc.CreateElement("Episode");
                    root.AppendChild(episodeEl);
                    Append(doc, episodeEl, "id", JsonHelpers.GetString(ep, "id"));
                    Append(doc, episodeEl, "seriesid", tvdbId.ToString());
                    Append(doc, episodeEl, "EpisodeName", epName);
                    Append(doc, episodeEl, "Overview", epOverview);
                    Append(doc, episodeEl, "SeasonNumber", seasonNumber.ToString());
                    Append(doc, episodeEl, "EpisodeNumber", episodeNumber.ToString());
                    Append(doc, episodeEl, "AbsoluteNumber", string.Empty);
                    Append(doc, episodeEl, "FirstAired", JsonHelpers.GetString(ep, "air_date"));
                    Append(doc, episodeEl, "filename", TmdbClient.ImageUrl(StillSize, JsonHelpers.GetString(ep, "still_path")));
                    Append(doc, episodeEl, "Language", languageCode);
                    Append(doc, episodeEl, "IMDB_ID", string.Empty);
                    Append(doc, episodeEl, "ProductionCode", JsonHelpers.GetString(ep, "production_code"));
                    Append(doc, episodeEl, "lastupdated", string.Empty);
                    Append(doc, episodeEl, "GuestStars", JoinNames(JsonHelpers.GetArray(ep, "guest_stars"), null, null));
                    Append(doc, episodeEl, "Director", JoinNames(JsonHelpers.GetArray(ep, "crew"), "job", "Director"));
                    Append(doc, episodeEl, "Writer", JoinNames(JsonHelpers.GetArray(ep, "crew"), "department", "Writing"));
                }
            }
            return root;
        }

        static XmlNode BuildActorsXml(Dictionary<string, object> credits)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Actors");
            doc.AppendChild(root);

            List<object> cast = JsonHelpers.GetArray(credits, "cast");
            if (cast == null) return root;

            int count = 0;
            foreach (object item in cast)
            {
                if (++count > 50) break; // the main cast; long-running shows list hundreds
                var c = JsonHelpers.AsObject(item);
                if (c == null) continue;

                // aggregate_credits lists every role an actor played; the first is the main one
                var role = FirstObject(JsonHelpers.GetArray(c, "roles"));
                XmlElement actor = doc.CreateElement("Actor");
                root.AppendChild(actor);
                Append(doc, actor, "id", JsonHelpers.GetString(c, "id"));
                Append(doc, actor, "Name", JsonHelpers.GetString(c, "name"));
                Append(doc, actor, "Role", role != null ? JsonHelpers.GetString(role, "character") : JsonHelpers.GetString(c, "character"));
                Append(doc, actor, "SortOrder", JsonHelpers.GetString(c, "order"));
                Append(doc, actor, "Image", TmdbClient.ImageUrl(ProfileSize, JsonHelpers.GetString(c, "profile_path")));
            }
            return root;
        }

        static XmlNode BuildBannersXml(int tvdbId, Dictionary<string, object> d, List<Dictionary<string, object>> seasons, string languageCode)
        {
            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Banners");
            doc.AppendChild(root);

            var images = JsonHelpers.AsObject(JsonHelpers.Get(d, "images"));

            List<object> posters = JsonHelpers.GetArray(images, "posters");
            if (posters != null)
                foreach (object item in posters)
                {
                    var p = JsonHelpers.AsObject(item);
                    string path = JsonHelpers.GetString(p, "file_path");
                    if (string.IsNullOrEmpty(path)) continue;
                    AppendBanner(doc, root, path, "poster", "680x1000", TmdbClient.ImageUrl(PosterSize, path), TmdbClient.ImageUrl(PosterThumbSize, path),
                                 JsonHelpers.GetString(p, "iso_639_1"), p, null);
                }

            List<object> backdrops = JsonHelpers.GetArray(images, "backdrops");
            if (backdrops != null)
                foreach (object item in backdrops)
                {
                    var b = JsonHelpers.AsObject(item);
                    string path = JsonHelpers.GetString(b, "file_path");
                    if (string.IsNullOrEmpty(path)) continue;
                    int width = JsonHelpers.GetInt(b, "width"), height = JsonHelpers.GetInt(b, "height");
                    // Full HD pictures are fetched as they are. Larger ones (often 4K, several MB each - far more than a TV
                    // screen needs, and slow over Wi-Fi) are fetched at TMDB's 1280-wide size instead; TMDB offers nothing
                    // between that and the original. The labels match the Fanart "Resolution" choice (1280x720 / 1920x1080).
                    string size, resolution;
                    if (width > 1920) { size = "w1280"; resolution = "1280x720"; }
                    else if (width == 1920) { size = "original"; resolution = "1920x1080"; }
                    else { size = "original"; resolution = width > 0 && height > 0 ? width + "x" + height : "1280x720"; }
                    AppendBanner(doc, root, path, "fanart", resolution, TmdbClient.ImageUrl(size, path), TmdbClient.ImageUrl(BackdropThumbSize, path),
                                 JsonHelpers.GetString(b, "iso_639_1"), b, null);
                }

            foreach (var season in seasons)
            {
                string seasonNumber = JsonHelpers.GetString(season, "season_number");
                List<object> seasonPosters = JsonHelpers.GetArray(JsonHelpers.AsObject(JsonHelpers.Get(season, "images")), "posters");
                if (seasonPosters == null) continue;
                foreach (object item in seasonPosters)
                {
                    var p = JsonHelpers.AsObject(item);
                    string path = JsonHelpers.GetString(p, "file_path");
                    if (string.IsNullOrEmpty(path)) continue;
                    AppendBanner(doc, root, path, "season", "season", TmdbClient.ImageUrl(PosterSize, path), TmdbClient.ImageUrl(PosterThumbSize, path),
                                 JsonHelpers.GetString(p, "iso_639_1"), p, seasonNumber);
                }
            }

            // wide banners: TMDB has none, fanart.tv has them by TheTVDB number
            try
            {
                FanartTvAPI.FanartTvAPI.Init(Settings.UserAgent, "4fe5df4e1a4069867074458c7f9c795e", DBOption.GetOptions(DBOption.cFanartTvClientKey));
                var fanartTv = FanartTvAPI.FanartTvAPI.GetShowImages(tvdbId.ToString());
                if (fanartTv != null && fanartTv.TvBanners != null)
                    foreach (var banner in fanartTv.TvBanners)
                    {
                        if (banner == null || string.IsNullOrEmpty(banner.Url)) continue;
                        string lang = banner.Language == "00" ? string.Empty : (banner.Language ?? string.Empty); // "00" = no text
                        AppendBanner(doc, root, banner.Url, "series", "graphical", banner.Url, banner.Url.Replace("/fanart/", "/preview/"), lang, null, null,
                                     banner.Likes.ToString(CultureInfo.InvariantCulture), "0");
                    }
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write(string.Format("fanart.tv: no wide banners for TheTVDB {0}: {1}", tvdbId, ex.Message));
            }

            return root;
        }

        static void AppendBanner(XmlDocument doc, XmlElement root, string uniqueSource, string type, string type2, string url, string thumbUrl,
                                 string language, Dictionary<string, object> tmdbImage, string season, string rating = null, string ratingCount = null)
        {
            XmlElement banner = doc.CreateElement("Banner");
            root.AppendChild(banner);
            Append(doc, banner, "id", StableId(uniqueSource).ToString());
            Append(doc, banner, "BannerType", type);
            Append(doc, banner, "BannerType2", type2);
            Append(doc, banner, "BannerPath", url);
            Append(doc, banner, "ThumbnailPath", thumbUrl);
            Append(doc, banner, "Language", language ?? string.Empty);
            Append(doc, banner, "Rating", rating ?? (tmdbImage != null ? JsonHelpers.GetString(tmdbImage, "vote_average") : string.Empty));
            Append(doc, banner, "RatingCount", ratingCount ?? (tmdbImage != null ? JsonHelpers.GetInt(tmdbImage, "vote_count").ToString() : "0"));
            if (season != null) Append(doc, banner, "Season", season);
        }

        // ------------------------------------------------------------------
        // TheTVDB <-> TMDB numbers
        // ------------------------------------------------------------------

        /// <summary>TMDB's number for the show with this TheTVDB number, or 0 if TMDB doesn't know it.</summary>
        public static int TmdbIdFor(int tvdbId)
        {
            lock (sIdLock)
            {
                int known;
                if (sTmdbByTvdb.TryGetValue(tvdbId, out known)) return known;
            }

            var reply = JsonHelpers.AsObject(TmdbClient.Get(string.Format("/find/{0}?external_source=tvdb_id", tvdbId)));
            var show = FirstObject(JsonHelpers.GetArray(reply, "tv_results"));
            int tmdbId = JsonHelpers.GetInt(show, "id");

            if (reply != null) Remember(tvdbId, tmdbId); // don't remember a failed request as "not on TMDB"
            return tmdbId;
        }

        /// <summary>TheTVDB's number for the TMDB show with this number, or 0 if TMDB doesn't have one.</summary>
        static int TvdbIdFor(int tmdbId)
        {
            if (tmdbId <= 0) return 0;
            lock (sIdLock)
            {
                int known;
                if (sTvdbByTmdb.TryGetValue(tmdbId, out known)) return known;
            }

            var reply = JsonHelpers.AsObject(TmdbClient.Get(string.Format("/tv/{0}/external_ids", tmdbId)));
            int tvdbId = JsonHelpers.GetInt(reply, "tvdb_id");
            if (reply != null)
            {
                lock (sIdLock) sTvdbByTmdb[tmdbId] = tvdbId;
                if (tvdbId > 0) Remember(tvdbId, tmdbId);
            }
            return tvdbId;
        }

        /// <summary>
        /// For a show TMDB has without its TheTVDB number (common for documentaries): look it up on TMDB by the name
        /// the library knows it by, and use it only when exactly one TMDB show has that name and started in the same
        /// year (give or take one), and isn't linked to a different TheTVDB show. Returns TMDB's number, or 0.
        /// </summary>
        static int FindByName(int tvdbId)
        {
            DBSeries series = null;
            try { series = Helper.getCorrespondingSeries(tvdbId); } catch (Exception) { }
            string hint = OnlineSource.NameHint(tvdbId); // the folder/file name, for a show not in the library yet
            if (series == null && string.IsNullOrEmpty(hint)) return 0;

            var rawNames = new List<string>();
            if (series != null)
                foreach (string field in new[] { DBOnlineSeries.cPrettyName, DBOnlineSeries.cOriginalName, DBSeries.cParsedName })
                    rawNames.Add(series[field]);
            rawNames.Add(hint);

            var names = new List<string>();
            int year = series != null ? YearOf(series[DBOnlineSeries.cFirstAired]) : 0;
            foreach (string raw in rawNames)
            {
                if (year == 0)
                {
                    Match m = Regex.Match(raw ?? string.Empty, @"\((\d{4})\)|\b(19\d\d|20\d\d)$");
                    if (m.Success) year = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                }
                string n = CleanName(raw);
                if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
            }

            foreach (string name in names)
            {
                var reply = JsonHelpers.AsObject(TmdbClient.Get(string.Format("/search/tv?query={0}&include_adult=false&page=1", Uri.EscapeDataString(name))));
                List<object> results = JsonHelpers.GetArray(reply, "results");
                if (results == null) continue;

                string wanted = Normalize(name);
                var matches = new List<Dictionary<string, object>>();
                foreach (object item in results)
                {
                    var r = JsonHelpers.AsObject(item);
                    if (r == null) continue;
                    if (Normalize(CleanName(JsonHelpers.GetString(r, "name"))) != wanted && Normalize(CleanName(JsonHelpers.GetString(r, "original_name"))) != wanted) continue;
                    int resultYear = YearOf(JsonHelpers.GetString(r, "first_air_date"));
                    if (year > 0 && (resultYear == 0 || Math.Abs(resultYear - year) > 1)) continue;
                    matches.Add(r);
                }
                if (matches.Count != 1) continue; // none, or too close to call

                int tmdbId = JsonHelpers.GetInt(matches[0], "id");
                int linkedTvdb = TvdbIdFor(tmdbId);
                if (linkedTvdb > 0 && linkedTvdb != tvdbId) continue; // that one belongs to another TheTVDB show

                MPTVSeriesLog.Write(string.Format("TMDB: \"{0}\" (TheTVDB {1}) has no TheTVDB number on TMDB - matched by name{2} to TMDB show {3}",
                    name, tvdbId, year > 0 ? " and year" : string.Empty, tmdbId));
                Remember(tvdbId, tmdbId);
                return tmdbId;
            }
            return 0;
        }

        /// <summary>A name as the library stores it, made searchable: "Jinx, The (2015) [8.0]" becomes "The Jinx".</summary>
        static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string n = Regex.Replace(name, @"\[[^\]]*\]", " ");
            n = Regex.Replace(n, @"\(\d{4}\)", " ");
            n = Regex.Replace(n, @"\s(19\d\d|20\d\d)$", " ");
            n = Regex.Replace(n, @"\s+", " ").Trim();
            Match the = Regex.Match(n, @"^(.*), (The|A|An)$", RegexOptions.IgnoreCase);
            if (the.Success) n = the.Groups[2].Value + " " + the.Groups[1].Value;
            return n;
        }

        /// <summary>Lower case, letters and digits only, "&amp;" as "and" - so punctuation differences don't matter.</summary>
        static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.Replace("&", " and ").ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        static int YearOf(string date)
        {
            int y;
            return !string.IsNullOrEmpty(date) && date.Length >= 4 && int.TryParse(date.Substring(0, 4), out y) && y > 1900 ? y : 0;
        }

        static void Remember(int tvdbId, int tmdbId)
        {
            lock (sIdLock)
            {
                sTmdbByTvdb[tvdbId] = tmdbId;
                if (tmdbId > 0) sTvdbByTmdb[tmdbId] = tvdbId;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>The plugin keeps two-letter language codes ("en", "de"), which TMDB accepts as they are.</summary>
        static string LanguageParam(string languageCode)
        {
            return string.IsNullOrEmpty(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
        }

        /// <summary>Pictures in the chosen language, English, and pictures without text.</summary>
        static string ImageLanguages(string lang)
        {
            return lang == "en" ? "en,null" : lang + ",en,null";
        }

        static bool NeedsEnglishFallback(Dictionary<string, object> season)
        {
            List<object> episodes = JsonHelpers.GetArray(season, "episodes");
            if (episodes == null) return false;
            foreach (object item in episodes)
            {
                var ep = JsonHelpers.AsObject(item);
                if (ep == null) continue;
                string name = JsonHelpers.GetString(ep, "name");
                if (string.IsNullOrEmpty(JsonHelpers.GetString(ep, "overview")) || string.IsNullOrEmpty(name) || IsPlaceholderName(name))
                    return true;
            }
            return false;
        }

        // "Episode 5", "Folge 5", "Épisode 5"... - what TMDB shows when an episode has no name in that language
        static readonly Regex sPlaceholderName = new Regex(@"^\D{0,20}\s\d+$", RegexOptions.Compiled);

        static bool IsPlaceholderName(string name)
        {
            return !string.IsNullOrEmpty(name) && sPlaceholderName.IsMatch(name.Trim());
        }

        static Dictionary<string, object> EnglishTranslation(Dictionary<string, object> details)
        {
            List<object> translations = JsonHelpers.GetArray(JsonHelpers.AsObject(JsonHelpers.Get(details, "translations")), "translations");
            if (translations == null) return null;
            Dictionary<string, object> anyEnglish = null;
            foreach (object item in translations)
            {
                var t = JsonHelpers.AsObject(item);
                if (t == null || JsonHelpers.GetString(t, "iso_639_1") != "en") continue;
                var data = JsonHelpers.AsObject(JsonHelpers.Get(t, "data"));
                if (JsonHelpers.GetString(t, "iso_3166_1") == "US") return data;
                if (anyEnglish == null) anyEnglish = data;
            }
            return anyEnglish;
        }

        /// <summary>TMDB's status in the words TheTVDB used, which the plugin and skins understand.</summary>
        static string StatusFor(string tmdbStatus)
        {
            switch (tmdbStatus)
            {
                case "Returning Series": return "Continuing";
                case "Ended":
                case "Canceled": return "Ended";
                case "In Production":
                case "Planned":
                case "Pilot": return "Upcoming";
                default: return tmdbStatus ?? string.Empty;
            }
        }

        static string ContentRatingFor(Dictionary<string, object> contentRatings, string languageCode)
        {
            List<object> results = JsonHelpers.GetArray(contentRatings, "results");
            if (results == null || results.Count == 0) return string.Empty;

            string country = CountryFor(languageCode);
            string us = null, first = null;
            foreach (object item in results)
            {
                var r = JsonHelpers.AsObject(item);
                if (r == null) continue;
                string rating = JsonHelpers.GetString(r, "rating");
                if (string.IsNullOrEmpty(rating)) continue;
                string c = JsonHelpers.GetString(r, "iso_3166_1");
                if (c == country) return rating;
                if (c == "US" && us == null) us = rating;
                if (first == null) first = rating;
            }
            return us ?? first ?? string.Empty;
        }

        static string CountryFor(string languageCode)
        {
            switch (LanguageParam(languageCode))
            {
                case "en": return "US";
                case "da": return "DK";
                case "sv": return "SE";
                case "nb":
                case "no": return "NO";
                case "cs": return "CZ";
                case "el": return "GR";
                case "ja": return "JP";
                case "ko": return "KR";
                case "zh": return "CN";
                case "he": return "IL";
                default: return LanguageParam(languageCode).ToUpperInvariant();
            }
        }

        static string DayOfWeek(string date)
        {
            DateTime d;
            if (string.IsNullOrEmpty(date) || !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return string.Empty;
            return d.DayOfWeek.ToString();
        }

        static string JoinNames(List<object> people, string field, string value)
        {
            if (people == null) return string.Empty;
            var names = new List<object>();
            foreach (object item in people)
            {
                var p = JsonHelpers.AsObject(item);
                if (p == null) continue;
                if (field != null && JsonHelpers.GetString(p, field) != value) continue;
                string name = JsonHelpers.GetString(p, "name");
                if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
            }
            return JsonHelpers.JoinStringArray(names, "|");
        }

        static Dictionary<string, object> FirstObject(List<object> list)
        {
            if (list == null) return null;
            foreach (object item in list)
            {
                var o = JsonHelpers.AsObject(item);
                if (o != null) return o;
            }
            return null;
        }

        /// <summary>
        /// Pictures need a whole-number id (the fanart table is keyed by it). TMDB's pictures have none, so one is
        /// made from the picture's address: always the same for the same picture, and in a range far above
        /// TheTVDB's own picture numbers so the two never collide.
        /// </summary>
        static int StableId(string source)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char ch in source ?? string.Empty)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
                return (int)(hash % 600000000u) + 1500000000;
            }
        }

        static void Append(XmlDocument doc, XmlElement parent, string name, string value)
        {
            XmlElement el = doc.CreateElement(name);
            el.InnerText = value ?? string.Empty;
            parent.AppendChild(el);
        }
    }
}
