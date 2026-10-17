using System;
using System.Collections.Generic;

namespace WindowPlugins.GUITVSeries
{
    /// <summary>
    /// Decides which of a series' (or season's) artwork entries to keep, and which picture should be the
    /// selected one, so the Details tab and the skin always have a picture to show.
    ///
    ///  - pictures whose file is missing (or, with "check artwork" ticked, damaged) come off the list
    ///  - a selected picture that is missing is replaced with a good one from the list
    ///
    /// (Wrong-show pictures are not guessed from file names: TheTVDB's oldest artwork is numbered with an
    /// internal counter, not the series ID - e.g. Babylon 5's "11-g2.jpg" - so a name can't tell. Pictures of a
    /// previously wrong match are cleared when the series ID is corrected instead.)
    ///
    /// Only database entries are changed; no picture files are ever deleted.
    /// </summary>
    public static class ArtworkRepair
    {
        public class Result
        {
            public List<string> Keep = new List<string>();
            public string Selected;          // relative to the artwork folder, as stored in the database
            public int RemovedMissing;
            public bool SelectionChanged;
            public bool Changed { get { return RemovedMissing > 0 || SelectionChanged; } }
        }

        /// <param name="list">full paths, as returned by BannerList / PosterList</param>
        /// <param name="selectedRaw">the stored selection (relative, or occasionally a full path)</param>
        /// <param name="seriesId">TheTVDB ID of the series</param>
        /// <param name="bannersRoot">the artwork folder</param>
        /// <param name="checkEveryFile">the "check artwork exists and is valid" option: open every file in the list</param>
        /// <param name="fileExists">File.Exists</param>
        /// <param name="isGoodImage">exists and opens as an image</param>
        public static Result Repair(List<string> list, string selectedRaw, string seriesId, string bannersRoot, bool checkEveryFile,
                                    Func<string, bool> fileExists, Func<string, bool> isGoodImage, Action<string> log)
        {
            var r = new Result();

            foreach (string path in list)
            {
                if (checkEveryFile && !isGoodImage(path))
                {
                    r.RemovedMissing++;
                    if (log != null) log("Removing missing or damaged artwork from database: " + path);
                }
                else
                    r.Keep.Add(path);
            }

            r.Selected = selectedRaw ?? string.Empty;
            if (r.Selected.Trim().Length == 0)
            {
                // nothing selected but good pictures available: pick the first (the list is in TheTVDB's order)
                if (r.Keep.Count > 0) Select(r, r.Keep[0], bannersRoot);
                return r;
            }

            string selectedFull = IsFullPath(r.Selected) ? r.Selected : Combine(bannersRoot, r.Selected);
            bool selectionBad = !fileExists(selectedFull) || (checkEveryFile && !isGoodImage(selectedFull));

            if (selectionBad)
            {
                if (log != null) log("Selected artwork is missing, choosing another: " + selectedFull);

                // a selection already known to be bad comes off the list too
                int idx = r.Keep.FindIndex(k => string.Equals(k.TrimStart('\\', '/'), selectedFull.TrimStart('\\', '/'), StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(ToRelative(k, bannersRoot), ToRelative(selectedFull, bannersRoot), StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    r.Keep.RemoveAt(idx);
                    r.RemovedMissing++;
                }

                // replacement: the first picture actually on disk (without the tick, the list hasn't been checked)
                Select(r, r.Keep.Find(k => checkEveryFile || fileExists(k)), bannersRoot);
            }
            return r;
        }

        static void Select(Result r, string fullPath, string bannersRoot)
        {
            string relative = fullPath == null ? string.Empty : ToRelative(fullPath, bannersRoot);
            if (relative != r.Selected)
            {
                r.Selected = relative;
                r.SelectionChanged = true;
            }
        }

        static bool IsFullPath(string p)
        {
            return p.StartsWith(@"\\") || (p.Length > 2 && p[1] == ':') || p.StartsWith("/");
        }

        static string Combine(string root, string relative)
        {
            if (string.IsNullOrEmpty(root)) return relative;
            return root.TrimEnd('\\', '/') + @"\" + relative.TrimStart('\\', '/');
        }

        static string ToRelative(string fullPath, string root)
        {
            if (!string.IsNullOrEmpty(root) && fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(root.Length).TrimStart('\\', '/');
            return fullPath;
        }
    }
}
