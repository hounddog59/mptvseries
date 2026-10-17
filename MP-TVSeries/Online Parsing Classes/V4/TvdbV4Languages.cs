using System;
using System.Collections.Generic;
using System.Globalization;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4
{
    /// <summary>
    /// Language codes for TheTVDB v4.
    ///
    /// The plugin has always stored two-letter codes ("en", "fr" - the v3 style), in its settings and per
    /// series, and uses them in cache folder names. TheTVDB v4 identifies languages by three-letter codes
    /// ("eng", "fra") instead. Sending a two-letter code meant v4 never recognised it, so translations were
    /// never found and foreign-language shows appeared under their original-language titles (e.g. "The
    /// Diplomat" as "Diplomatene"). Stored values stay two-letter for compatibility; they're converted here
    /// whenever v4 is asked for something.
    /// </summary>
    static class TvdbV4Languages
    {
        static readonly object sync = new object();
        static List<Language> cached;
        static Dictionary<string, string> shortToV4; // "en" -> "eng", from TheTVDB's own list

        /// <summary>TheTVDB v4's languages, for the settings screen. Empty if TheTVDB couldn't be reached.</summary>
        public static List<Language> GetAll()
        {
            lock (sync)
            {
                if (cached != null) return cached;

                var list = new List<Language>();
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var data = JsonHelpers.AsArray(TvdbV4Client.GetData(DBOnlineMirror.cApiKey, DBOption.GetOptions(DBOption.cTvdbV4Pin), "/languages"));
                    if (data != null)
                    {
                        foreach (object item in data)
                        {
                            var l = JsonHelpers.AsObject(item);
                            if (l == null) continue;

                            string v4Code = JsonHelpers.GetString(l, "id");
                            string shortCode = JsonHelpers.GetString(l, "shortCode");
                            if (string.IsNullOrEmpty(v4Code)) continue;

                            // settings and per-series values are stored two-letter: use TheTVDB's short code,
                            // or work one out from the three-letter code when it has none
                            string twoLetter = shortCode.Length == 2 ? shortCode.ToLowerInvariant() : TwoLetterFor(v4Code);
                            if (string.IsNullOrEmpty(twoLetter) || map.ContainsKey(twoLetter)) continue;
                            map[twoLetter] = v4Code;

                            string englishName = JsonHelpers.GetString(l, "name");
                            string nativeName = JsonHelpers.GetString(l, "nativeName");
                            list.Add(new Language
                            {
                                Id = list.Count + 1,
                                Abbreviation = twoLetter,
                                EnglishName = string.IsNullOrEmpty(englishName) ? twoLetter : englishName,
                                Name = string.IsNullOrEmpty(nativeName) ? englishName : nativeName
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write("TVDB v4: could not get the list of languages: " + ex.Message);
                }

                list.Sort((a, b) => string.Compare(a.EnglishName, b.EnglishName, StringComparison.OrdinalIgnoreCase));

                // only remember a successful result, so a temporary failure is retried next time
                if (list.Count > 0)
                {
                    cached = list;
                    shortToV4 = map;
                }
                return list;
            }
        }

        /// <summary>
        /// Converts a stored language code ("en") to TheTVDB v4's ("eng"). Three-letter codes are returned
        /// unchanged; anything unrecognisable becomes English.
        /// </summary>
        public static string ToV4Code(string code)
        {
            if (string.IsNullOrEmpty(code)) return "eng";
            code = code.Trim().ToLowerInvariant();
            if (code.Length == 3) return code;

            lock (sync)
            {
                string v4;
                if (shortToV4 != null && shortToV4.TryGetValue(code, out v4)) return v4;
            }

            try
            {
                string three = new CultureInfo(code).ThreeLetterISOLanguageName;
                if (!string.IsNullOrEmpty(three) && three.Length == 3 && three != "ivl") return three;
            }
            catch (ArgumentException) { /* not a culture .NET knows */ }

            return "eng";
        }

        static string TwoLetterFor(string v4Code)
        {
            foreach (CultureInfo c in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
            {
                if (string.Equals(c.ThreeLetterISOLanguageName, v4Code, StringComparison.OrdinalIgnoreCase) && c.TwoLetterISOLanguageName.Length == 2)
                    return c.TwoLetterISOLanguageName;
            }
            return null;
        }
    }
}
