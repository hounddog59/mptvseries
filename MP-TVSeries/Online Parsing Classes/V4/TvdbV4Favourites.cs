using System;
using System.Collections.Generic;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4
{
    /// <summary>
    /// The user's favourite series on TheTVDB, through its current (v4) service.
    ///
    /// Favourites belong to the TheTVDB account the subscriber PIN logs in as (the old "Account ID" is no longer
    /// used). TheTVDB v4 can list favourites and add one, but has no way to remove one - that has to be done on
    /// thetvdb.com itself.
    /// </summary>
    static class TvdbV4Favourites
    {
        /// <summary>True when a subscriber PIN is set - without one there is no TheTVDB account to read favourites from.</summary>
        public static bool IsAvailable
        {
            get { return !string.IsNullOrEmpty((string)DBOption.GetOptions(DBOption.cTvdbV4Pin)); }
        }

        /// <summary>The TheTVDB IDs of the user's favourite series, or null if they couldn't be fetched.</summary>
        public static List<string> GetSeriesIds()
        {
            if (!IsAvailable) return null;

            object data;
            try
            {
                data = TvdbV4Client.GetData(DBOnlineMirror.cApiKey, DBOption.GetOptions(DBOption.cTvdbV4Pin), "/user/favorites");
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("TVDB v4: could not get your favourites: " + ex.Message);
                return null;
            }
            if (data == null) return null;

            // "data" is a favourites record; accept it wrapped in a list too
            var record = JsonHelpers.AsObject(data);
            if (record == null)
            {
                var list = JsonHelpers.AsArray(data);
                if (list != null && list.Count > 0) record = JsonHelpers.AsObject(list[0]);
            }
            if (record == null) return null;

            var ids = new List<string>();
            var series = JsonHelpers.GetArray(record, "series");
            if (series != null)
            {
                foreach (object id in series)
                {
                    if (id == null) continue;
                    string s = Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture);
                    int dot = s.IndexOf('.');
                    if (dot > 0) s = s.Substring(0, dot); // numbers may come back as 12345.0
                    if (!string.IsNullOrEmpty(s) && !ids.Contains(s)) ids.Add(s);
                }
            }
            return ids;
        }

        /// <summary>Adds a series to the user's TheTVDB favourites. Returns whether TheTVDB accepted it.</summary>
        public static bool AddSeries(string seriesId)
        {
            int id;
            if (!IsAvailable || !int.TryParse(seriesId, out id) || id <= 0) return false;

            return TvdbV4Client.PostData(DBOnlineMirror.cApiKey, DBOption.GetOptions(DBOption.cTvdbV4Pin), "/user/favorites", "{\"series\":" + id + "}");
        }
    }
}
