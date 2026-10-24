using System;
using System.Collections.Generic;
using WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.Tmdb
{
    /// <summary>TMDB's list of languages, in the same form as TheTVDB's (two-letter codes, as the settings store them).</summary>
    static class TmdbLanguages
    {
        static readonly object sync = new object();
        static List<Language> cached;

        public static List<Language> GetAll()
        {
            lock (sync)
            {
                if (cached != null) return new List<Language>(cached);

                var list = new List<Language>();
                List<object> languages = JsonHelpers.AsArray(TmdbClient.Get("/configuration/languages"));
                if (languages != null)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (object item in languages)
                    {
                        var l = JsonHelpers.AsObject(item);
                        string code = JsonHelpers.GetString(l, "iso_639_1");
                        if (string.IsNullOrEmpty(code) || code == "xx" || !seen.Add(code)) continue; // "xx" = "no language"

                        string englishName = JsonHelpers.GetString(l, "english_name");
                        string nativeName = JsonHelpers.GetString(l, "name");
                        list.Add(new Language
                        {
                            Id = list.Count + 1,
                            Abbreviation = code.ToLowerInvariant(),
                            EnglishName = string.IsNullOrEmpty(englishName) ? code : englishName,
                            Name = string.IsNullOrEmpty(nativeName) ? englishName : nativeName
                        });
                    }
                }
                else
                {
                    MPTVSeriesLog.Write("TMDB: could not get the list of languages");
                }

                list.Sort((a, b) => string.Compare(a.EnglishName, b.EnglishName, StringComparison.OrdinalIgnoreCase));
                if (list.Count > 0) cached = list; // a failure is tried again next time
                return new List<Language>(list);
            }
        }
    }
}
