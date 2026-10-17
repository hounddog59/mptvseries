using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace WindowPlugins.GUITVSeries
{
    /// <summary>
    /// Looks for a TheTVDB series id in show-level metadata files that sit alongside a show's video files
    /// (an "<id>.tvdb" file, or the tvshow.nfo / series.xml files Media Center Master writes), so a series can be identified
    /// from an id that's already known instead of fuzzy-matching its folder name against TheTVDB.
    ///
    /// Deliberately conservative - a wrong id here silently identifies a show as something else, which is
    /// worse than falling back to the normal name search. In particular Media Center Master also writes
    /// per-EPISODE .xml/.nfo files whose &lt;id&gt; is the episode's id, so only these exact show-level
    /// files are trusted, and each must have the expected root element.
    /// </summary>
    internal static class LocalSeriesIdFinder
    {
        // the season folder, the show folder, and one more level for nested layouts - but not further, so
        // we never wander up into the library root and pick up something that isn't about this show
        private const int MaxLevelsUp = 2;

        // series folders can have dozens of season folders; the show-level file will be found from the
        // first few, so don't touch a network share more than needed
        private const int MaxStartFolders = 3;

        private const long MaxXmlBytes = 2 * 1024 * 1024;
        private const long MaxTxtBytes = 4 * 1024;

        // .txt files whose whole content is expected to be just the id
        private static readonly string[] BareIdTxtNames =
            { "tvdb.txt", "tvdbid.txt", "tvdb_id.txt", "seriesid.txt", "series_id.txt" };

        // any other .txt is only trusted if it labels the number as a TVDB / series id, e.g. "tvdbid=84373"
        private static readonly Regex LabelledId = new Regex(
            @"\b(?:tvdb(?:[\s_-]?id)?|thetvdb|series[\s_-]?id)\b\s*[:=]\s*(\d{1,9})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Returns the TheTVDB series id found for the given local files, or 0 if none was found.
        /// <paramref name="source"/> is the path of the file the id came from (for logging).
        /// </summary>
        public static int FindSeriesId(IEnumerable<string> localFilePaths, out string source)
        {
            source = null;
            if (localFilePaths == null) return 0;

            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int startFolders = 0;

            foreach (string file in localFilePaths)
            {
                string startDir = SafeGetDirectory(file);
                if (startDir == null || tried.Contains(startDir)) continue;
                if (++startFolders > MaxStartFolders) break;

                string dir = startDir;
                for (int level = 0; level <= MaxLevelsUp && dir != null; level++, dir = SafeGetParent(dir))
                {
                    if (!tried.Add(dir)) continue;

                    int id;
                    string found;
                    if (TryFindInFolder(dir, out id, out found))
                    {
                        source = found;
                        return id;
                    }
                }
            }
            return 0;
        }

        private static bool TryFindInFolder(string dir, out int id, out string source)
        {
            id = 0;
            source = null;

            try
            {
                if (!Directory.Exists(dir)) return false;

                // "<id>.tvdb" (e.g. "371572.tvdb") - an empty file whose name is the id, the workaround used by the
                // official MP-TVSeries 4.5.1 release. Someone made it deliberately, so it comes first. Ignored if a
                // folder holds two different ones, since then it isn't clear which is meant.
                int tvdbFileId = 0;
                string tvdbFile = null;
                foreach (string candidate in Directory.EnumerateFiles(dir, "*.tvdb"))
                {
                    int n;
                    if (!int.TryParse(Path.GetFileNameWithoutExtension(candidate), out n) || n <= 0) continue;
                    if (tvdbFileId != 0 && tvdbFileId != n)
                    {
                        MPTVSeriesLog.Write("Two different .tvdb id files in '" + dir + "', ignoring both");
                        tvdbFileId = -1;
                        break;
                    }
                    tvdbFileId = n;
                    tvdbFile = candidate;
                }
                if (tvdbFileId > 0)
                {
                    id = tvdbFileId;
                    source = tvdbFile;
                    return true;
                }

                string path = Path.Combine(dir, "tvshow.nfo");
                if (File.Exists(path) && TryReadXmlId(path, new[] { "tvshow" }, out id))
                {
                    source = path;
                    return true;
                }

                path = Path.Combine(dir, "series.xml");
                if (File.Exists(path) && TryReadXmlId(path, new[] { "series", "tvshow" }, out id))
                {
                    source = path;
                    return true;
                }

                foreach (string txt in Directory.EnumerateFiles(dir, "*.txt"))
                {
                    if (TryReadTxtId(txt, out id))
                    {
                        source = txt;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                // an unreachable/odd folder must never break identification - it just means "not found here"
                MPTVSeriesLog.Write("Looking for a local series id in '" + dir + "' failed: " + ex.Message, MPTVSeriesLog.LogLevel.Debug);
            }

            id = 0;
            return false;
        }

        private static bool TryReadXmlId(string path, string[] allowedRootNames, out int id)
        {
            id = 0;
            try
            {
                if (new FileInfo(path).Length > MaxXmlBytes) return false;

                bool wellFormed;
                if (TryReadIdStrict(path, allowedRootNames, out id, out wellFormed))
                    return true;

                // Some tools write files that aren't valid XML - Media Center Master's tvshow.nfo, for one,
                // can have a stray closing </role> tag in its actor entries. Structure can't be trusted in a
                // broken file, so only then fall back to a narrow text match. A file that parsed fine but had
                // the wrong root or no id is NOT second-guessed.
                if (!wellFormed && TryReadIdTolerant(path, allowedRootNames, out id))
                    return true;
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Could not read series id from '" + path + "': " + ex.Message, MPTVSeriesLog.LogLevel.Debug);
            }

            id = 0;
            return false;
        }

        private static bool TryReadIdStrict(string path, string[] allowedRootNames, out int id, out bool wellFormed)
        {
            id = 0;
            wellFormed = true;

            var doc = new XmlDocument();
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(path, settings))
                    doc.Load(reader);
            }
            catch (XmlException)
            {
                wellFormed = false;
                return false;
            }

            XmlElement root = doc.DocumentElement;
            if (root == null || !allowedRootNames.Contains(root.LocalName, StringComparer.OrdinalIgnoreCase))
                return false;

            // most specific first: newer Kodi-style <uniqueid type="tvdb">, then the plain <id> that
            // both tvshow.nfo and series.xml use for the show itself. (<episodeguide> etc. are ignored.)
            string[] xpaths =
            {
                "//uniqueid[translate(@type,'TVDB','tvdb')='tvdb']",
                "/*/tvdbid",
                "/*/id",
                "/*/seriesid"
            };

            foreach (string xpath in xpaths)
            {
                XmlNode node = doc.SelectSingleNode(xpath);
                if (node != null && TryParseId(node.InnerText, out id))
                    return true;
            }

            id = 0;
            return false;
        }

        // the first real element after the <?xml?> declaration / any comments
        private static readonly Regex RootElement = new Regex(@"<\s*([A-Za-z_][\w.\-]*)[\s>/]", RegexOptions.Compiled);

        // the show-level id tags; nested blocks (actors) can carry their own <id>, so the search is cut off
        // at the first <actor> block - see below
        private static readonly Regex ShowLevelId = new Regex(@"<(?:id|tvdbid|seriesid)>\s*(\d{1,9})\s*</(?:id|tvdbid|seriesid)>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ActorBlockStart = new Regex(@"<actor[\s>]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool TryReadIdTolerant(string path, string[] allowedRootNames, out int id)
        {
            id = 0;
            string text = File.ReadAllText(path);

            Match root = RootElement.Match(text);
            if (!root.Success || !allowedRootNames.Contains(root.Groups[1].Value, StringComparer.OrdinalIgnoreCase))
                return false;

            // only the part before the first <actor> block, so an actor's own <id> can never be picked up
            Match actor = ActorBlockStart.Match(text);
            string showLevel = actor.Success ? text.Substring(0, actor.Index) : text;

            Match m = ShowLevelId.Match(showLevel);
            return m.Success && TryParseId(m.Groups[1].Value, out id);
        }

        private static bool TryReadTxtId(string path, out int id)
        {
            id = 0;
            try
            {
                if (new FileInfo(path).Length > MaxTxtBytes) return false;

                string text = File.ReadAllText(path).Trim();
                string name = Path.GetFileName(path);

                if (BareIdTxtNames.Contains(name, StringComparer.OrdinalIgnoreCase) && TryParseId(text, out id))
                    return true;

                Match m = LabelledId.Match(text);
                if (m.Success && TryParseId(m.Groups[1].Value, out id))
                    return true;
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Could not read series id from '" + path + "': " + ex.Message, MPTVSeriesLog.LogLevel.Debug);
            }

            id = 0;
            return false;
        }

        private static bool TryParseId(string text, out int id)
        {
            return int.TryParse((text ?? string.Empty).Trim(), out id) && id > 0;
        }

        private static string SafeGetDirectory(string file)
        {
            try { return string.IsNullOrEmpty(file) ? null : Path.GetDirectoryName(file); }
            catch (Exception) { return null; }
        }

        private static string SafeGetParent(string dir)
        {
            try { return Path.GetDirectoryName(dir); }
            catch (Exception) { return null; }
        }
    }
}
