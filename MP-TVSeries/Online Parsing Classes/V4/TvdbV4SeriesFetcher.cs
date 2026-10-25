using System;
using System.Collections.Generic;
using System.IO;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4
{
    /// <summary>
    /// Replaces the old "download series/{id}/all/{lang}.zip, unpack en.xml + banners.xml +
    /// actors.xml to disk" step. v4 has no equivalent single bundle, so this fetches the
    /// pieces separately (series extended, episodes, characters, artworks) and writes them
    /// into the SAME cache file locations (Cache\{seriesID}\{lang}\{en.xml,banners.xml,actors.xml})
    /// using the v3-shaped XML from TvdbV4XmlAdapter. Everything downstream of the cache
    /// (getFromCache, GetEpisodes, GetActors, GetBanner, worker_DoWork) is unaware anything
    /// changed.
    /// </summary>
    static class TvdbV4SeriesFetcher
    {
        static Dictionary<int, string> sArtworkTypeNamesCache;
        static readonly object sArtworkTypesLock = new object();

        /// <summary>
        /// Fetches everything for one series/language and writes en.xml (well, "{lang}.xml"),
        /// banners.xml and actors.xml into the cache directory, mirroring what the old
        /// zip-unpack step did. Returns true if at least the series record was fetched.
        /// </summary>
        public static bool FetchAndCacheSeries(int seriesId, string languageCode)
        {
            string apiKey = DBOnlineMirror.cApiKey;
            string pin = DBOption.GetOptions(DBOption.cTvdbV4Pin);

            MPTVSeriesLog.Write(string.Format("TVDB v4: fetching series {0} ({1})", seriesId, languageCode));

            object seriesExtendedRaw;
            try
            {
                seriesExtendedRaw = TvdbV4Client.GetData(apiKey, pin, string.Format("/series/{0}/extended?meta=translations", seriesId));
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write(string.Format("TVDB v4: failed to fetch series {0}: {1}", seriesId, ex.Message));
                return false;
            }

            var seriesExtended = JsonHelpers.AsObject(seriesExtendedRaw);
            if (seriesExtended == null)
            {
                MPTVSeriesLog.Write(string.Format("TVDB v4: no series data returned for {0}", seriesId));
                return false;
            }

            // Episodes: v4 paginates (typically 500/page) via the "default" season-type order.
            // aired-order episodes with the requested language's translations where available.
            List<object> episodes = null;
            try
            {
                episodes = TvdbV4Client.GetAllPages(apiKey, pin,
                    string.Format("/series/{0}/episodes/default/{1}", seriesId, TvdbV4Languages.ToV4Code(languageCode)), "episodes");
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write(string.Format("TVDB v4: failed to fetch episodes for series {0}: {1}", seriesId, ex.Message));
            }

            var fullSeriesXml = TvdbV4XmlAdapter.BuildFullSeriesXml(seriesExtended, episodes, languageCode);
            string seriesFilename = Path.Combine(Settings.GetPath(Settings.Path.config),
                string.Format(@"Cache\{0}\{1}\{2}", seriesId, languageCode, languageCode + ".xml"));
            Helper.SaveXmlCache(seriesFilename, fullSeriesXml);

            // Characters -> actors.xml. seriesExtended embeds a "characters" array directly.
            List<object> characters = JsonHelpers.GetArray(seriesExtended, "characters");
            var actorsXml = TvdbV4XmlAdapter.BuildActorsXml(characters);
            string actorsFilename = Path.Combine(Settings.GetPath(Settings.Path.config),
                string.Format(@"Cache\{0}\{1}\{2}", seriesId, languageCode, "actors.xml"));
            Helper.SaveXmlCache(actorsFilename, actorsXml);

            // Artworks -> banners.xml. seriesExtended embeds an "artworks" array when
            // requested with the right "meta"/"include" query params; if it's missing here,
            // fall back to the dedicated endpoint.
            List<object> artworks = JsonHelpers.GetArray(seriesExtended, "artworks");
            if (artworks == null)
            {
                try
                {
                    object artworksRaw = TvdbV4Client.GetData(apiKey, pin, string.Format("/series/{0}/artworks", seriesId));
                    artworks = JsonHelpers.GetArray(JsonHelpers.AsObject(artworksRaw), "artworks");
                }
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write(string.Format("TVDB v4: failed to fetch artworks for series {0}: {1}", seriesId, ex.Message));
                }
            }

            var artworkTypeNames = GetArtworkTypeNames(apiKey, pin);
            var bannersXml = TvdbV4XmlAdapter.BuildBannersXml(artworks, artworkTypeNames);
            string bannersFilename = Path.Combine(Settings.GetPath(Settings.Path.config),
                string.Format(@"Cache\{0}\{1}\{2}", seriesId, languageCode, "banners.xml"));
            Helper.SaveXmlCache(bannersFilename, bannersXml);

            OnlineSource.MarkFetched(seriesId);
            return true;
        }

        /// <summary>
        /// v4 artwork "type" is a numeric id whose meaning is defined by /artwork/types
        /// (not guaranteed stable/hardcodable), so we fetch and cache the id->name map once
        /// per plugin session.
        /// </summary>
        static Dictionary<int, string> GetArtworkTypeNames(string apiKey, string pin)
        {
            lock (sArtworkTypesLock)
            {
                if (sArtworkTypeNamesCache != null) return sArtworkTypeNamesCache;

                var map = new Dictionary<int, string>();
                try
                {
                    object raw = TvdbV4Client.GetData(apiKey, pin, "/artwork/types");
                    List<object> types = JsonHelpers.AsArray(raw);
                    if (types != null)
                    {
                        foreach (object item in types)
                        {
                            var t = JsonHelpers.AsObject(item);
                            if (t == null) continue;
                            int id = JsonHelpers.GetInt(t, "id");
                            string name = JsonHelpers.GetString(t, "name");
                            if (id != 0 && !string.IsNullOrEmpty(name)) map[id] = name;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write("TVDB v4: failed to fetch /artwork/types, banner classification will be limited: " + ex.Message);
                }

                sArtworkTypeNamesCache = map;
                return sArtworkTypeNamesCache;
            }
        }

        /// <summary>
        /// Runs a v4 search and returns it as a v3-shaped &lt;Data&gt;&lt;Series&gt;...
        /// document, matching what GetSeries.cs expects from OnlineAPI.GetSeries(...).
        /// </summary>
        public static System.Xml.XmlNode Search(string seriesName, string languageCode)
        {
            string apiKey = DBOnlineMirror.cApiKey;
            string pin = DBOption.GetOptions(DBOption.cTvdbV4Pin);

            string query = Uri.EscapeDataString(seriesName);
            object raw;
            try
            {
                // No "language" filter here: in v4 it means "only shows whose ORIGINAL language is this", which would
                // hide every foreign-language show. Translated names are picked from the results instead.
                raw = TvdbV4Client.GetData(apiKey, pin, string.Format("/search?query={0}&type=series", query));
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write(string.Format("TVDB v4: search failed for '{0}': {1}", seriesName, ex.Message));
                return null;
            }

            List<object> results = JsonHelpers.AsArray(raw);
            return TvdbV4XmlAdapter.BuildSearchResultsXml(results, languageCode);
        }
    }
}
