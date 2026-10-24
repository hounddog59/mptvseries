using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using MediaPortal.Dialogs;
using MediaPortal.GUI.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Web;
using System.Xml;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes
{
    static class OnlineAPI
    {
        private static class apiURIs
        {
            public const string Languages = "languages";
            public const string FullSeriesUpdate = @"series/{0}/all/{1}";
            public const string Updates = "updates/updates_{0}";
        }

        public enum Format
        {
            Xml,
            Zip,
            NoExtension
        }

        public enum UpdateType
        {
            day = 0,
            week = 1,
            month = 2,
            all = 3,
        }

        static Dictionary<int, WebClient> webClientList = new Dictionary<int, WebClient>();
        static int nDownloadGUIDGenerator = 1;

        #region Language
        static string selLang = string.Empty;

        public static string SelLanguageAsString
        {
            get
            {
                if ( selLang.Length == 0 )
                {
                    string lang = DBOption.GetOptions( DBOption.cOnlineLanguage );
                    if ( !String.IsNullOrEmpty( lang ) ) selLang = lang;
                    else selLang = "en"; // use english
                }
                return selLang;
            }
            set { selLang = value; }
        }

        public static string GetLanguageOverride( String sSeriesID )
        {
            string sqlCon = string.Empty;
            SQLCondition cond = new SQLCondition();

            sqlCon = "id = " + sSeriesID;
            cond.AddCustom( sqlCon );

            // Get the language that the user has selected for the serie
            List<DBValue> seriesLanguage = DBOnlineSeries.GetSingleField( DBOnlineSeries.cLanguage, cond, new DBOnlineSeries() );

            if ( ( seriesLanguage.Count > 0 ) && ( seriesLanguage[0] != "" ) )
            {
                return seriesLanguage[0];
            }
            else
            {
                //If there is no language prefered for the series, the fallback is SelLanguageAsString (en).
                return SelLanguageAsString;
            }
        }

        public static string GetSeriesLanguage( int aSeriesID )
        {
            if ( DBOption.GetOptions( DBOption.cOverrideLanguage ) )
            {
                return GetLanguageOverride( aSeriesID.ToString() );
            }
            else
            {
                return SelLanguageAsString;
            }
        }

        #endregion

        static public XmlNode GetLanguages()
        {
            return Generic( apiURIs.Languages, Format.Xml );
        }

        static public XmlNode GetSeries( String sSeriesName, string aLanguageCode = "en" )
        {
            // TVDB v4 migration: search now goes through /search (JSON), not GetSeries.php
            // (XML). TvdbV4SeriesFetcher.Search returns the v3-shaped <Data><Series>...
            // document GetSeries.cs already knows how to parse.
            // without a TheTVDB subscription, show information comes from TMDB instead (see OnlineSource)
            if ( OnlineSource.UseTmdb )
                return Tmdb.TmdbSeriesFetcher.Search( sSeriesName, aLanguageCode );
            return V4.TvdbV4SeriesFetcher.Search( sSeriesName, aLanguageCode );
        }

        /// <summary>
        /// Adds a series to, or (asks to) remove it from, the user's TheTVDB favourites. TheTVDB's current service can add
        /// favourites but cannot remove them, so a removal only happens here in MP-TVSeries and has to be done on
        /// thetvdb.com as well - otherwise the next update brings the series back into Online Favourites.
        /// Returns false when nothing could be changed on TheTVDB.
        /// </summary>
        static public bool ConfigureFavourites( bool bAdd, String sSeriesID )
        {
            if ( !V4.TvdbV4Favourites.IsAvailable )
            {
                MPTVSeriesLog.Write( "Cannot change online favourites: they need your TheTVDB subscriber PIN to be set." );
                return false;
            }

            string seriesName = Helper.getCorrespondingSeries( int.Parse( sSeriesID ) ) == null ? sSeriesID : Helper.getCorrespondingSeries( int.Parse( sSeriesID ) ).ToString();
            if ( !bAdd )
            {
                MPTVSeriesLog.Write( string.Format( "Removed \"{0}\" from Online Favourites here. TheTVDB can't remove favourites through its service - remove it at thetvdb.com too, or it will come back at the next update.", seriesName ) );
                return false;
            }

            bool ok = V4.TvdbV4Favourites.AddSeries( sSeriesID );
            MPTVSeriesLog.Write( ok ? string.Format( "Added \"{0}\" to your TheTVDB favourites", seriesName )
                                    : string.Format( "Could not add \"{0}\" to your TheTVDB favourites", seriesName ) );
            return ok;
        }

        static public XmlNode UpdateSeries( String sSeriesID )
        { return UpdateSeries( sSeriesID, true ); }

        static public XmlNode UpdateSeries( String sSeriesID, String languageID, bool aOverride = false )
        { return UpdateSeries( sSeriesID, languageID, true, aOverride ); }

        static private XmlNode UpdateSeries( String sSeriesID, bool first )
        {
            int series = Int32.Parse( sSeriesID );

            if ( DBOption.GetOptions( DBOption.cOverrideLanguage ) )
            {
                string SelLang = GetLanguageOverride( sSeriesID );
                return getFromCache( series, SelLang + ".xml", SelLang );
            }
            else
            {
                return getFromCache( series, SelLanguageAsString + ".xml" );
            }
        }

        static private XmlNode UpdateSeries( String sSeriesID, String languageID, bool first, bool aOverride = false )
        {
            // we may need to get english original name even when series language is overridden
            if ( DBOption.GetOptions( DBOption.cOverrideLanguage ) && !aOverride )
            {
                languageID = GetLanguageOverride( sSeriesID );
                int series = Int32.Parse( sSeriesID );
                return getFromCache( series, languageID + ".xml", languageID );
            }
            else
            {
                // if we have an overridden language then we do not care
                int series = Int32.Parse( sSeriesID );
                return getFromCache( series, languageID + ".xml", languageID );
            }
        }

        static public XmlNode Updates( UpdateType type, Format format = Format.Zip )
        {
            string typeName = Enum.GetName( typeof( UpdateType ), type );
            return Generic( string.Format( apiURIs.Updates, typeName ), true, true, format, "updates_" + typeName, -1 );
        }

        static public XmlNode UpdateEpisodes( int seriesID )
        { return UpdateEpisodes( seriesID, true ); }

        static XmlNode UpdateEpisodes( int seriesID, bool first )
        {
            if ( DBOption.GetOptions( DBOption.cOverrideLanguage ) )
            {
                string SelLang = GetLanguageOverride( seriesID.ToString() );
                return getFromCache( seriesID, SelLang + ".xml" );
            }
            else
            {
                return getFromCache( seriesID, SelLanguageAsString + ".xml" );
            }
        }
        
        static public XmlNode GetBannerList( int aSeriesID, string aLanguageId = null )
        {
            if ( aLanguageId == null )
            {
                // get current language or series overridden language
                return getFromCache( aSeriesID, "banners.xml" );
            }
            else
            {
                string lFilename = Path.Combine( Settings.GetPath( Settings.Path.config ), string.Format( @"Cache\{0}\{1}\{2}", aSeriesID, aLanguageId, "banners.xml" ) );
                XmlNode lNode = Helper.LoadXmlCache( lFilename );
                if ( lNode != null ) return lNode;

                // download and save to cache
                lNode = Generic( string.Format( apiURIs.FullSeriesUpdate, aSeriesID, aLanguageId ),
                                 true, true, Format.Zip, aLanguageId, aSeriesID );

                if ( lNode == null ) return null;

                // our cache is not set, get from cache
                return GetBannerList(aSeriesID, aLanguageId);
            }
        }

        static public XmlNode GetActorsList( int seriesID )
        {
            return getFromCache( seriesID, "actors.xml" );
        }

        // TVDB v4 migration: v4's artwork "image"/"thumbnail" fields are already absolute
        // URLs (this changed from v3, which used paths relative to a mirror's banner base
        // URL) - MOST of the time. In practice v4 also still returns plain relative paths
        // for some fields (confirmed in this project's own logs: "/graphical/xxx.jpg",
        // "/posters/xxx.jpg", "/seasons/xxx.jpg", "/blank/xxx.jpg", "/banners/episodes/xxx.jpg",
        // as well as fuller ones like "/v4/series/xxx/banners/xxx.jpg" that already include
        // their own structure). This is a known, acknowledged inconsistency in TheTVDB's own
        // API, not something specific to this plugin.
        //
        // The old code assumed every non-absolute path should fall back to the v3 mirror
        // prefix (DBOnlineMirror.Banners) - but that mirror-discovery system was removed
        // during the v3->v4 migration, so DBOnlineMirror.Banners is now permanently empty.
        // That turned every relative path into something like "/graphical/xxx.jpg" with no
        // host at all, which .NET's WebClient then quietly treated as a local file path
        // instead of a URL - on Windows, a leading "/" resolves against the current drive,
        // which is exactly the "C:\graphical\xxx.jpg" paths seen failing in the log.
        //
        // Fixed by using TheTVDB's real, documented image host directly. A short fragment
        // (no "/banners/" and not already one of v4's fuller "/v4/..." paths) needs
        // "/banners" inserted, matching TheTVDB's own documented convention for these -
        // paths that already carry their own structure (including the episode-thumbnail
        // paths, which already start with "/banners/episodes/...") are used as-is.
        public static string ResolveImageURL( string onlineFilename )
        {
            if ( onlineFilename.StartsWith( "http://" ) || onlineFilename.StartsWith( "https://" ) )
                return onlineFilename;

            const string imageHost = "https://artworks.thetvdb.com";
            string path = onlineFilename.StartsWith( "/" ) ? onlineFilename : "/" + onlineFilename;
            bool alreadyHasStructure = path.StartsWith( "/banners/" ) || path.StartsWith( "/v4/" );
            return imageHost + ( alreadyHasStructure ? path : "/banners" + path );
        }

        static public string DownloadBanner( string onlineFilename, Settings.Path localPath, string localFilename )
        {
            WebClient webClient = new WebClient();
            string fullLocalPath = Helper.PathCombine( Settings.GetPath( localPath ), localFilename );
            string fullURL = ResolveImageURL( onlineFilename );
            webClient.Headers.Add( "user-agent", Settings.UserAgent );

            // .NET 4.0: Use TLS v1.2. Many download sources no longer support the older and now insecure TLS v1.0/1.1 and SSL v3.
            ServicePointManager.SecurityProtocol = ( SecurityProtocolType )0xc00;

            try
            {
                Directory.CreateDirectory( Path.GetDirectoryName( fullLocalPath ) );
                if ( !File.Exists( fullLocalPath ) // only if the file doesn't exist
                    || ImageAllocator.LoadImageFastFromFile( fullLocalPath ) == null ) // or the file is damaged
                {
                    MPTVSeriesLog.Write( "Downloading new Image from: " + fullURL, MPTVSeriesLog.LogLevel.Debug );
                    webClient.DownloadFile( fullURL, fullLocalPath );
                    return fullLocalPath;
                }
                return string.Empty;
            }
            catch ( WebException ex )
            {
                MPTVSeriesLog.Write( $"Banner download failed from '{fullURL}' to '{fullLocalPath.Replace("/", @"\")}'. Reason='{ex.Message}'" );
                return null;
            }
        }

        static public int StartFileDownload( string fullURL, Settings.Path localPath, string localFilename )
        {
            WebClient webClient = new WebClient();

            // .NET 4.0: Use TLS v1.2. Many download sources no longer support the older and now insecure TLS v1.0/1.1 and SSL v3.
            ServicePointManager.SecurityProtocol = ( SecurityProtocolType )0xc00;

            int nDownloadGUID = nDownloadGUIDGenerator;
            nDownloadGUIDGenerator++;

            string fullLocalPath = Helper.PathCombine( Settings.GetPath( localPath ), localFilename );
            webClient.Headers.Add( "user-agent", Settings.UserAgent );
            try
            {
                Directory.CreateDirectory( Path.GetDirectoryName( fullLocalPath ) );
                if ( !File.Exists( fullLocalPath ) // only if the file doesn't exist
                    || ImageAllocator.LoadImageFastFromFile( fullLocalPath ) == null ) // or the file is damaged
                {
                    webClient.DownloadFileCompleted += new System.ComponentModel.AsyncCompletedEventHandler( webClient_DownloadFileCompleted );
                    webClient.DownloadFileAsync( new Uri( fullURL ), fullLocalPath, nDownloadGUID );
                    webClientList.Add( nDownloadGUID, webClient );
                    return nDownloadGUID;
                }
                return -1;
            }
            catch ( WebException )
            {
                MPTVSeriesLog.Write( "File download failed (" + fullURL + ") to " + fullLocalPath.Replace( "/", @"\" ) );
                return -1;
            }
        }

        static void webClient_DownloadFileCompleted( object sender, System.ComponentModel.AsyncCompletedEventArgs e )
        {
            webClientList.Remove( ( int )e.UserState );
        }

        static public bool CheckFileDownload( int nDownloadGUID )
        {
            return webClientList.ContainsKey( nDownloadGUID );
        }

        static public bool CancelFileDownload( int nDownloadGUID )
        {
            if ( webClientList.ContainsKey( nDownloadGUID ) )
            {
                WebClient client = webClientList[nDownloadGUID];
                client.CancelAsync();
                return true;
            }
            return false;
        }

        static XmlNode getFromCache( int aSeriesID, string aXMLFilename )
        {
            return getFromCache( aSeriesID, true, aXMLFilename, GetSeriesLanguage(aSeriesID) );
        }

        static XmlNode getFromCache( int aSeriesID, string aXMLFilename, string aLanguageId )
        {
            return getFromCache( aSeriesID, true, aXMLFilename, aLanguageId );
        }

        static XmlNode getFromCache( int aSeriesID, bool aRefresh, string aXMLFilename, string aLanguageId )
        {
            // cache filename
            string filename = Path.Combine( Settings.GetPath( Settings.Path.config ), string.Format( @"Cache\{0}\{1}\{2}", aSeriesID, aLanguageId, aXMLFilename ) );

            // check if in cache
            XmlNode node = Helper.LoadXmlCache( filename );
            if ( node != null ) return node;

            // xml is not cached, retrieve online
            if ( aRefresh )
            {
                Generic( string.Format( apiURIs.FullSeriesUpdate, aSeriesID, aLanguageId ),
                        true, true, Format.Zip, aLanguageId, aSeriesID );

                // its now cached, so load it
                return getFromCache( aSeriesID, false, aXMLFilename, aLanguageId );
            }
            return null;
        }

        #region Generic Private Implementation
        static XmlNode Generic( String sUrl, Format format )
        { return Generic( sUrl, true, format ); }

        static XmlNode Generic( String sUrl, bool appendBaseUrl, Format format )
        { return Generic( sUrl, appendBaseUrl, true, format ); }

        static XmlNode Generic( String sUrl, bool appendBaseUrl, bool appendAPIKey, Format format )
        { return Generic( sUrl, appendBaseUrl, appendAPIKey, format, null, 0 ); }

        static XmlNode Generic( String sUrl, bool appendBaseUrl, bool appendAPIKey, Format format, string aLanguageId, int seriesIDIfZip )
        {
            if ( format == Format.Zip )
            {
                // TVDB v4 migration: v4 has no equivalent to the old "series/{id}/all/{lang}.zip"
                // bundle (or the "updates_{type}.zip" global changes feed). Series/episode/
                // banner/actor data now comes from TvdbV4SeriesFetcher, which fetches the v4
                // JSON endpoints and writes the same en.xml/banners.xml/actors.xml cache files
                // this zip step used to produce - so we intercept here and let the normal
                // cache re-read below pick up what it wrote.
                //
                // seriesIDIfZip is -1 for the global "updates_{type}" feed (day/week/month/all
                // changes) - that endpoint doesn't exist in v4 in the same shape, so we return
                // null here. GetUpdates.cs already has a documented, working fallback for a
                // null Updates() result (it treats all "continuing" series with local files as
                // needing a refresh), so this is a safe, if less efficient, degradation rather
                // than a break.
                if ( seriesIDIfZip <= 0 )
                {
                    return null;
                }

                bool fetched = OnlineSource.UseTmdb
                    ? Tmdb.TmdbSeriesFetcher.FetchAndCacheSeries( seriesIDIfZip, aLanguageId )
                    : V4.TvdbV4SeriesFetcher.FetchAndCacheSeries( seriesIDIfZip, aLanguageId );
                if ( !fetched ) return null;

                // Both callers (getFromCache and GetBannerList) discard/only null-check this
                // return value and re-read whichever specific file they wanted straight from
                // the cache directory afterward - so all we need to hand back is "non-null on
                // success". Return the series XML we just cached for that purpose.
                string lCacheRoot = Path.Combine( Settings.GetPath( Settings.Path.config ), string.Format( @"Cache\{0}\{1}", seriesIDIfZip, aLanguageId ) );
                return Helper.LoadXmlCache( Path.Combine( lCacheRoot, aLanguageId + ".xml" ) );
            }
            else
            {
                if ( appendBaseUrl && appendAPIKey ) sUrl = DBOnlineMirror.Interface + sUrl;
                else if ( appendBaseUrl ) sUrl = DBOnlineMirror.InterfaceWithoutKey + sUrl;
            }
            switch ( format )
            {
                case Format.Xml:
                    sUrl += ".xml";
                    break;
                case Format.Zip:
                    sUrl += ".zip";
                    break;
            }
            Stream lStreamData = RetrieveData( sUrl );

            if ( lStreamData != null )
            {
                if ( format == Format.Zip )
                {
                    if ( !String.IsNullOrEmpty( aLanguageId ) && seriesIDIfZip != 0 )
                    {
                        // save the zip file downloaded to disk
                        //string lFilename = Path.Combine(Settings.GetPath(Settings.Path.config), string.Format(@"Cache\{0}\{1}.zip", seriesIDIfZip, aLanguageId));
                        //Helper.SaveZipCache(lFilename, lStreamData);

                        Dictionary<string, XmlDocument> lZip = DecompressZipToXmls( lStreamData );
                        string lFilenameInZip = aLanguageId + ".xml";
                        XmlNode lRoot;

                        // save all xmls in zip to cache
                        foreach ( string key in lZip.Keys )
                        {
                            string lFilename = Path.Combine( Settings.GetPath( Settings.Path.config ), string.Format( @"Cache\{0}\{1}\{2}", seriesIDIfZip, aLanguageId, key ) );
                            Helper.SaveXmlCache( lFilename, lZip[key].FirstChild.NextSibling ?? lZip[key].FirstChild );
                        }

                        // get what we are looking for            
                        if (lZip.ContainsKey(lFilenameInZip))
                        {
                            lRoot = lZip[lFilenameInZip].FirstChild.NextSibling;
                            return lRoot;
                        }
                        else
                        {
                            MPTVSeriesLog.Write("Decompression returned null or not the requested entry");
                        }
                    }
                }
                else
                {
                    var reader = new StreamReader( lStreamData );
                    String sXmlData = string.Empty;
                    try
                    {
                        sXmlData = reader.ReadToEnd().Replace( '\0', ' ' );
                    }
                    catch ( Exception e )
                    {
                        MPTVSeriesLog.Write( "Error reading stream: {0}", e.Message );
                    }

                    lStreamData.Close();
                    reader.Close();
                    if ( !string.IsNullOrEmpty( sXmlData ) )
                    {
                        MPTVSeriesLog.Write( "*************************************", MPTVSeriesLog.LogLevel.Debug );
                        MPTVSeriesLog.Write( sXmlData, MPTVSeriesLog.LogLevel.Debug );
                        MPTVSeriesLog.Write( "*************************************", MPTVSeriesLog.LogLevel.Debug );
                        try
                        {
                            var doc = new XmlDocument();
                            doc.LoadXml( sXmlData );
                            XmlNode root = doc.FirstChild.NextSibling;
                            return root;
                        }
                        catch ( XmlException e )
                        {
                            // Most likely bad xml formatting, tvdb does not use CDATA structures so good luck.
                            MPTVSeriesLog.Write( "Xml parsing of " + sUrl + " failed (line " + e.LineNumber + " - " + e.Message + ")" );
                        }
                    }
                }
            }
            return null;
        }

        static Stream RetrieveData( String aUrl )
        {
            MPTVSeriesLog.Write( $"Retrieving Data from '{aUrl}'", MPTVSeriesLog.LogLevel.Debug );
            if ( aUrl == null || aUrl.Length < 1 || aUrl[0] == '/' )
            {
                // this happens if no active mirror is set
                return null;
            }

            HttpWebRequest lRequest;
            HttpWebResponse lResponse;

            try
            {
                // .NET 4.0: Use TLS v1.2. Many download sources no longer support the older and now insecure TLS v1.0/1.1 and SSL v3.
                ServicePointManager.SecurityProtocol = ( SecurityProtocolType )0xc00;

                // force secure http
                lRequest = ( HttpWebRequest )WebRequest.Create( aUrl );
                // Note: some network proxies require the useragent string to be set or they will deny the http request
                // this is true for instance for EVERY thailand internet connection (also needs to be set for banners/episodethumbs and any other http request we send)
                lRequest.UserAgent = Settings.UserAgent;
                lRequest.Timeout = 60000;

                lResponse = ( HttpWebResponse )lRequest.GetResponse();

                if ( lResponse != null ) // Get the stream associated with the response.
                {
                    return lResponse.GetResponseStream();
                }   
            }
            catch ( Exception e )
            {
                MPTVSeriesLog.Write( $"Unable to connect to '{aUrl}'. Error = '{e.Message}'" );
            }
            finally
            {
                //if (response != null) response.Close(); // screws up the decompression
            }

            return null;
        }

        static Dictionary<string, XmlDocument> DecompressZipToXmls( Stream aStream )
        {
            MPTVSeriesLog.Write( "Decompressing Stream...", MPTVSeriesLog.LogLevel.Debug );

            var lDocsInZip = new Dictionary<string, XmlDocument>();

            ZipConstants.DefaultCodePage = 850;
            var lZipStream = new ZipInputStream( aStream );
            ZipEntry currEntry = lZipStream.GetNextEntry();

            while ( currEntry != null )
            {
                MPTVSeriesLog.Write( "Decompressing Entry: ", currEntry.Name, MPTVSeriesLog.LogLevel.Debug );
                byte[] buffer = new byte[4096];

                var doc = new XmlDocument();
                var stream = new MemoryStream();
                try
                {
                    StreamUtils.Copy( lZipStream, stream, buffer );
                    stream.Position = 0;
                    MPTVSeriesLog.Write( "Decompression done, now loading as XML...", MPTVSeriesLog.LogLevel.Debug );
                    doc.Load( stream );
                    MPTVSeriesLog.Write( "Loaded as valid XML", MPTVSeriesLog.LogLevel.Debug );
                    // check if .zip in filename and remove for backwards compatibility
                    lDocsInZip.Add( currEntry.Name.Replace( ".zip", string.Empty ), doc );
                }
                catch ( XmlException e )
                {
                    MPTVSeriesLog.Write( "Failed to load XML: " + e.Message.ToString() );
                }

                stream.Close();
                currEntry = lZipStream.GetNextEntry();
            }
            return lDocsInZip;
        }

        #endregion
    }
}
