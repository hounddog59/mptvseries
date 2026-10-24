namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes
{
    /// <summary>
    /// Where show information comes from: TheTVDB (needs each user's own subscriber PIN) or TMDB (free).
    ///
    /// "Automatic" (the default) uses TheTVDB when a PIN has been entered and TMDB otherwise, so nobody has to
    /// choose anything: subscribers carry on as before, everyone else gets TMDB. Either way the library keeps
    /// TheTVDB's show numbers - TMDB records them for its shows - so a database built with one source works
    /// with the other.
    /// </summary>
    static class OnlineSource
    {
        public const string Automatic = "Auto";
        public const string TheTvdb = "TheTVDB";
        public const string Tmdb = "TMDB";

        /// <summary>The stored choice: Automatic, TheTVDB or TMDB.</summary>
        public static string Setting
        {
            get
            {
                string s = DBOption.GetOptions(DBOption.cOnlineDataSource);
                return s == TheTvdb || s == Tmdb ? s : Automatic;
            }
        }

        /// <summary>True when show information should come from TMDB.</summary>
        public static bool UseTmdb
        {
            get
            {
                string s = Setting;
                if (s == Tmdb) return true;
                if (s == TheTvdb) return false;
                return !HasTvdbPin;
            }
        }

        public static bool HasTvdbPin
        {
            get
            {
                string pin = DBOption.GetOptions(DBOption.cTvdbV4Pin);
                return !string.IsNullOrEmpty(pin) && pin.Trim().Length > 0;
            }
        }

        /// <summary>The source in use, for messages: "TMDB" or "TheTVDB".</summary>
        public static string Name
        {
            get { return UseTmdb ? "TMDB" : "TheTVDB"; }
        }
    }
}
