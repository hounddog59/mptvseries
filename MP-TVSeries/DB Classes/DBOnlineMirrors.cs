#region GNU license
// MP-TVSeries - Plugin for Mediaportal
// http://www.team-mediaportal.com
// Copyright (C) 2006-2007
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
//
//+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
//+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
#endregion


using System;
using System.Collections.Generic;

namespace WindowPlugins.GUITVSeries
{
    class DBOnlineMirror
    {
        public const String cTableName = "OnlineMirrors";

        public const String cID = "ID";
        public const String cInterface = "Interface";
        public const String cBanners = "Banners";
        readonly Dictionary<string, DBField> mfields = new Dictionary<string, DBField>();

        private static String mCurrentInterface = String.Empty;
        private static String mCurrentBanner = String.Empty;
        private static String mCurrentZip = String.Empty;

        /// <summary>
        /// if you compile yourself get you're own key or it will have to be disabled!
        /// </summary>
        // key built into this copy (APIKey.resx); the source code on GitHub has only a placeholder
        private static string builtInApiKey = string.Empty;

        /// <summary>The TheTVDB API key built into this copy, or empty if there is none.</summary>
        public static string cApiKey
        {
            get
            {
                if (string.IsNullOrEmpty(builtInApiKey) || builtInApiKey.StartsWith("PUT-YOUR")) return string.Empty;
                return builtInApiKey;
            }
        }
        private static bool mMirrorsAvailable = false;

        static DBOnlineMirror()
        {
            try
            {
                builtInApiKey = new System.Resources.ResourceManager("WindowPlugins.GUITVSeries.Online_Parsing_Classes.APIKey", typeof(DBOnlineMirror).Assembly).GetString("Key") ?? string.Empty;
            }
            catch (Exception) { builtInApiKey = string.Empty; }
        }

        private DBOnlineMirror()
        { }

        public virtual DBValue this[String fieldName]
        {
            get
            {
                if (mfields.ContainsKey(fieldName))
                    return mfields[fieldName].Value;
                else
                    return String.Empty;
            }
            set
            {
                try
                {
                    if (mfields.ContainsKey(fieldName))
                    {
                        if (mfields[fieldName].Type == DBField.cTypeInt)
                            mfields[fieldName].Value = (long)value;
                        else
                            mfields[fieldName].Value = value;
                    }
                    else
                    {
                        mfields.Add(fieldName, new DBField(DBField.cTypeString));
                        this[fieldName] = value;
                    }
                }
                catch (SystemException)
                { }
            }
        }

        public static void Init()
        {
            if (!DBOption.GetOptions(DBOption.cNewAPIUpgradeDone)) // upgrade
            {
                String sqlDel = "drop table if exists " + cTableName;
                DBTVSeries.Execute(sqlDel);
            }

            if (String.IsNullOrEmpty(cApiKey))
            {
                MPTVSeriesLog.Write("No TheTVDB API key is built into this copy of MP-TVSeries. If you built it yourself, put your own key (free from thetvdb.com) in Online Parsing Classes\\APIKey.resx and build it again. Nothing can be downloaded until then.");
                IsMirrorsAvailable = false;
                return;
            }

            // TVDB v4 migration: v4 has no "mirrors" concept at all - there is a single base
            // URL (https://api4.thetvdb.com/v4) and no relative-path banner mirrors either
            // (artwork URLs come back absolute). IsMirrorsAvailable is used throughout the
            // rest of the plugin purely as an "is TheTVDB reachable and are we configured
            // correctly" flag, so we repurpose it here: a successful v4 login stands in for
            // "mirrors available".
            try
            {
                string pin = DBOption.GetOptions(DBOption.cTvdbV4Pin);
                IsMirrorsAvailable = Online_Parsing_Classes.V4.TvdbV4Client.VerifyLogin(cApiKey, pin);
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Error: unable to log in to TheTVDB v4 API: " + ex.Message);
                IsMirrorsAvailable = false;
            }

            if (!IsMirrorsAvailable)
            {
                MPTVSeriesLog.Write("Warning: could not log in to TheTVDB v4 API (check your API key and, if you're a TVDB subscriber, your PIN) - nothing will be downloaded!");
            }
        }

        public static bool IsMirrorsAvailable
        {
            get { return mMirrorsAvailable; }
            set { mMirrorsAvailable = value; }
        }

        static string AppendAPI(string path, bool appendKey)
        {
            return string.Format("{0}api/{1}", (path.EndsWith("/") ? path : (path + "/")), (appendKey ? (cApiKey + "/") : string.Empty));
        }


        public static String Interface
        {
            get
            {
                InitIfNullOrEmpty(mCurrentInterface);
                return AppendAPI(mCurrentInterface, true);
            }
        }

        public static String InterfaceWithoutKey
        {
            get 
            {
                InitIfNullOrEmpty(mCurrentInterface);
                return AppendAPI(mCurrentInterface, false);
            }
        }

        public static String Banners
        {
            get
            {
                InitIfNullOrEmpty(mCurrentBanner);
                return mCurrentBanner;
            }
        }

        public static String ZipInterface
        {
            get
            {
                InitIfNullOrEmpty(mCurrentZip);
                return AppendAPI(mCurrentZip, true);
            }
        }

        static void InitIfNullOrEmpty(string value)
        {
            if (String.IsNullOrEmpty(value)) Init();
        }
    }
}
