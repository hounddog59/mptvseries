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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using WindowPlugins.GUITVSeries.Feedback;

namespace WindowPlugins.GUITVSeries
{
    public enum ParsingAction
    {
        NoExactMatch,

        LocalScan,
        List_Add,
        List_Remove,
        MediaInfo,
        IdentifyNewSeries,
        IdentifyNewEpisodes,
        CheckArtwork,

        GetOnlineUpdates,
        UpdateSeries,
        UpdateEpisodes,
        CleanupEpisodes,
        UpdateEpisodeCounts,
        UpdateCommunityRatings,
        UpdateBanners,
        UpdateFanart,

        GetNewBanners,
        GetNewFanArt,
        GetNewActors,
        UpdateEpisodeThumbNails,
        UpdateUserFavourites,

        UpdateRecentlyAdded
    }

    public class ParsingProgress
    {
        public ParsingAction CurrentAction { get; set; }
        public string CurrentProgress { get; set; }
        public int CurrentItem { get; set; }
        public int TotalItems { get; set; }
        public DBTable Details { get; set; }
        public string Picture { get; set; }

        public ParsingProgress(ParsingAction currentAction, int totalitems)
            : this(currentAction, null, -1, totalitems) { }
        public ParsingProgress(ParsingAction currentAction, string currentItem, int currentProgress, int totalItems) 
            : this(currentAction, currentItem, currentProgress, totalItems, null, null){}
        public ParsingProgress(ParsingAction currentAction, string currentItem, int currentProgress, int totalItems, DBTable details, string picture)
        {
            this.CurrentAction = currentAction;
            this.CurrentProgress = currentItem;
            this.CurrentItem = currentProgress;
            this.TotalItems = totalItems;
            this.Details = details;
            this.Picture = picture;
        }
    }

    public class CParsingParameters
    {
        private static List<ParsingAction> FirstLocalScanActions = new List<ParsingAction> { 
            ParsingAction.LocalScan, 
            ParsingAction.MediaInfo,
            ParsingAction.IdentifyNewSeries, 
            ParsingAction.IdentifyNewEpisodes,
            ParsingAction.UpdateEpisodeCounts,
            ParsingAction.CheckArtwork
        };

        private static List<ParsingAction> OnlineRefreshActions = new List<ParsingAction> { 
            ParsingAction.GetOnlineUpdates, 
            ParsingAction.UpdateSeries, 
            ParsingAction.UpdateEpisodes, 
            ParsingAction.UpdateBanners, 
            ParsingAction.UpdateFanart, 
            ParsingAction.GetNewBanners, 
            ParsingAction.GetNewFanArt,
            ParsingAction.GetNewActors,
            ParsingAction.CleanupEpisodes,
            ParsingAction.UpdateCommunityRatings,
            ParsingAction.UpdateEpisodeThumbNails,
            ParsingAction.UpdateUserFavourites,
            ParsingAction.UpdateRecentlyAdded,
            ParsingAction.UpdateEpisodeCounts
		};

        public List<ParsingAction> m_actions = new List<ParsingAction>();

        public bool m_bLocalScan = true;
        public bool m_bUpdateScan = true;
        public List<PathPair> m_files = null;

        public List<DBValue> m_series = null;
        public List<DBValue> m_episodes = null;

        public UserInputResults m_userInputResult = null;
        public Feedback.IEpisodeMatchingFeedback UserEpisodeMatcher = null;

        public CParsingParameters(bool bScanNew, bool bUpdateExisting)
        {
            m_bLocalScan = bScanNew;
            m_bUpdateScan = bUpdateExisting;

            if (m_bLocalScan)
                m_actions.AddRange(FirstLocalScanActions);

            if (m_bUpdateScan)
                m_actions.AddRange(OnlineRefreshActions);
        }

        public CParsingParameters(ParsingAction action, List<PathPair> files, bool bScanNew, bool bUpdateExisting)
        {
            m_bLocalScan = bScanNew;
            m_bUpdateScan = bUpdateExisting;

            m_actions.Add(action);
            if (action == ParsingAction.List_Add) {
                m_actions.Add(ParsingAction.MediaInfo);
                m_actions.Add(ParsingAction.IdentifyNewSeries);
                m_actions.Add(ParsingAction.IdentifyNewEpisodes);
                m_actions.Add(ParsingAction.UpdateEpisodeCounts);                
            }
            
            if (action == ParsingAction.List_Remove)
            { 
                m_actions.Add(ParsingAction.UpdateEpisodeCounts); 
            }
            
            m_files = files;

            if (m_bLocalScan)
                m_actions.AddRange(FirstLocalScanActions);
            if (m_bUpdateScan)
                m_actions.AddRange(OnlineRefreshActions);
        }

        public CParsingParameters(IEnumerable<ParsingAction> actions, List<DBValue> series, List<DBValue> episodes)
        {
            m_actions.AddRange(actions);
            m_episodes = episodes;
            m_series = series;
        }

        public CParsingParameters(bool bScanNew, bool bUpdateExisting, UserInputResults UserInputResult, Feedback.IEpisodeMatchingFeedback UserEpisodeMatcher)
            : this(bScanNew, bUpdateExisting)
        {
            this.m_userInputResult = UserInputResult;
            this.UserEpisodeMatcher = UserEpisodeMatcher;
        }

    };

    public class OnlineParsing
    {
        public bool mOnlineUpdateNeeded = false;
        public bool mWasOnlineUpdate = false;
        public BackgroundWorker mWorker = new BackgroundWorker();
        IFeedback mFeedback = null;

        public static bool mDataUpdated = false;
        bool mNewLocalFiles = false;        
        bool mFullSeriesRetrieval = false;
        bool mNoExactMatch = false;       //if set to true then the user will be always prompted to choose the series
        CParsingParameters mParsingParameters = null;
        DateTime mLastOnlineMirrorUpdate = DateTime.MinValue;

        public static bool IsMainOnlineParseComplete = true; // not including extra background threads

        Dictionary<string, List<DBOnlineEpisode>> mOnlineEpisodes = new Dictionary<string, List<DBOnlineEpisode>>();

        int RETRY_INTERVAL = 1000;
        int RETRY_MULTIPLIER = 2;
        int MAX_TIMEOUT = 120000;

        public delegate void OnlineParsingProgressHandler(int nProgress, ParsingProgress Progress);
        public delegate void OnlineParsingCompletedHandler(bool newLocalFiles);

        /// <summary>
        /// This will be triggered once all the SeriesAndEpisodeInfo has been parsed completely.
        /// </summary>        
        public static event OnlineParsingCompletedHandler OnlineParsingCompleted;
        public static event OnlineParsingProgressHandler OnlineParsingProgress;

        public OnlineParsing(IFeedback feedback)
        {
            mFeedback = feedback;
            mWorker.WorkerReportsProgress = true;
            mWorker.WorkerSupportsCancellation = true;
            mWorker.ProgressChanged += new ProgressChangedEventHandler(worker_ProgressChanged);
            mWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(worker_RunWorkerCompleted);
            mWorker.DoWork += new DoWorkEventHandler(worker_DoWork);
        }

        void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // Run an Online update when needed
            if (mOnlineUpdateNeeded && !mWasOnlineUpdate)
            {
                MPTVSeriesLog.Write("Import Worker completed, online update is required.", MPTVSeriesLog.LogLevel.Normal);
                mOnlineUpdateNeeded = false;
                TVSeriesPlugin.m_LastUpdateScan = DateTime.Now;
                Start(new CParsingParameters(false, true));
            }
            else
            {
                if (OnlineParsingCompleted != null) // only if any subscribers exist
                {
                    OnlineParsingCompleted.Invoke(mNewLocalFiles);
                }
                mNewLocalFiles = false;
            }
        }

        void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (OnlineParsingProgress != null) // only if any subscribers exist
                OnlineParsingProgress.Invoke(e.ProgressPercentage, e.UserState as ParsingProgress);
        }

        public bool IsWorking
        {
            get
            {
                return mWorker.IsBusy;
            }
        }

        public bool Start(CParsingParameters param)
        {
            mWasOnlineUpdate=false;

            // check if we are doing any online refresh actions
            // we dont want to double up actions later            
            if (param.m_actions.Contains(ParsingAction.GetOnlineUpdates)) {
                mWasOnlineUpdate = true;
            }

            if (!mWorker.IsBusy)
            {
                mWorker.RunWorkerAsync(param);
                return true;
            }
            return false;
        }

        public bool LocalScan
        {
            get { if (mParsingParameters != null) return mParsingParameters.m_bLocalScan; else return false; }
        }

        public bool UpdateScan
        {
            get { if (mParsingParameters != null) return mParsingParameters.m_bUpdateScan; else return false; }
        }

        public void Cancel()
        {
            mWorker.CancelAsync();
        }

        public void UpdateOnlineMirror()
        {
            TimeSpan tsUpdate = DateTime.Now - mLastOnlineMirrorUpdate;
            if ((int)tsUpdate.TotalMinutes > 15)
            {
                mLastOnlineMirrorUpdate = DateTime.Now;
                // Re-Initialize Mirrors in case they have changed or are down
                DBOnlineMirror.Init();

                // Try again, possibly resuming from standby and network interface is not available yet           
                int iSleepInterval = RETRY_INTERVAL;
                while (!DBOnlineMirror.IsMirrorsAvailable && (TVSeriesPlugin.IsResumeFromStandby || !TVSeriesPlugin.IsNetworkAvailable))
                {
                    if (iSleepInterval > MAX_TIMEOUT)
                    {
                        MPTVSeriesLog.Write("Aborting connection retries, maximum timeout has expired");
                        break;
                    }
                    MPTVSeriesLog.Write(string.Format("Retrying connection to mirror in {0} seconds", iSleepInterval / 1000));
                    Thread.Sleep(iSleepInterval);
                    DBOnlineMirror.Init();
                    iSleepInterval *= RETRY_MULTIPLIER;
                }
            }
        }

        public void worker_DoWork(object sender, DoWorkEventArgs e)
        {            
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            
            mParsingParameters = e.Argument as CParsingParameters;
            mFullSeriesRetrieval = DBOption.GetOptions(DBOption.cFullSeriesRetrieval);
            mNoExactMatch = false;
            mWorker.ReportProgress(0);

            IsMainOnlineParseComplete = false;

            TVSeriesPlugin.IsResumeFromStandby = false;

            UpdateOnlineMirror();

            BackgroundWorker tMediaInfo = null;
            BackgroundWorker tEpisodeCounts = null;
            BackgroundWorker tTraktCommunityRatings = null;

            // remove first update episode count as this is added twice in some Import Actions
            // we could do distinct() but that removes last
            if (mParsingParameters.m_actions.Count(pa => pa == ParsingAction.UpdateEpisodeCounts) == 2)
                mParsingParameters.m_actions.Remove(ParsingAction.UpdateEpisodeCounts);

            // we may need to clear the cache to identify new episodes
            // but update check and identify are done in two seperate import actions (online/local)
            if (mParsingParameters.m_actions.Contains(ParsingAction.GetOnlineUpdates) && mParsingParameters.m_actions.Contains(ParsingAction.IdentifyNewEpisodes))
            {
                mParsingParameters.m_actions.Remove(ParsingAction.GetOnlineUpdates);
                mParsingParameters.m_actions.Insert(mParsingParameters.m_actions.IndexOf(ParsingAction.IdentifyNewEpisodes), ParsingAction.GetOnlineUpdates);
            }
                
            Online_Parsing_Classes.GetUpdates updates = null;
            foreach (ParsingAction action in mParsingParameters.m_actions) {
                MPTVSeriesLog.Write("Begin Parsing action: ", action.ToString(), MPTVSeriesLog.LogLevel.Debug);
                if (mWorker.CancellationPending)
                    break;

                switch (action) {
                    case ParsingAction.List_Remove:
                        ParseActionRemove(mParsingParameters.m_files);
                        break;

                    case ParsingAction.List_Add:
                        ParseActionAdd(mParsingParameters.m_files);
                        break;

                    case ParsingAction.NoExactMatch:
                        this.mNoExactMatch = true;
                        break;

                    case ParsingAction.LocalScan:
                        ParseActionLocalScan(mParsingParameters.m_bLocalScan, mParsingParameters.m_bUpdateScan, mParsingParameters.m_userInputResult == null ? null : mParsingParameters.m_userInputResult.ParseResults);
                        break;

                    case ParsingAction.MediaInfo:
                        // Multi-threaded MediaInfo parsing of new files - goes straight to next task
                        if ( !DBOption.GetOptions( DBOption.cDisableMediaInfo ) )
                        {
                            // disable only in configuration
                            if ( !Settings.isConfig || ( Settings.isConfig && !DBOption.GetOptions( DBOption.cDisableMediaInfoInConfigImports ) ) )
                            {
                                tMediaInfo = new BackgroundWorker();
                                MediaInfoParse( tMediaInfo );
                            }
                        }
                        break;

                    case ParsingAction.IdentifyNewSeries:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                        {
                            GetSeries(mWorker, mNoExactMatch, mParsingParameters.m_userInputResult == null ? null : mParsingParameters.m_userInputResult.UserChosenSeries);
                            UpdateSeries(true, null); // todo: ask orderoption
                        }
                        break;

                    case ParsingAction.IdentifyNewEpisodes:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                        {
                            GetEpisodes(mParsingParameters.m_bUpdateScan, mFullSeriesRetrieval);                            
                        }
                        // at this point we have identified any new episodes
                        // signal the facade to be reloaded.
                        // TODO: smart way to report progress and expose as property to skins
                        mWorker.ReportProgress(30);
                        break;
                    
                    case ParsingAction.CheckArtwork:
                        // always: the quick check that the selected picture exists; opening every picture file
                        // only when "check artwork" is ticked
                        this.CheckBanners(DBOption.GetOptions(DBOption.cCheckArtwork) == 1);
                        break;

                    case ParsingAction.CleanupEpisodes:
                        if (DBOption.GetOptions(DBOption.cCleanOnlineEpisodes) == 1)
                            this.CleanEpisodes();
                        break;

                    case ParsingAction.GetOnlineUpdates:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                        {
                            updates = GetOnlineUpdates();
                        }
                        break;

                    case ParsingAction.UpdateSeries:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                        {
                            if (updates != null)
                                UpdateSeries(false, updates.UpdatedSeries.Keys.ToList());
                            if (mParsingParameters.m_series != null)
                                UpdateSeries(false, mParsingParameters.m_series);
                        }
                        break;

                    case ParsingAction.UpdateEpisodes:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                        {
                            if (updates != null)
                                UpdateEpisodes(updates.UpdatedEpisodes.Keys.ToList());
                            if (mParsingParameters.m_episodes != null)
                            {
                                UpdateEpisodes(mParsingParameters.m_episodes);                               
                            }

                            // the full refresh has been applied in this import (series and episodes); if the import
                            // was stopped part-way, it stays pending and runs again next time
                            if (mFullRefreshThisRun && !mWorker.CancellationPending)
                            {
                                DBOption.SetOptions(DBOption.cFullTextRefreshPending, false);
                                mFullRefreshThisRun = false;
                                MPTVSeriesLog.Write("Refresh of every series' details from " + Online_Parsing_Classes.OnlineSource.Name + " is complete");
                            }
                        }
                        break;

                    case ParsingAction.UpdateBanners:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable && updates != null)
                            UpdateBanners(false, updates.UpdatedBanners.Keys.ToList());
                        break;

                    case ParsingAction.UpdateFanart:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable && updates != null && !DBOption.GetOptions(DBOption.cAutoUpdateAllFanart))
                            UpdateFanart(true, updates.UpdatedFanart.Keys.ToList());
                        break;

                    case ParsingAction.GetNewBanners:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                            UpdateBanners(true, null);// update new series for banners
                        break;

                    case ParsingAction.GetNewFanArt:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                            UpdateFanart(false, null);
                        break;

                    case ParsingAction.GetNewActors:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                            UpdateActors(false, null);
                        break;

                    case ParsingAction.UpdateEpisodeThumbNails:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                            UpdateEpisodeThumbNails();
                        break;

                    case ParsingAction.UpdateCommunityRatings:
                        if (DBOption.GetOptions(DBOption.cTraktCommunityRatings) == 1)
                        {
                            tTraktCommunityRatings = new BackgroundWorker();
                            UpdateTraktCommunityRatings(tTraktCommunityRatings, mParsingParameters.m_series, mParsingParameters.m_episodes);
                        }
                        break;

                    case ParsingAction.UpdateUserFavourites:
                        if (DBOption.GetOptions(DBOption.cOnlineParseEnabled) == 1)
                            UpdateOnlineMirror();
                        if (DBOnlineMirror.IsMirrorsAvailable)
                            UpdateUserFavourites();
                        break;

                    case ParsingAction.UpdateEpisodeCounts:
                        //Threaded processing of episode counts - goes straight to next task
                        tEpisodeCounts = new BackgroundWorker();
                        UpdateEpisodeCounts(tEpisodeCounts);
                        break;

                    case ParsingAction.UpdateRecentlyAdded:
                        // set hasnewepisodes field on series record
                        UpdateRecentlyAdded();
                        break;
                }
            }

            IsMainOnlineParseComplete = true;

            // SLEEP UNTIL ALL WORKER THREADS ARE DONE - avoids user thinking scan it's completed
            do
            {
                Thread.Sleep(1000);
            }
            while ((tMediaInfo != null && tMediaInfo.IsBusy) || (tEpisodeCounts != null && tEpisodeCounts.IsBusy) || tTraktCommunityRatings != null && tTraktCommunityRatings.IsBusy);
            
            // lets save the updateTimestamp
            if (updates != null && updates.OnlineTimeStamp > 0)
                DBOption.SetOptions(DBOption.cUpdateTimeStamp, updates.OnlineTimeStamp);            

            // and we are done, the backgroundworker is going to notify so
            MPTVSeriesLog.Write("***************************************************************************");
            MPTVSeriesLog.Write("*******************            Completed           ************************");
            MPTVSeriesLog.Write("***************************************************************************");
        }

        private void UpdateRecentlyAdded()
        {
            // no need to update fields if we wont use them
            if (DBOption.GetOptions(DBOption.cNewEpisodeThumbType) == (int)NewEpisodeIndicatorType.none ||
                DBOption.GetOptions(DBOption.cNewEpisodeThumbType) == (int)NewEpisodeIndicatorType.unwatched) return;

            MPTVSeriesLog.Write(bigLogMessage("Updating Recently Added"));

            // Clear setting for all series
            DBSeries.GlobalSet(DBOnlineSeries.cHasNewEpisodes, (DBValue)"0");

            // Calculate date for querying database
            DateTime dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
            dt = dt.Subtract(new TimeSpan(DBOption.GetOptions(DBOption.cNewEpisodeRecentDays), 0, 0, 0, 0));
            string date = dt.ToString("yyyy'-'MM'-'dd HH':'mm':'ss");
           
            SQLCondition conditions = new SQLCondition();
            conditions.Add(new DBEpisode(), DBEpisode.cFileDateCreated, date, SQLConditionType.GreaterEqualThan);            
            List<DBEpisode> episodes = DBEpisode.Get(conditions, false);
            
            // Get unique series list
            var seriesIdList = episodes.Select(e => e[DBOnlineEpisode.cSeriesID].ToString()).Distinct().ToList();

            // Set series 'HasNewEpisodes' field if it contains new episodes
            int i = 0;
            foreach (var seriesId in seriesIdList)
            {
                DBSeries series = Helper.getCorrespondingSeries(int.Parse(seriesId));
                if (series != null)
                {
                    MPTVSeriesLog.Write("progress received: UpdateRecentlyAdded [{0}/{1}] {2}", ++i, seriesIdList.Count, series.ToString());
                    series[DBOnlineSeries.cHasNewEpisodes] = "1";
                    series.Commit();
                }
            }
        }

        #region ParseActions
        private void ParseActionRemove(List<PathPair> files) {
            string initialMsg = "*******************        Remove Run Starting      ***********************";
            MPTVSeriesLog.Write(prettyStars(initialMsg.Length));
            MPTVSeriesLog.Write(initialMsg);
            MPTVSeriesLog.Write(prettyStars(initialMsg.Length));
			
            List<DBOnlineSeries> relatedSeries = new List<DBOnlineSeries>();
            List<DBSeason> relatedSeasons = new List<DBSeason>();

            foreach (PathPair pair in files) 
            {               
                DBEpisode episode = new DBEpisode(pair.m_sFull_FileName, true);

                // already in?
                bool bSeasonFound = false;
                foreach (DBSeason season in relatedSeasons)
                {
                    if (season[DBSeason.cSeriesID] == episode[DBEpisode.cSeriesID] && season[DBSeason.cIndex] == episode[DBEpisode.cSeasonIndex])
                    {
                        bSeasonFound = true;
                        break;
                    }
                }
                if (!bSeasonFound)
                    relatedSeasons.Add(new DBSeason(episode[DBEpisode.cSeriesID], episode[DBEpisode.cSeasonIndex]));

                bool bSeriesFound = false;
                foreach (DBOnlineSeries series in relatedSeries)
                {
                    if (series[DBOnlineSeries.cID] == episode[DBEpisode.cSeriesID])
                    {
                        bSeriesFound = true;
                        break;
                    }
                }
                if (!bSeriesFound)
                    relatedSeries.Add(new DBOnlineSeries(episode[DBEpisode.cSeriesID]));

                SQLCondition condition = new SQLCondition();
                condition.Add(new DBEpisode(), DBEpisode.cFilename, pair.m_sFull_FileName, SQLConditionType.Equal);
                
                // clear the file if we know for certain it doesnt exist anymore
                // if removable drive or network path is offline then we dont remove
                string importPath = LocalParse.getImportPath(pair.m_sFull_FileName);
                if (string.IsNullOrEmpty(importPath) || (Directory.Exists(importPath) && !File.Exists(pair.m_sFull_FileName) && string.Equals(DeviceManager.GetVolumeLabel(importPath), DeviceManager.GetVolumeLabel(pair.m_sFull_FileName))))
                {
                    MPTVSeriesLog.Write("Episode is marked for removal from database, file: {0}", pair.m_sFull_FileName);
                    DBEpisode.Clear(condition);
                    mDataUpdated = true;
                }
                else 
                {
                    DBEpisode.GlobalSet(DBEpisode.cImportProcessed, 2, condition);
                    DBEpisode.GlobalSet(DBEpisode.cIsAvailable, 0, condition);
                    mDataUpdated = true;
                }               
            }

            // now go over the touched seasons & series
            bool seriesHasLocalFilesTemp = false;
            bool seasonHasLocalFilesTemp = false;
            foreach (DBSeason season in relatedSeasons) {
                foreach (DBEpisode episode1 in DBEpisode.Get(season[DBSeason.cSeriesID], season[DBSeason.cIndex], false)) {
                    if (episode1[DBEpisode.cImportProcessed] == 1) {
                        seasonHasLocalFilesTemp = true;
                        break;
                    }
                }

                season[DBSeason.cHasLocalFilesTemp] = seasonHasLocalFilesTemp;

                if (DBEpisode.Get(season[DBSeason.cSeriesID], season[DBSeason.cIndex], false).Count > 0) {
                    //season[DBSeason.cHasLocalFilesTemp] = true;
                    season[DBSeason.cHasEpisodes] = true;
                }
                else {
                    //season[DBSeason.cHasLocalFilesTemp] = false;
                    season[DBSeason.cHasEpisodes] = false;
                }

                DBEpisode episode = DBEpisode.GetFirstUnwatched(season[DBSeason.cSeriesID], season[DBSeason.cIndex]);
                if (episode != null)
                    season[DBSeason.cUnwatchedItems] = true;
                else
                    season[DBSeason.cUnwatchedItems] = false;

                season.Commit();
            }

            foreach (DBOnlineSeries series in relatedSeries) {
                	// Just bug fixing :)
					foreach (DBEpisode episode1 in DBEpisode.Get(series[DBOnlineSeries.cID], false)) {
                    if (episode1[DBEpisode.cImportProcessed] == 1) {
                        seriesHasLocalFilesTemp = true;
                        break;
                    }
                }

                series[DBOnlineSeries.cHasLocalFilesTemp] = seriesHasLocalFilesTemp;

                //if (DBEpisode.Get((int)series[DBOnlineSeries.cID], false).Count > 0)
                //    series[DBOnlineSeries.cHasLocalFilesTemp] = true;
                //else
                //    series[DBOnlineSeries.cHasLocalFilesTemp] = false;

                DBEpisode episode = DBEpisode.GetFirstUnwatched(series[DBSeries.cID]);
                if (episode != null)
                    series[DBOnlineSeries.cUnwatchedItems] = true;
                else
                    series[DBOnlineSeries.cUnwatchedItems] = false;

                series.Commit();
            }

            // and copy the HasLocalFileTemp value into the real one
            DBSeries.GlobalSet(DBOnlineSeries.cHasLocalFiles, DBOnlineSeries.cHasLocalFilesTemp);
            DBSeason.GlobalSet(DBSeason.cHasLocalFiles, DBSeason.cHasLocalFilesTemp);
        }

        private void ParseActionAdd(List<PathPair> files) {
            string initialMsg = "*******************       Add Run Starting     ***************************";
            MPTVSeriesLog.Write(prettyStars(initialMsg.Length));
            MPTVSeriesLog.Write(initialMsg);
            MPTVSeriesLog.Write(prettyStars(initialMsg.Length));
            ParseLocal(files);

            // GUI refresh after update actions
            // and copy the HasLocalFileTemp value into the real one
            DBSeries.GlobalSet(DBOnlineSeries.cHasLocalFiles, DBOnlineSeries.cHasLocalFilesTemp);
            DBSeason.GlobalSet(DBSeason.cHasLocalFiles, DBSeason.cHasLocalFilesTemp);
        }

        private void ParseActionLocalScan(bool localScan, bool updateScan, IList<parseResult> UserModifiedParsedResults)
        {
            if (UserModifiedParsedResults == null) // only if we dont get the results from the user
            {
                string initialMsg = String.Format("******************* Full Run Starting (LocalScan: {0} -  UpdateScan: {1})   ***************************", localScan, updateScan);
                if (mNoExactMatch)
                {
                    initialMsg = String.Format("******************* NoExactMatch Run Starting (LocalScan: {0} -  UpdateScan: {1})   ***************************", localScan, updateScan);
                }

                MPTVSeriesLog.Write(prettyStars(initialMsg.Length));
                MPTVSeriesLog.Write(initialMsg);
                MPTVSeriesLog.Write(prettyStars(initialMsg.Length));
            }
            if (localScan) {
                // mark all files in the db as not processed (to figure out which ones we'll have to remove after the import)
                DBEpisode.GlobalSet(DBEpisode.cImportProcessed, 2);                
                DBEpisode.GlobalSet(DBEpisode.cIsAvailable, 2); // dont set to 0 so we dont filter out eps in view during localscan

                // also clear all season & series for local files
                DBSeries.GlobalSet(DBOnlineSeries.cHasLocalFilesTemp, false);
                DBSeason.GlobalSet(DBSeason.cHasLocalFilesTemp, false);

                List<String> listFolders = new List<string>();
                DBImportPath[] importPathes = DBImportPath.GetAll();
                if (importPathes != null) {
                    foreach (DBImportPath importPath in importPathes) {
                        if (importPath[DBImportPath.cEnabled] != 0) {
                            listFolders.Add(importPath[DBImportPath.cPath]);
                        }
                    }
                }

                // NOTE: previously tried skipping this scan when UserModifiedParsedResults was available
                // (reusing Step 1's file list instead of re-scanning), on the assumption that list
                // represented the whole library. It doesn't - Step 1 only ever shows *new* files (see its
                // "X new files found" label), not files already correctly tracked in the database. Reusing
                // THAT list here meant every already-imported show's episodes never got reconfirmed as
                // still present, and the removal-cleanup logic below then deleted them from the database,
                // wrongly treating them as gone from disk. That was the real, serious bug.
                //
                // mParsingParameters.m_files is a different, genuinely complete list - every file Step 1's
                // scan found, new and already-known alike, captured in ImportPanelParsing.cs before it gets
                // filtered down to the "new files" list above. GUIConfiguration.cs already carries it this
                // far (see ImportWizardParseParams.m_files, with its own "else they will be marked as
                // removed" comment) but nothing was actually reading it here before now - so the wizard
                // still walked the entire filesystem a second time regardless. Using it when present avoids
                // that second walk safely, since it's complete enough for the reconfirmation above to work
                // correctly - unlike the UserModifiedParsedResults attempt, nothing here is filtered down to
                // "new files only". Anything that builds a CParsingParameters without this field (the
                // scheduled scan, the quick per-series ID-edit refresh, etc.) leaves it null by default and
                // falls through to a fresh scan exactly as before, so this only changes behaviour for the
                // one wizard flow that actually has a complete list ready to hand it.
                List<PathPair> filesForThisScan;
                if (mParsingParameters.m_files != null && mParsingParameters.m_files.Count > 0)
                {
                    MPTVSeriesLog.Write("Reusing the file list from the wizard's first scan instead of scanning the filesystem again");
                    filesForThisScan = mParsingParameters.m_files;
                }
                else
                {
                    filesForThisScan = Filelister.GetFiles(listFolders);
                }
                ParseLocal(filesForThisScan, UserModifiedParsedResults);

                // now, remove all episodes still processed = 0, the weren't find in the scan
                SQLCondition condition = new SQLCondition();
                condition.Add(new DBEpisode(), DBEpisode.cImportProcessed, 2, SQLConditionType.Equal);

                // remove references to files that we know for certain dont exist anymore
                // if import path is not available then could be offline
                List<DBEpisode> localepisodes = DBEpisode.Get(condition);
                foreach (DBImportPath path in DBImportPath.GetAll())
                {
                    string importPath = path[DBImportPath.cPath];
                    if (path[DBImportPath.cEnabled] && !Directory.Exists(importPath)) //  && !LocalParse.needToKeepReference(importPath)
                    {
                        MPTVSeriesLog.Write("Import path '{0}' is not available, ignoring database maintenance on this path until available.", importPath);
                        localepisodes.RemoveAll(ep => ep[DBEpisode.cFilename].ToString().Contains(importPath));
                    }
                    // check if same importpath has different volume labels and keep them as well
                    // this can occur if you have multiple DVDs or USB disks that share the same drive letter
                    // but each removable disk have a unique volume label (volume serial should be used but we dont store that)
                    if (path[DBImportPath.cEnabled] && Directory.Exists(importPath) && !string.IsNullOrEmpty(DeviceManager.GetVolumeLabel(importPath)))
                    {
                        localepisodes.RemoveAll(ep => ep[DBEpisode.cFilename].ToString().Contains(importPath) &&
                                                      !string.Equals(ep[DBEpisode.cVolumeLabel].ToString(), DeviceManager.GetVolumeLabel(importPath)));
                    }
                }

                // clear local database references for files that dont exist anymore
                foreach (DBEpisode localepisode in localepisodes)
                {
                    MPTVSeriesLog.Write("Episode is marked for removal from database, file: '{0}', volume label: '{1}'", localepisode[DBEpisode.cFilename], localepisode[DBEpisode.cVolumeLabel]);
                    DBEpisode.Clear(new SQLCondition(new DBEpisode(), DBEpisode.cFilename, localepisode[DBEpisode.cFilename], SQLConditionType.Equal));                 
                }

                // mark all remaining episodes in database as not available                
                condition = new SQLCondition();
                condition.Add(new DBEpisode(), DBEpisode.cImportProcessed, 2, SQLConditionType.Equal);                
                DBEpisode.GlobalSet(DBEpisode.cIsAvailable, false, condition);

                // and copy the HasLocalFileTemp value into the real one
                DBSeries.GlobalSet(DBOnlineSeries.cHasLocalFiles, DBOnlineSeries.cHasLocalFilesTemp);
                DBSeason.GlobalSet(DBSeason.cHasLocalFiles, DBSeason.cHasLocalFilesTemp);
            }
        }

        private void GetSeries(BackgroundWorker worker, bool bNoExactMatch, Dictionary<string, UserInputResultSeriesActionPair> preChosenSeriesPairs)
        {
            MPTVSeriesLog.Write(bigLogMessage("Identifying Unknown Series Online"), MPTVSeriesLog.LogLevel.Debug);

            SQLCondition condition = null;

            condition = new SQLCondition();
            // all series that don't have an onlineID ( < 0) and not marked as ignored
            condition.Add(new DBSeries(), DBSeries.cID, 0, SQLConditionType.LessThan);
            condition.Add(new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal);

            int nIndex = 0;
            List<DBSeries> seriesList = DBSeries.Get(condition, false, false);
        
            if (seriesList.Count > 0)            
            {
                // Run Online update when needed
                mOnlineUpdateNeeded = true;
                MPTVSeriesLog.Write(string.Format("Found {0} unknown Series, attempting to identify them now", seriesList.Count), MPTVSeriesLog.LogLevel.Debug);
            }
            else
                MPTVSeriesLog.Write("All Series are already identified", MPTVSeriesLog.LogLevel.Debug);

            foreach (DBSeries series in seriesList)
            {
                if (worker.CancellationPending)
                    return;

                String sSeriesNameToSearch = series[DBSeries.cParsedName];                
                DBOnlineSeries UserChosenSeries = null;
                UserInputResultSeriesActionPair sap = null;

                if (preChosenSeriesPairs != null)
                    preChosenSeriesPairs.TryGetValue(sSeriesNameToSearch, out sap);

                if (preChosenSeriesPairs == null)
                {
                    // an id already sitting in a local tvshow.nfo / series.xml beats a fuzzy name match
                    UserChosenSeries = TryGetSeriesFromLocalIdFile(series, sSeriesNameToSearch);
                    if (UserChosenSeries == null)
                        UserChosenSeries = SearchForSeries(sSeriesNameToSearch, -1, bNoExactMatch, mFeedback);
                }
                else if (sap != null && sap.RequestedAction == UserInputResults.SeriesAction.Approve)
                {
                    MPTVSeriesLog.Write("User has approved \"" + sSeriesNameToSearch + "\" as being: " + sap.ChosenSeries.ToString(), MPTVSeriesLog.LogLevel.Debug);
                    UserChosenSeries = sap.ChosenSeries;
                }
                else if (sap != null && sap.RequestedAction == UserInputResults.SeriesAction.IgnoreAlways)
                {
                    // duplicate code from SearchForSeries - should be changed
                    MPTVSeriesLog.Write("User has Ignored \"" + sSeriesNameToSearch + "\" in the future", MPTVSeriesLog.LogLevel.Debug);
                    DBSeries ignoreSeries = new DBSeries(sSeriesNameToSearch);
                    series[DBSeries.cScanIgnore] = 1; // means it will be skipped in the future
                    series[DBSeries.cHidden] = true;
                    series.Commit();
                }

                string seriesName = UserChosenSeries != null ? UserChosenSeries[DBOnlineSeries.cPrettyName].ToString() : sSeriesNameToSearch;
                worker.ReportProgress(0, new ParsingProgress(ParsingAction.IdentifyNewSeries, seriesName, ++nIndex, seriesList.Count, series, null));

                if (UserChosenSeries != null) // make sure selection was not cancelled
                {                    
                    // set the ID on the current series with the one from the chosen one
                    // we need to update all depending items - seasons & episodes
                    List<DBSeason> seasons = DBSeason.Get(series[DBSeries.cID]);
                    foreach (DBSeason season in seasons)
                        season.ChangeSeriesID(UserChosenSeries[DBSeries.cID]);

                    SQLCondition setcondition = new SQLCondition();
                    setcondition.Add(new DBSeason(), DBSeason.cSeriesID, series[DBSeries.cID], SQLConditionType.Equal);
                    DBSeason.Clear(setcondition);

                    setcondition = new SQLCondition();
                    setcondition.Add(new DBEpisode(), DBEpisode.cSeriesID, series[DBSeries.cID], SQLConditionType.Equal);
                    List<DBEpisode> episodes = DBEpisode.Get(setcondition, false);
                    foreach (DBEpisode episode in episodes)
                        episode.ChangeSeriesID(UserChosenSeries[DBSeries.cID]);

                    setcondition = new SQLCondition();
                    setcondition.Add(new DBOnlineEpisode(), DBOnlineEpisode.cSeriesID, series[DBSeries.cID], SQLConditionType.Equal);
                    DBOnlineEpisode.Clear(setcondition);

                    int nSeriesID = series[DBSeries.cID];

                    series.ChangeSeriesID(UserChosenSeries[DBSeries.cID]);
                    series.Commit();

                    setcondition = new SQLCondition();
                    setcondition.Add(new DBOnlineSeries(), DBOnlineSeries.cID, nSeriesID, SQLConditionType.Equal);
                    DBOnlineSeries.Clear(setcondition);

                    // only keep one local dbseries marked as non dupe
                    setcondition = new SQLCondition();
                    setcondition.Add(new DBOnlineSeries(), DBOnlineSeries.cID, UserChosenSeries[DBSeries.cID], SQLConditionType.Equal);
                    List<DBSeries> seriesDupeSetList = DBSeries.Get(setcondition);
                    bool bFirst = true;
                    foreach (DBSeries seriesDupeSet in seriesDupeSetList)
                    {
                        if (bFirst)
                        {
                            seriesDupeSet[DBSeries.cDuplicateLocalName] = 0;
                            seriesDupeSet.Commit();
                            bFirst = false;
                        }
                        else
                        {
                            seriesDupeSet[DBSeries.cDuplicateLocalName] = 1;
                            seriesDupeSet.Commit();
                        }
                    }
                }               
            }
            // that is done
            worker.ReportProgress(0, new ParsingProgress(ParsingAction.IdentifyNewSeries, seriesList.Count));
        }

        // set when this import is doing the full refresh (see DBOption.cFullTextRefreshPending)
        private bool mFullRefreshThisRun = false;

        /// <summary>
        /// Adds every series in the database, and all their episodes, to the update list, marked as updated now - so
        /// their saved TheTVDB copies are thrown away and their titles, descriptions and episode names are fetched
        /// again in each series' language. Pictures are not part of this, so nothing is downloaded again.
        /// </summary>
        private void AddEverySeriesForFullRefresh(Online_Parsing_Classes.GetUpdates lUpdates)
        {
            var condition = new SQLCondition();
            condition.Add(new DBOnlineSeries(), DBOnlineSeries.cID, 0, SQLConditionType.GreaterThan);
            var allSeriesIds = DBOnlineSeries.GetSingleField(DBOnlineSeries.cID, condition, new DBOnlineSeries())
                                             .Select(v => (string)v).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();

            long now = DateTime.UtcNow.ToEpoch();
            var seriesAlready = new HashSet<string>(lUpdates.UpdatedSeries.Keys.Select(k => (string)k));
            foreach (string id in allSeriesIds)
            {
                if (seriesAlready.Contains(id))
                {
                    // already listed: make sure its saved copy counts as out of date
                    var key = lUpdates.UpdatedSeries.Keys.First(k => (string)k == id);
                    lUpdates.UpdatedSeries[key] = now;
                }
                else
                    lUpdates.UpdatedSeries.Add(id, now);
            }

            var episodeCondition = new SQLCondition();
            episodeCondition.Add(new DBOnlineEpisode(), DBOnlineEpisode.cID, 0, SQLConditionType.GreaterThan);
            var episodesAlready = new HashSet<string>(lUpdates.UpdatedEpisodes.Keys.Select(k => (string)k));
            int episodeCount = 0;
            foreach (DBValue episodeId in DBOnlineEpisode.GetSingleField(DBOnlineEpisode.cID, episodeCondition, new DBOnlineEpisode()))
            {
                string id = episodeId;
                if (string.IsNullOrEmpty(id) || !episodesAlready.Add(id)) continue;
                lUpdates.UpdatedEpisodes.Add(id, now);
                episodeCount++;
            }

            // the stored "original name" may have been saved from a wrong-language copy too; it's looked up again when needed
            DBOnlineSeries.GlobalSet(new DBOnlineSeries(), DBOnlineSeries.cOriginalName, (DBValue)string.Empty, new SQLCondition());

            mFullRefreshThisRun = true;
            MPTVSeriesLog.Write(string.Format("Refreshing the details of all {0} series ({1} episodes) from " + Online_Parsing_Classes.OnlineSource.Name + ", so titles, descriptions and episode names are in each series' language. This takes a while, once.",
                                              allSeriesIds.Count, episodeCount));
        }

        private Online_Parsing_Classes.GetUpdates GetOnlineUpdates()
        {
            MPTVSeriesLog.Write(bigLogMessage("Processing updates from online database"));

            // let's get the time we last updated from the local options
            long lastUpdateTimeStamp = DBOption.GetOptions(DBOption.cUpdateTimeStamp);
            long curTimeStamp = DateTime.UtcNow.ToEpoch();
            long sinceLastUpdate = curTimeStamp - lastUpdateTimeStamp;

            Online_Parsing_Classes.OnlineAPI.UpdateType uType = Online_Parsing_Classes.OnlineAPI.UpdateType.all;
            if (sinceLastUpdate < 3600 * 24)
                uType = Online_Parsing_Classes.OnlineAPI.UpdateType.day;
            else if (sinceLastUpdate < 3600 * 24 * 7)
                uType = Online_Parsing_Classes.OnlineAPI.UpdateType.week;
            else if (sinceLastUpdate < 3600 * 24 * 30)
                uType = Online_Parsing_Classes.OnlineAPI.UpdateType.month;

            var lUpdates = new Online_Parsing_Classes.GetUpdates(uType);
            if ( lUpdates.UpdatedSeries == null ) return null;

            if (DBOption.GetOptions(DBOption.cFullTextRefreshPending))
                AddEverySeriesForFullRefresh(lUpdates);

            // series whose details were downloaded earlier in this import (e.g. every show on a first import) already
            // have a fresh saved copy - don't download them all a second time
            var lFreshlyFetched = lUpdates.UpdatedSeries.Keys.Where(k => Online_Parsing_Classes.OnlineSource.FetchedThisSession((string)k)).ToList();
            foreach (var key in lFreshlyFetched) lUpdates.UpdatedSeries.Remove(key);
            if (lFreshlyFetched.Count > 0)
                MPTVSeriesLog.Write(string.Format("{0} series were already downloaded in this import, so they are not downloaded again", lFreshlyFetched.Count));

            MPTVSeriesLog.Write("Available series with updates: {0}", lUpdates.UpdatedSeries?.Count);
            MPTVSeriesLog.Write("Available episodes with updates: {0}", lUpdates.UpdatedEpisodes?.Count);
            MPTVSeriesLog.Write("Avaialble series with updated Backdrops: {0}", lUpdates.UpdatedFanart?.Count);
            MPTVSeriesLog.Write("Available series with updated Artwork: {0}", lUpdates.UpdatedBanners?.Count);

            #region remove cache files that need updating

            string lCacheDir = Path.Combine(Settings.GetPath(Settings.Path.config), @"Cache");

            if (Directory.Exists(lCacheDir))
            {
                // get series cache directories
                var lSeriesDirs = new DirectoryInfo(lCacheDir).GetDirectories().Select(d => d.Name).ToList();

                foreach (var series in lUpdates.UpdatedSeries.Where(s => lSeriesDirs.Contains(s.Key)))
                {
                    bool lCacheCleared = false;

                    var lLanguageDirs = new DirectoryInfo( Path.Combine( Settings.GetPath( Settings.Path.config ), string.Format( @"Cache\{0}", series.Key ) ) ).GetDirectories();
                    foreach (var lDirectory in lLanguageDirs)
                    {
                        FileInfo[] files = lDirectory.GetFiles();
                        foreach ( FileInfo file in files )
                        {
                            if ( file.LastWriteTime.ToUniversalTime().ToEpoch() <= series.Value )
                            {
                                lCacheCleared = true;
                                try { file.Delete(); }
                                catch
                                {
                                    lCacheCleared = false;
                                    MPTVSeriesLog.Write( "Error removing cache: {0}", file.FullName );
                                }
                            }
                        }
                    }
                    
                    if (lCacheCleared)
                    {
                        DBSeries s = Helper.getCorrespondingSeries(series.Key);
                        MPTVSeriesLog.Write("Cache cleared for series: {0}", s == null ? series.Key.ToString() : s.ToString());

                        // try delete the directory...we probably no longer have a reference to it in the database
                        if ( s == null )
                        {
                            try
                            {
                                Directory.Delete( Path.Combine( lCacheDir, series.Key ), true );
                            }
                            catch(Exception ex)
                            {
                                MPTVSeriesLog.Write( $"Failed to delete '{series.Key}' cache directory, Exception: {ex.Message}" );
                            }
                        }
                    }
                }
            }

            #endregion

            MPTVSeriesLog.Write(bigLogMessage("Finished processing updates from online database"));

            return lUpdates;
        }

        private void UpdateSeries(bool bUpdateNewSeries, List<DBValue> seriesUpdated)
        {
            // now retrieve the info about the series
            SQLCondition condition = new SQLCondition();
            // all series that have an onlineID ( != 0)
            condition.Add(new DBOnlineSeries(), DBOnlineSeries.cID, 0, SQLConditionType.GreaterThan);
            condition.Add(new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal);
            condition.Add(new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal);
            if (bUpdateNewSeries) {
                MPTVSeriesLog.Write(bigLogMessage("Retrieving Metadata for new Series"));
                // and that never had data imported from the online DB
                condition.Add(new DBOnlineSeries(), DBOnlineSeries.cOnlineDataImported, 0, SQLConditionType.Equal);
            } else {
                MPTVSeriesLog.Write(bigLogMessage("Updating Metadata for existing Series"));
                // and that already had data imported from the online DB (but not the new ones, that are set to 1) ??
                condition.Add(new DBOnlineSeries(), DBOnlineSeries.cOnlineDataImported, 2, SQLConditionType.Equal);
            }
            List<DBSeries> SeriesList = DBSeries.Get(condition, false, false);

            if (!bUpdateNewSeries && SeriesList.Count > 0)
            {
                // let's check which of these we have any interest in
                var wantedSeriesIds = new HashSet<string>(seriesUpdated.Select(v => (string)v));
                SeriesList.RemoveAll(s => !wantedSeriesIds.Contains((string)s[DBSeries.cID]));
            }

            if (SeriesList.Count == 0) {
                MPTVSeriesLog.Write("Nothing to do");
                return;
            }

            MPTVSeriesLog.Write(string.Format("{0} metadata of {1} Series", (bUpdateNewSeries ? "Retrieving" : "Looking for updated"), SeriesList.Count), MPTVSeriesLog.LogLevel.Debug);

            UpdateSeries UpdateSeriesParser = new UpdateSeries(generateIDListOfString(SeriesList, DBSeries.cID));
            
            int nIndex = 0;
            bool traktCommunityRatings = DBOption.GetOptions(DBOption.cTraktCommunityRatings) && Helper.IsTraktAvailableAndEnabled;

            foreach (DBOnlineSeries updatedSeries in UpdateSeriesParser.ResultsLazy) {
                mDataUpdated = true;
                if (mWorker.CancellationPending)
                    return;
                               
                MPTVSeriesLog.Write(string.Format("Metadata {0} for \"{1}\"", (bUpdateNewSeries ? "retrieved" : "updated"), updatedSeries.ToString()), MPTVSeriesLog.LogLevel.Debug);
                // find the corresponding series in our list
                foreach (DBSeries localSeries in SeriesList) {
                    if (localSeries[DBSeries.cID] == updatedSeries[DBSeries.cID]) {
                        mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateSeries, updatedSeries[DBOnlineSeries.cPrettyName], ++nIndex, SeriesList.Count, updatedSeries, null));
                        // go over all the fields, (and update only those which haven't been modified by the user - will do that later)
                        foreach (String key in updatedSeries.FieldNames) {
                            switch (key) {
                                // do not overwrite current series local settings with the one from the online series
                                case DBSeries.cParsedName: // this field shouldn't be required here since updatedSeries is an Onlineseries and not a localseries??
                                case DBOnlineSeries.cHasLocalFiles:
                                case DBOnlineSeries.cHasLocalFilesTemp:
                                case DBOnlineSeries.cEpisodesUnWatched:
                                case DBOnlineSeries.cEpisodeCount:
                                case DBOnlineSeries.cIsFavourite:
                                case DBOnlineSeries.cChosenEpisodeOrder:
                                case DBOnlineSeries.cEpisodeSortOrder:
                                case DBOnlineSeries.cBannerFileNames: // banners get handled differently (later on)
                                case DBOnlineSeries.cPosterFileNames:
                                case DBOnlineSeries.cCurrentBannerFileName:
                                case DBOnlineSeries.cCurrentPosterFileName:
                                case DBOnlineSeries.cMyRating:
                                case DBOnlineSeries.cMyRatingAt:
                                case DBOnlineSeries.cViewTags:
                                case DBOnlineSeries.cHasNewEpisodes: //gets cleared and updated at end of scan
                                case DBOnlineSeries.cTraktIgnore:
                                case DBOnlineSeries.cOriginalName:
                                case DBOnlineSeries.cTraktID:
                                case DBOnlineSeries.cSlug:
                                case DBOnlineSeries.cTmdbId:
                                case DBOnlineSeries.cArtworkChooserProvider:
                                    break;

                                // dont update community ratings if we get from trakt
                                case DBOnlineSeries.cRating:
                                case DBOnlineSeries.cRatingCount:
                                    if (traktCommunityRatings)
                                        break;
                                    // TheTVDB v4 supplies no rating (see TvdbV4XmlAdapter), so an empty value must not
                                    // wipe a genuine rating saved earlier - except an over-10 value, which is a v4
                                    // popularity score saved by earlier builds of this fork, never a real rating.
                                    if (string.IsNullOrEmpty(updatedSeries[key]))
                                    {
                                        double stored;
                                        if (key == DBOnlineSeries.cRating && double.TryParse(localSeries[key], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out stored) && stored > 10)
                                            localSeries[key] = string.Empty;
                                        break;
                                    }
                                    goto default;
                                    
                                case DBOnlineSeries.cEpisodeOrders:
                                    if (bUpdateNewSeries)
                                        goto default;
                                    break;
                                default:
                                    if (!key.EndsWith(DBTable.cUserEditPostFix))
                                    {
                                        localSeries.AddColumn(key, new DBField(DBField.cTypeString));
                                        localSeries[key] = updatedSeries[key].ToString().RemapHighOrderChars();
                                    }
                                    break;
                            }
                        }

                        // diff. order options
                        if (bUpdateNewSeries)
                            SetEpisodeOrderForSeries(localSeries);

                        // data import completed; set to 2 (data up to date)
                        localSeries[DBOnlineSeries.cOnlineDataImported] = 2;

                        if (localSeries[DBOnlineSeries.cHasLocalFilesTemp])
                            localSeries[DBOnlineSeries.cHasLocalFiles] = 1;
                        
                        localSeries.Commit();
                        
                        // UPDATE CACHE to fix getting the series named as the parsed name instead of the online pretty name!
                        if(bUpdateNewSeries) cache.addChangeSeries(localSeries);
                    }
                }
            }
            if (nIndex == 0)
            {
                MPTVSeriesLog.Write(string.Format("No {0} found", (bUpdateNewSeries ? "metadata" : "updates")));
                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateSeries, 0));
            }
            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateSeries,  UpdateSeriesParser.Results.Count));
        }

        private void GetEpisodes(bool bUpdateScan, bool bFullSeriesRetrieval)
        {
            MPTVSeriesLog.Write(bigLogMessage("Get Episodes"));
            SQLCondition condition = null;
            if (bUpdateScan) 
            {
                // mark existing online data as "old", needs a refresh
                condition = new SQLCondition();
                condition.Add(new DBOnlineEpisode(), DBOnlineEpisode.cOnlineDataImported, 2, SQLConditionType.Equal);
                DBEpisode.GlobalSet(DBOnlineEpisode.cOnlineDataImported, 1, condition);
            }

            condition = new SQLCondition();
            // all series that have an onlineID ( != 0)
            condition.Add(new DBSeries(), DBSeries.cID, 0, SQLConditionType.GreaterThan);
            condition.Add(new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal);
            condition.Add(new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal);

            List<DBSeries> seriesList = DBSeries.Get(condition, false, false);

            if (bFullSeriesRetrieval && bUpdateScan)
                MPTVSeriesLog.Write("Mode: Get all Episodes of Series");

            int epCount = 0;
            int nIndex = 0;
            bool traktCommunityRatings = DBOption.GetOptions(DBOption.cTraktCommunityRatings) && Helper.IsTraktAvailableAndEnabled;
            
            // clear reference to existing online episodes
            mOnlineEpisodes.Clear();

            foreach (DBSeries series in seriesList) 
            {
                List<DBEpisode> episodesList = null;

                // lets get the list of unidentified episodes
                SQLCondition conditions = new SQLCondition();               
                conditions.Add(new DBOnlineEpisode(), DBOnlineEpisode.cSeriesID, series[DBSeries.cID], SQLConditionType.Equal);
                conditions.Add(new DBOnlineEpisode(), DBOnlineEpisode.cID, 0, SQLConditionType.Equal);
                conditions.Add(new DBEpisode(), DBEpisode.cFilename, string.Empty, SQLConditionType.NotEqual);
                episodesList = DBEpisode.Get(conditions, false);

                // we may already have all the online episode references
                // but need to update the composite id's of new episodes added
                if (episodesList.Count == 0 && !(string.IsNullOrEmpty(series[DBOnlineSeries.cChosenEpisodeOrder]) || series[DBOnlineSeries.cChosenEpisodeOrder] == "Aired"))
                {
                    conditions = new SQLCondition();
                    conditions.Add(new DBOnlineEpisode(), DBOnlineEpisode.cSeriesID, series[DBSeries.cID], SQLConditionType.Equal);
                    conditions.Add(new DBEpisode(), DBEpisode.cCompositeUpdated, 0, SQLConditionType.Equal);
                    episodesList = DBEpisode.Get(conditions, false);
                }

                epCount += episodesList.Count;
                                
                if (bFullSeriesRetrieval || episodesList.Count > 0) 
                {
                    GetEpisodes episodesParser = new GetEpisodes((string)series[DBSeries.cID]);
                    mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.IdentifyNewEpisodes, series[DBOnlineSeries.cPrettyName], ++nIndex, seriesList.Count, series, null));
                    if (episodesParser.Results.Count > 0) 
                    {
                        MPTVSeriesLog.Write(string.Format("Found {0} episodes online for \"{1}\"", episodesParser.Results.Count.ToString().PadLeft(3, '0'), series.ToString()), MPTVSeriesLog.LogLevel.Debug);
                        // look for the episodes for that series, and compare / update the values
                        if (mParsingParameters.UserEpisodeMatcher == null) // auto mode
                            matchOnlineToLocalEpisodes(series, episodesList, episodesParser);
                        else // user mode
                            mParsingParameters.UserEpisodeMatcher.MatchEpisodesForSeries(series, episodesList, episodesParser.Results);

                        // add online episode for cleanup task
                        mOnlineEpisodes.Add(series[DBOnlineSeries.cID], episodesParser.Results);
                    } 
                    else
                        MPTVSeriesLog.Write(string.Format("No episodes could be identified online for {0}, check that the online database has these episodes", series.ToString()));
                    
                    if (bFullSeriesRetrieval) 
                    {
                        // add all online episodes to the database                 
                        foreach (DBOnlineEpisode onlineEpisode in episodesParser.Results) 
                        {
                            // season if not there yet
                            DBSeason season = new DBSeason(series[DBSeries.cID], onlineEpisode[DBOnlineEpisode.cSeasonIndex]);
                            season[DBSeason.cHasEpisodes] = true;
                            DBSeason existing = DBSeason.getRaw(series[DBSeries.cID], onlineEpisode[DBOnlineEpisode.cSeasonIndex]);
                            if (existing != null) 
                            {
                                season[DBSeason.cHasLocalFiles] = existing[DBSeason.cHasLocalFilesTemp];
                                season[DBSeason.cHasLocalFilesTemp] = existing[DBSeason.cHasLocalFilesTemp];
                            }
                            season.Commit();

                            DBOnlineEpisode newOnlineEpisode = new DBOnlineEpisode(series[DBSeries.cID], onlineEpisode[DBOnlineEpisode.cSeasonIndex], onlineEpisode[DBOnlineEpisode.cEpisodeIndex]);
                            newOnlineEpisode[DBOnlineEpisode.cID] = onlineEpisode[DBOnlineEpisode.cID];
                            if (newOnlineEpisode[DBOnlineEpisode.cEpisodeName].ToString().Length == 0)
                                newOnlineEpisode[DBOnlineEpisode.cEpisodeName] = onlineEpisode[DBOnlineEpisode.cEpisodeName];
                            foreach (String key in onlineEpisode.FieldNames) 
                            {
                                switch (key) 
                                {
                                    case DBOnlineEpisode.cCompositeID:
                                    case DBEpisode.cSeriesID:
                                    case DBOnlineEpisode.cWatched:
                                    case DBOnlineEpisode.cLastWatchedDate:
                                    case DBOnlineEpisode.cFirstWatchedDate:
                                    case DBOnlineEpisode.cPlayCount:
                                    case DBOnlineEpisode.cHidden:
                                    case DBOnlineEpisode.cMyRating:
                                    case DBOnlineEpisode.cMyRatingAt:
                                    case DBOnlineEpisode.cEpisodeThumbnailFilename:
                                    case DBOnlineEpisode.cEpisodeThumbnailSource:
                                    case DBOnlineEpisode.cTMDbEpisodeThumbnailUrl:
                                        // do nothing here, this information is local only
                                        break;

                                    // don't update community ratings if we get from trakt
                                    case DBOnlineEpisode.cRating:
                                    case DBOnlineEpisode.cRatingCount:
                                        if (traktCommunityRatings)
                                            break;
                                        goto default;

                                    case DBOnlineEpisode.cSeasonIndex:
                                    case DBOnlineEpisode.cEpisodeIndex:
                                        break; // those must not get overwritten from what they were set to by getEpisodes (because of different order options)

                                    default:
                                        if (!key.EndsWith(DBTable.cUserEditPostFix))
                                        {
                                            newOnlineEpisode.AddColumn(key, new DBField(DBField.cTypeString));
                                            newOnlineEpisode[key] = onlineEpisode[key].ToString().RemapHighOrderChars();
                                        }
                                        break;
                                }
                            }
                            newOnlineEpisode.Commit();                        
                        }
                    }
                }
            }
            DBEpisode.GlobalSet(DBEpisode.cCompositeUpdated, 1);

            if (epCount == 0)
                MPTVSeriesLog.Write("No new episodes identified");            
            else 
            {

                // get the result from user matching if needed and commit them
                List<KeyValuePair<DBSeries, List<KeyValuePair<DBEpisode, DBOnlineEpisode>>>> result;
                // this will block here
                if (mParsingParameters.UserEpisodeMatcher != null && mParsingParameters.UserEpisodeMatcher.GetResult(out result) == ReturnCode.OK)
                {
                    foreach (var match in result)
                        foreach (var episodePair in match.Value)
                        {
                            if(episodePair.Value != null && episodePair.Key != null)
                                commitOnlineToLocalEpisodeMatch(match.Key, episodePair.Key, episodePair.Value);
                        }
                }

                // Online update when needed
                if (!bUpdateScan)
                    mOnlineUpdateNeeded = true;
            }
            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.IdentifyNewEpisodes, epCount));
        }

        private void UpdateEpisodes(List<DBValue> episodesUpdated)
        {
            MPTVSeriesLog.Write(bigLogMessage("Updating Metadata for existing Episodes"));

            // let's check which series/episodes we have locally
            SQLCondition cond = new SQLCondition(new DBOnlineEpisode(), DBOnlineEpisode.cID, 0, SQLConditionType.GreaterThan);
            cond.AddOrderItem(DBOnlineEpisode.Q(DBOnlineEpisode.cSeriesID), SQLCondition.orderType.Ascending);

            List<DBEpisode> episodesInDB = DBEpisode.Get(cond);

            if (episodesUpdated == null || episodesUpdated.Count == 0)
                episodesInDB.Clear();

            // let's check which of these we have any interest in
            var wantedEpisodeIds = new HashSet<string>(episodesUpdated == null ? new List<string>() : episodesUpdated.Select(v => (string)v));
            episodesInDB.RemoveAll(e => !wantedEpisodeIds.Contains((string)e[DBOnlineEpisode.cID]));

            if (episodesInDB.Count == 0) {
                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateEpisodes, 0));            
                MPTVSeriesLog.Write("Nothing to do");
                return;
            }

            // let's update those we are interested in
            // for the remaining ones get the <lang>.xml
            MPTVSeriesLog.Write(episodesInDB.Count.ToString() + " episodes need updating", MPTVSeriesLog.LogLevel.Debug);

            int i = 0;
            var distinctSeriesIds = (from s in episodesInDB select s[DBEpisode.cSeriesID].ToString()).Distinct().ToList();
            foreach (var seriesid in distinctSeriesIds)
            {
                var episodes = episodesInDB.Where(e => e[DBEpisode.cSeriesID] == seriesid).ToList();
                DBSeries series = Helper.getCorrespondingSeries(int.Parse(seriesid));
                if (series != null)
                {
                    mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateEpisodes, series.ToString() + " [" + episodes.Count.ToString() + " episodes]", ++i, distinctSeriesIds.Count, series, null));
                    matchOnlineToLocalEpisodes(series, episodes, new GetEpisodes(seriesid));
                }
            }
            
            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateEpisodes, episodesInDB.Count));
        }

        private void UpdateBanners(bool bUpdateNewSeries, List<DBValue> updatedSeries)
        {
            // no updates to process
            if (!bUpdateNewSeries && updatedSeries == null)
                return;

            SQLCondition condition = new SQLCondition();
            // all series that have an onlineID ( > 0)
            condition.Add(new DBOnlineSeries(), DBSeries.cID, 0, SQLConditionType.GreaterThan);
            condition.Add(new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal);
            condition.Add(new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal);
            if (bUpdateNewSeries && !DBOption.GetOptions(DBOption.cAutoDownloadMissingArtwork))
            {
                // "Download missing artwork for existing series" is off - but a series matched for the first time has
                // no pictures at all yet and must still get them (previously it got none, e.g. a newly matched show
                // stayed blank on the Details tab and in MediaPortal)
                MPTVSeriesLog.Write(bigLogMessage("Downloading artwork for newly added series"));
                condition.Add(new DBOnlineSeries(), DBOnlineSeries.cBannerFileNames, string.Empty, SQLConditionType.Equal);
                condition.Add(new DBOnlineSeries(), DBOnlineSeries.cPosterFileNames, string.Empty, SQLConditionType.Equal);
            }
            else if (bUpdateNewSeries)
            {
                MPTVSeriesLog.Write(bigLogMessage("Checking for missing artwork"));
                // and that never had data imported from the online DB
                condition.beginGroup();
                condition.Add(new DBOnlineSeries(), DBOnlineSeries.cBannerFileNames, string.Empty, SQLConditionType.Equal);
                condition.nextIsOr = true;
                condition.Add(new DBOnlineSeries(), DBOnlineSeries.cPosterFileNames, string.Empty, SQLConditionType.Equal);
                condition.nextIsOr = true;
                condition.AddCustom(" exists (select * from season where season.seriesID = online_series.id and season.bannerfilenames = '')");
                if (mSeriesNeedingArtwork.Count > 0)
                {
                    // the artwork check removed some of these series' pictures because their files are missing;
                    // pictures still on disk are skipped by the download, so only the missing ones are fetched
                    condition.nextIsOr = true;
                    condition.AddCustom(" online_series.id in (" + string.Join(",", mSeriesNeedingArtwork.ToArray()) + ")");
                }
                condition.nextIsOr = false;
                condition.endGroup();
            }
            else
            {
                MPTVSeriesLog.Write(bigLogMessage("Checking for new artwork"));
                // and that already had data imported from the online DB
                condition.Add(new DBOnlineSeries(), DBOnlineSeries.cBannerFileNames, string.Empty, SQLConditionType.NotEqual);
                condition.Add(new DBOnlineSeries(), DBOnlineSeries.cPosterFileNames, string.Empty, SQLConditionType.NotEqual);
            }

            List<DBSeries> seriesList = DBSeries.Get(condition, false, false);
            if (!bUpdateNewSeries && seriesList.Count > 0)
            {
                // let's check which of these we have any interest in
                seriesList.RemoveAll(s => !updatedSeries.Contains(s[DBOnlineSeries.cID]));
            }

            int nIndex = 0;
            if (seriesList.Count == 0)
            {
                if (bUpdateNewSeries)
                    MPTVSeriesLog.Write("All series appear to have artwork already", MPTVSeriesLog.LogLevel.Debug);
                else
                    MPTVSeriesLog.Write("Nothing to do");

                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateBanners, 0));
            }
            else
                MPTVSeriesLog.Write("Looking for artwork on " + seriesList.Count + " series", MPTVSeriesLog.LogLevel.Debug);

            foreach (DBSeries series in seriesList)
            {
                if (mWorker.CancellationPending)
                    return;

                MPTVSeriesLog.Write((bUpdateNewSeries ? "Downloading" : "Refreshing") + " artwork for \"" + series.ToString() + "\"", MPTVSeriesLog.LogLevel.Debug);

                nIndex++;                
                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateBanners, series[DBOnlineSeries.cPrettyName], nIndex, seriesList.Count, series, null));

                // get list of available banners online
                GetBanner bannerParser = new GetBanner((string)series[DBSeries.cID]);

                // download banners
                bannerParser.BannerDownloadDone += new NewArtWorkDownloadDoneHandler(name =>
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateBanners, string.Format("{0} - {1}", series[DBOnlineSeries.cPrettyName], name), nIndex, seriesList.Count, series, name));
                    }
                });
                
                // Other language for the Series?
                if (DBOption.GetOptions(DBOption.cOverrideLanguage))
                {
                    //Get the prefered language for the Series.
                    bannerParser.DownloadBanners(Online_Parsing_Classes.OnlineAPI.GetLanguageOverride(series[DBOnlineSeries.cID]));
                }
                else
                {
                    bannerParser.DownloadBanners(Online_Parsing_Classes.OnlineAPI.SelLanguageAsString);
                }

                // update database with available banners
                SeriesBannersMap seriesArtwork = Helper.getElementFromList<SeriesBannersMap, string>(series[DBSeries.cID], "SeriesID", 0, bannerParser.SeriesBannersMap);
                if (seriesArtwork == null) continue;

                #region Series WideBanners
                foreach (WideBannerSeries bannerSeries in seriesArtwork.SeriesWideBanners)
                {
                    // if we have a new banner then add it the list of available ones, possibly a higher rated artwork became available
                    if (!series[DBOnlineSeries.cBannerFileNames].ToString().Contains(bannerSeries.FileName))
                    {
                        mDataUpdated = true;                            
                        MPTVSeriesLog.Write("New series widebanner found for \"" + series.ToString() + "\" : " + bannerSeries.OnlinePath, MPTVSeriesLog.LogLevel.Debug);
                        if (series[DBOnlineSeries.cBannerFileNames].ToString().Trim().Length == 0)
                        {
                            series[DBOnlineSeries.cBannerFileNames] += bannerSeries.FileName;
                        }
                        else
                        {
                            series[DBOnlineSeries.cBannerFileNames] += "|" + bannerSeries.FileName;
                        }
                    }
                }

                // Check if current set series widebanner exists
                string currentArtwork = Helper.PathCombine(Settings.GetPath(Settings.Path.banners), series[DBOnlineSeries.cCurrentBannerFileName].ToString());
                bool currentArtworkExists = File.Exists(currentArtwork);

                // if the current artwork exists, but not in the list of available artwork then add it
                if (currentArtworkExists && !series.BannerList.Exists(b => b.Equals(currentArtwork)))
                {
                    var imageList = new List<string>(series.BannerList);
                    imageList.Add(currentArtwork);
                    series.BannerList = imageList;
                    imageList = null;
                }

                // Don't override user selection of banner
                if ((series[DBOnlineSeries.cCurrentBannerFileName].ToString().Trim().Length == 0 || !currentArtworkExists) && seriesArtwork.SeriesWideBanners.Count > 0)
                {
                    // our list of banners are already sorted by local language and rating so choose first one
                    series[DBOnlineSeries.cCurrentBannerFileName] = seriesArtwork.SeriesWideBanners[0].FileName;
                }
                #endregion

                #region Series Posters
                foreach (PosterSeries posterSeries in seriesArtwork.SeriesPosters)
                {
                    if (!series[DBOnlineSeries.cPosterFileNames].ToString().Contains(posterSeries.FileName))
                    {
                        mDataUpdated = true;
                        MPTVSeriesLog.Write("New series poster found for \"" + series.ToString() + "\" : " + posterSeries.OnlinePath, MPTVSeriesLog.LogLevel.Debug);
                        if (series[DBOnlineSeries.cPosterFileNames].ToString().Trim().Length == 0)
                        {
                            series[DBOnlineSeries.cPosterFileNames] += posterSeries.FileName;
                        }
                        else
                        {
                            series[DBOnlineSeries.cPosterFileNames] += "|" + posterSeries.FileName;
                        }
                    }                        
                }

                // Check if current set series poster exists
                currentArtwork = Helper.PathCombine(Settings.GetPath(Settings.Path.banners), series[DBOnlineSeries.cCurrentPosterFileName].ToString());
                currentArtworkExists = File.Exists(currentArtwork);

                // if the current artwork exists, but not in the list of available artwork then add it
                if (currentArtworkExists && !series.PosterList.Exists(b => b.Equals(currentArtwork)))
                {
                    var imageList = new List<string>(series.PosterList);
                    imageList.Add(currentArtwork);
                    series.PosterList = imageList;
                    imageList = null;
                }

                // Don't override user selection of poster
                if ((series[DBOnlineSeries.cCurrentPosterFileName].ToString().Trim().Length == 0 || !currentArtworkExists) && seriesArtwork.SeriesPosters.Count > 0)
                {
                    series[DBOnlineSeries.cCurrentPosterFileName] = seriesArtwork.SeriesPosters[0].FileName;
                }
                #endregion

                // commit series record
                series.Commit();

                #region Season Posters
                // update the season artwork for series
                List<DBSeason> localSeasons = DBSeason.Get(new SQLCondition(new DBSeason(), DBSeason.cSeriesID, series[DBSeries.cID], SQLConditionType.Equal), false);

                foreach (DBSeason season in localSeasons)
                {
                    // get season posters from map for the current season
                    List<PosterSeason> seasonPosters = new List<PosterSeason>(seriesArtwork.SeasonPosters);
                    seasonPosters.RemoveAll(s => s.SeasonIndex != season[DBSeason.cIndex]);

                    // add to existing range of season posters in database
                    foreach (PosterSeason bannerSeason in seasonPosters)
                    {
                        if (!season[DBSeason.cBannerFileNames].ToString().Contains(bannerSeason.FileName))
                        {
                            mDataUpdated = true;
                            MPTVSeriesLog.Write("New season poster found for \"" + series.ToString() + "\" Season " + season[DBSeason.cIndex] + ": " + bannerSeason.OnlinePath, MPTVSeriesLog.LogLevel.Debug);
                            if (season[DBSeason.cBannerFileNames].ToString().Length == 0)
                            {
                                season[DBSeason.cBannerFileNames] += bannerSeason.FileName;
                            }
                            else
                            {
                                season[DBSeason.cBannerFileNames] += "|" + bannerSeason.FileName;
                            }
                        }
                    }

                    // Check if current set season poster exists
                    currentArtwork = Helper.PathCombine(Settings.GetPath(Settings.Path.banners), season[DBSeason.cCurrentBannerFileName].ToString());
                    currentArtworkExists = File.Exists(currentArtwork);

                    // if the current artwork exists, but not in the list of available artwork then add it
                    if (currentArtworkExists && !season.BannerList.Exists(b => b.Equals(currentArtwork)))
                    {
                        var imageList = new List<string>(season.BannerList);
                        imageList.Add(currentArtwork);
                        season.BannerList = imageList;
                        imageList = null;
                    }

                    // Don't override user selection of season poster
                    if ((season[DBSeason.cCurrentBannerFileName].ToString().Trim().Length == 0 || !currentArtworkExists) && seasonPosters.Count > 0)
                    {
                        season[DBSeason.cCurrentBannerFileName] = seasonPosters[0].FileName;
                    }

                    // commit season record
                    season.Commit();
                }
                #endregion
            }
            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateBanners, seriesList.Count));
        }

        /// <summary>
        /// Removes any episodes from the online_episodes table that no longer exists online
        /// </summary>
        private void CleanEpisodes()
        {
            MPTVSeriesLog.Write(bigLogMessage("Cleaning Online Episode References"));

            int i = 0;
            foreach (var series in mOnlineEpisodes.Keys)
            {
                var seriesObj = Helper.getCorrespondingSeries(int.Parse(series));
                string seriesName = seriesObj == null ? series : seriesObj.ToString();

                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.CleanupEpisodes, seriesName, ++i, mOnlineEpisodes.Keys.Count));

                // get the current online episodes in database
                var episodes = DBEpisode.Get(int.Parse(series), false);
                if (episodes == null) continue;

                bool cleanEpisodeZero = DBOption.GetOptions(DBOption.cCleanOnlineEpisodeZero);

                var epsRemovedInSeason = new Dictionary<int, int>();
                foreach (var episode in episodes)
                {
                    var expectedEpisodes = mOnlineEpisodes[series];
                    
                    int episodeIdx = episode[DBOnlineEpisode.cEpisodeIndex];
                    int seasonIdx = episode[DBOnlineEpisode.cSeasonIndex];

                    // check if episode is in the expected list of episodes
                    // also check if episode is valid i.e. Idx > 0
                    if (!expectedEpisodes.Any(e => (e[DBOnlineEpisode.cEpisodeIndex] == episodeIdx &&
                                                   e[DBOnlineEpisode.cSeasonIndex] == seasonIdx)) ||
                                                   (cleanEpisodeZero && episodeIdx == 0))
                    {
                        string message = string.Format("{0} - Removing episode {1}x{2}, episode no longer exists online or is invalid", seriesName, seasonIdx, episodeIdx);
                        mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.CleanupEpisodes, message, i, mOnlineEpisodes.Keys.Count));

                        // Delete the online reference. The local file's record is only deleted when the file itself is
                        // gone: deleting it for a file still on disk (e.g. a special or a season TheTVDB numbers
                        // differently) made the next import see that file as "new" again, add it, and this step
                        // delete it again - the same files came back in every import's list of new files.
                        string localFile = episode[DBEpisode.cFilename];
                        if (string.IsNullOrEmpty(localFile) || !File.Exists(localFile))
                            episode.DeleteLocalEpisode();
                        episode.DeleteOnlineEpisode();

                        // keep a counter of episodes we remove per season 
                        // if amount removed equals total eps in season then we can remove season
                        int season = episode[DBOnlineEpisode.cSeasonIndex];
                        if (epsRemovedInSeason.ContainsKey(season))
                        {
                            epsRemovedInSeason[season] = epsRemovedInSeason[season] + 1;
                        }
                        else
                        {
                            epsRemovedInSeason.Add(season, 1);
                        }

                        // check if we should clear the season as well
                        int episodesInSeason = episodes.Where(e => e[DBOnlineEpisode.cSeasonIndex] == season).Count();
                        if (episodesInSeason == epsRemovedInSeason[season])
                        {
                            message = string.Format("{0} - Removing season {1}, season no longer has any associated episodes", seriesName, seasonIdx);
                            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.CleanupEpisodes, message, i, mOnlineEpisodes.Keys.Count));

                            var conditions = new SQLCondition();
                            conditions.Add(new DBSeason(), DBSeason.cSeriesID, series, SQLConditionType.Equal);
                            conditions.Add(new DBSeason(), DBSeason.cIndex, episode[DBOnlineEpisode.cSeasonIndex], SQLConditionType.Equal);
                            DBSeason.Clear(conditions);
                        }
                    }
                }
            }
            mWorker.ReportProgress(100, new ParsingProgress(ParsingAction.CleanupEpisodes, mOnlineEpisodes.Keys.Count));
        }

        /// <summary>
        /// Check Banners & Posters for existence and corruption. Remove non-existent or corrupt banners from database (to re-download on UpdateBanners)
        /// </summary>
        // series whose artwork lost entries during CheckBanners, so the "download missing artwork" step fetches
        // them again even though they still have some pictures left
        private readonly List<string> mSeriesNeedingArtwork = new List<string>();

        private void CheckBanners(bool checkEveryFile)
        {
            MPTVSeriesLog.Write(bigLogMessage("Verifying ArtWork"));
            var condition = new SQLCondition();

            // all series that have an onlineID ( > 0)
            condition.Add(new DBOnlineSeries(), DBSeries.cID, 0, SQLConditionType.GreaterThan);
            condition.Add(new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal);
            condition.Add(new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal);

            var seriesList = DBSeries.Get(condition, false, false);
            if (seriesList.Count <= 0)
            {
                MPTVSeriesLog.Write("No Artwork found to verify.");
                return;
            }

            string bannersRoot = Settings.GetPath(Settings.Path.banners);
            Func<string, bool> fileExists = File.Exists;
            Func<string, bool> isGoodImage = path =>
            {
                if (!File.Exists(path)) return false;
                using (var image = ImageAllocator.LoadImageFastFromFile(path))
                    return image != null;
            };
            Action<string> log = s => MPTVSeriesLog.Write(s);

            int i = 0, seriesFixed = 0, missing = 0, reselected = 0;
            foreach (var series in seriesList)
            {
                if (mWorker != null && mWorker.CancellationPending) return;
                MPTVSeriesLog.Write(string.Format("progress received: VerifyArtwork [{0}/{1}] {2}", ++i, seriesList.Count, series.ToString()), MPTVSeriesLog.LogLevel.Debug);

                string seriesId = series[DBOnlineSeries.cID];
                bool seriesChanged = false, lostPictures = false;

                var banners = ArtworkRepair.Repair(series.BannerList, series[DBOnlineSeries.cCurrentBannerFileName], seriesId, bannersRoot, checkEveryFile, fileExists, isGoodImage, log);
                if (banners.Changed)
                {
                    series.BannerList = banners.Keep;
                    series[DBOnlineSeries.cCurrentBannerFileName] = banners.Selected;
                    seriesChanged = true;
                }

                var posters = ArtworkRepair.Repair(series.PosterList, series[DBOnlineSeries.cCurrentPosterFileName], seriesId, bannersRoot, checkEveryFile, fileExists, isGoodImage, log);
                if (posters.Changed)
                {
                    series.PosterList = posters.Keep;
                    series[DBOnlineSeries.cCurrentPosterFileName] = posters.Selected;
                    seriesChanged = true;
                }

                int removed = banners.RemovedMissing + posters.RemovedMissing;
                missing += banners.RemovedMissing + posters.RemovedMissing;
                if (banners.SelectionChanged) reselected++;
                if (posters.SelectionChanged) reselected++;
                if (removed > 0) lostPictures = true;
                if (seriesChanged) series.Commit();

                // season posters
                foreach (DBSeason season in DBSeason.Get(int.Parse(seriesId)))
                {
                    var sp = ArtworkRepair.Repair(season.BannerList, season[DBSeason.cCurrentBannerFileName], seriesId, bannersRoot, checkEveryFile, fileExists, isGoodImage, log);
                    if (!sp.Changed) continue;

                    season.BannerList = sp.Keep;
                    season[DBSeason.cCurrentBannerFileName] = sp.Selected;
                    season.Commit();

                    missing += sp.RemovedMissing;
                    if (sp.SelectionChanged) reselected++;
                    if (sp.RemovedMissing > 0) lostPictures = true;
                    seriesChanged = true;
                }

                if (lostPictures && !mSeriesNeedingArtwork.Contains(seriesId))
                    mSeriesNeedingArtwork.Add(seriesId);
                if (seriesChanged) seriesFixed++;
            }

            MPTVSeriesLog.Write(string.Format("Artwork check: {0} series fixed - {1} missing or damaged pictures removed, {2} selected pictures replaced",
                                              seriesFixed, missing, reselected));
        }

        /// <summary>
        /// Gets Actors From Online
        /// </summary>
        /// <param name="updatesOnly">set to true when processing online updates</param>
        /// <param name="updatedSeries">list of series that have had updates online, set to null if updatesOnly is false</param>
        private void UpdateActors(bool updatesOnly, List<DBValue> updatedSeries)
        {
            // exit if we dont want to automatically download fanart
            if (!DBOption.GetOptions(DBOption.cAutoDownloadActors))
                return;

            // get all series in database
            SQLCondition condition = new SQLCondition();
            condition.Add(new DBSeries(), DBSeries.cID, 0, SQLConditionType.GreaterThan);
            condition.Add(new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal);
            condition.Add(new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal);
            List<DBSeries> seriesList = DBSeries.Get(condition, false, false);

            // process updates on series we have locally
            // remove series not of interest
            if (updatesOnly)
            {
                MPTVSeriesLog.Write(bigLogMessage("Processing Actors from Online Updates"));
                seriesList.RemoveAll(s => !updatedSeries.Contains(s[DBSeries.cID]));
            }

            // updating fanart for all series
            if (!updatesOnly)
            {
                MPTVSeriesLog.Write(bigLogMessage("Downloading new and missing Actors for Series"));

                List<int> seriesids = DBActor.GetSeriesWithActors();
                seriesList.RemoveAll(s => seriesids.Contains(s[DBSeries.cID]));
            }

            int nIndex = 0;
            foreach (DBSeries series in seriesList)
            {
                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.GetNewActors, series.ToString(), ++nIndex, seriesList.Count, series, null));

                try
                {
                    // get available banners and add to database
                    Online_Parsing_Classes.GetActors ga = new Online_Parsing_Classes.GetActors(series[DBSeries.cID]);
                    foreach (DBActor a in ga.Actors)
                    {
                        a.Commit();

                        string remoteThumb = a.ImageRemotePath;
                        if (string.IsNullOrEmpty(remoteThumb)) continue;

                        string localThumb = a.Image;
                        if (string.IsNullOrEmpty(localThumb)) continue;

                        if (Helper.DownloadFile(remoteThumb, localThumb))
                        {
                            // notify that thumbnail image has been downloaded
                            a.ThumbnailImage = localThumb;
                            a.NotifyPropertyChanged("ThumbnailImage");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write("Failed to update Actor: " + ex.Message);
                }
            }
            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.GetNewActors, seriesList.Count));
        }

        /// <summary>
        /// Gets Fanart From Online
        /// </summary>
        /// <param name="updatesOnly">set to true when processing online updates</param>
        /// <param name="updatedSeries">list of series that have had updates online, set to null if updatesOnly is false</param>
        private void UpdateFanart(bool updatesOnly, List<DBValue> updatedSeries)
        {
            // exit if we dont want to automatically download fanart
            if (!DBOption.GetOptions(DBOption.cAutoDownloadFanart))
                return;

            // get all series in database
            SQLCondition condition = new SQLCondition();
            condition.Add(new DBSeries(), DBSeries.cID, 0, SQLConditionType.GreaterThan);
            condition.Add(new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal);
            condition.Add(new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal);
            List<DBSeries> seriesList = DBSeries.Get(condition, false, false);
            
            // process updates on series we have locally
            // remove series not of interest
            if (updatesOnly)
            {
                MPTVSeriesLog.Write(bigLogMessage("Processing Fanart from Online Updates"));
                seriesList.RemoveAll(s => !updatedSeries.Contains(s[DBSeries.cID])); 
            }

            // updating fanart for all series
            if (!updatesOnly)
            {
                MPTVSeriesLog.Write(bigLogMessage("Downloading new and missing Fanart for Series"));

                // just process the ones with missing fanart
                if (!DBOption.GetOptions(DBOption.cAutoUpdateAllFanart))
                {
                    List<int> seriesids = DBFanart.GetSeriesWithFanart();
                    seriesList.RemoveAll(s => seriesids.Contains(s[DBSeries.cID]));
                }
            }

            int nIndex = 0;
            int maxConsecutiveDownloadErrors;
            var consecutiveDownloadErrors = 0;
            if (!int.TryParse(DBOption.GetOptions(DBOption.cMaxConsecutiveDownloadErrors).ToString(), out maxConsecutiveDownloadErrors))
            {
                maxConsecutiveDownloadErrors = 3;
            }

            foreach (DBSeries series in seriesList)
            {
                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateFanart, series.ToString(), ++nIndex, seriesList.Count, series, null));

                try
                {
                    // get available banners and add to database
                    GetFanart gf = new GetFanart(series[DBSeries.cID]);
                    foreach (DBFanart f in gf.Fanart)
                    {
                        f.Commit();
                    }

                    // get list of fanarts to auto download
                    DBFanart lDbFanart = new DBFanart();
                    List<DBFanart> lFanarts = lDbFanart.FanartsToDownload(series[DBSeries.cID]);

                    // download fanart
                    foreach (DBFanart lFanart in lFanarts)
                    {
                        if (consecutiveDownloadErrors >= maxConsecutiveDownloadErrors)
                        {
                            MPTVSeriesLog.Write("Too many consecutive download errors. Aborting.");
                            return;
                        }
                        
                        // lets put fanart in expected location fanart\original\<seriesID>*.jpg
                        string lLocalFilename = Fanart.GetLocalPath( lFanart );

                        string result = Online_Parsing_Classes.OnlineAPI.DownloadBanner( lFanart[DBFanart.cBannerPath], Settings.Path.fanart, lLocalFilename );
                        if (result != null)
                        {
                            // commit to database
                            lFanart[DBFanart.cLocalPath] = lLocalFilename;
                            lFanart.Commit();

                            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateFanart, string.Format("{0} - {1}", series.ToString(), Helper.PathCombine(Settings.GetPath(Settings.Path.fanart), lLocalFilename) ), nIndex, seriesList.Count, series, result));
                            consecutiveDownloadErrors = 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write("Failed to update Fanart. " + ex.Message);
                    consecutiveDownloadErrors++;
                }
            }
            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateFanart, seriesList.Count));
        }

        void UpdateTraktCommunityRatings(BackgroundWorker tTraktCommunityRatings, List<DBValue> seriesIds, List<DBValue> episodeIds)
        {
            // Community ratings are public Trakt data fetched directly (see TraktRatingsClient), so they no
            // longer need the Trakt plugin to be installed - whether this step runs at all is controlled by the
            // "Automatically update community ratings and votes from trakt.tv" option.

            MPTVSeriesLog.Write(bigLogMessage("Updating Trakt Community Ratings"), MPTVSeriesLog.LogLevel.Normal);

            var series = new List<DBSeries>();
            bool forceUpdate = false;

            if (seriesIds == null && episodeIds == null)
            {
                // get all series
                var condition = new SQLCondition();
                condition.Add(new DBSeries(), DBSeries.cID, 0, SQLConditionType.GreaterThan);
                condition.Add(new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal);
                condition.Add(new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal);
                series = DBSeries.Get(condition, false, false);
            }
            else
            {
                forceUpdate = true;

                // if there are no series ids, then we must be updating a season / episode
                if (seriesIds != null && seriesIds.Count > 0)
                {
                    // update only select series - typically only a single series
                    seriesIds.ForEach(id => series.Add(DBSeries.Get(id, false)));
                }
                else if (episodeIds != null & episodeIds.Count > 0)
                {
                    // get the series being updated from from the first episode id
                    var episodes = DBEpisode.Get("SELECT * FROM online_episodes WHERE EpisodeID = " + episodeIds.First());
                    series.Add(DBSeries.Get(episodes.First()[DBOnlineEpisode.cSeriesID], false));
                }
            }

            tTraktCommunityRatings.DoWork += new DoWorkEventHandler(asyncTraktCommunityRatings);
            tTraktCommunityRatings.RunWorkerCompleted += new RunWorkerCompletedEventHandler(asyncTraktCommunityRatingsCompleted);

            // parse a tuple into the worker method where T1=The list of series and T2=Force Update
            // Force update if only updating selected series
            tTraktCommunityRatings.RunWorkerAsync(Tuple.Create(series, forceUpdate));
        }

        void asyncTraktCommunityRatingsCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            MPTVSeriesLog.Write(bigLogMessage("Trakt Community Ratings Updated in Database"), MPTVSeriesLog.LogLevel.Debug);
        }

        void asyncTraktCommunityRatings(object sender, DoWorkEventArgs e)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            Thread.CurrentThread.Name = "Ratings";

            var tupleParams = e.Argument as Tuple<List<DBSeries>, bool>;
            List<DBSeries> seriesList = tupleParams.Item1;
            bool forceUpdate = tupleParams.Item2;

            // get list of updated shows from trakt
            DateTime dteLastUpdated = DateTime.MinValue;
            string strLastUpdated = DBOption.GetOptions(DBOption.cTraktLastDateUpdated);
            HashSet<string> recentSeries = new HashSet<string>();
            Dictionary<string, string> updatedShows = null; // Trakt ID -> when Trakt last updated it

            // Every Trakt request here is made directly by TraktRatingsClient rather than through TraktAPI.dll,
            // which silently failed to read Trakt's current replies - see that class for why.

            #region Recently Updated Shows
            if (!string.IsNullOrEmpty(strLastUpdated) && !forceUpdate)
            {
                if (DateTime.TryParse(strLastUpdated, out dteLastUpdated))
                {
                    updatedShows = TraktRatingsClient.GetShowsUpdatedSince(dteLastUpdated.ToUniversalTime());
                }

                #region Forced Updates
                // if there has been some new episodes recently added to the database (since last trakt update)
                // force an update on the series such that the ratings are up to date
                var recentlyAddedEpisodes = DBEpisode.GetMostRecent(dteLastUpdated);
                recentlyAddedEpisodes.GroupBy(ep => ep[DBOnlineEpisode.cSeriesID]).ToList().ForEach(s => recentSeries.Add(s.Key));
                #endregion
            }
            #endregion

            int nIndex = 0;
            int notOnTrakt = 0, ratingsUpdated = 0, lookupFailures = 0;
            foreach (var series in seriesList)
            {
                if (mWorker.CancellationPending) break;

                // Update Progress
                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateCommunityRatings, series[DBOnlineSeries.cPrettyName], ++nIndex, seriesList.Count, series, null));

                // get the trakt id for the series
                // if not found do a lookup and store it for next time
                string tvdbId = series[DBSeries.cID];
                string traktid = series[DBOnlineSeries.cTraktID];
                string seriesName = series[DBOnlineSeries.cPrettyName];
                string failureReason;

                // a series that has never had a rating saved, or whose Trakt ID is only being found now, must always
                // be fetched - see the update check below
                bool neverRated = string.IsNullOrEmpty(traktid) || string.IsNullOrEmpty(series[DBOnlineSeries.cRatingCount]);

                #region Trakt ID Lookup
                if (string.IsNullOrEmpty(traktid))
                {
                    if (string.IsNullOrEmpty(tvdbId))
                        continue;

                    MPTVSeriesLog.Write(string.Format("Searching for series Trakt ID, Title = '{0}', TVDb ID = '{1}'", seriesName, tvdbId), MPTVSeriesLog.LogLevel.Debug);

                    traktid = TraktRatingsClient.FindTraktIdByTvdbId(tvdbId, out failureReason);
                    if (string.IsNullOrEmpty(traktid))
                    {
                        if (failureReason == "not on Trakt") notOnTrakt++; else lookupFailures++;
                        MPTVSeriesLog.Write("Skipping community rating, no Trakt ID ({2}). Title = '{0}', TVDb ID = '{1}'", seriesName, tvdbId, failureReason);
                        continue;
                    }

                    // store the id for later
                    mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateCommunityRatings, string.Format("{0} - Trakt Lookup Complete, ID = {1}", seriesName, traktid), nIndex, seriesList.Count, series, null));

                    series[DBOnlineSeries.cTraktID] = traktid;
                    series.Commit();
                }
                #endregion

                #region Series Community Ratings

                #region Update Check
                // only update ratings for series if it is a series that has recently been updated on trakt.
                // we don't want to update every series and underlying episode every sync!!!
                // skip update check on a series if it was recently added
                // The "last updated" date is saved after every run, including runs where every lookup failed (as
                // happened for everyone while TraktAPI.dll couldn't read Trakt's replies). Without the neverRated
                // exception, those series would then be skipped until Trakt happened to change them.
                if (updatedShows != null && !recentSeries.Contains(tvdbId) && !neverRated)
                {
                    string updatedAt;
                    if (!updatedShows.TryGetValue(traktid, out updatedAt))
                    {
                        MPTVSeriesLog.Write(string.Format("Skipping community ratings update for series, the series has not been found in the update list. Title = '{0}', TVDb ID = '{1}', Trakt ID = '{2}'", seriesName, tvdbId, traktid), MPTVSeriesLog.LogLevel.Debug);
                        continue;
                    }

                    // check that the show updated_at is newer than last time we did an update
                    DateTime dteShowUpdated;
                    if (DateTime.TryParse(updatedAt, out dteShowUpdated) && dteShowUpdated.ToUniversalTime() < dteLastUpdated.ToUniversalTime())
                    {
                        MPTVSeriesLog.Write(string.Format("Skipping community ratings update for series, the series has not been updated recently. Title = '{0}', TVDb ID = '{1}', Trakt ID = '{2}'", seriesName, tvdbId, traktid), MPTVSeriesLog.LogLevel.Debug);
                        continue;
                    }
                }
                #endregion

                // get series ratings from trakt
                MPTVSeriesLog.Write(string.Format("Requesting series ratings from trakt.tv. Title = '{0}', TVDb ID = '{1}', Trakt ID = '{2}'", seriesName, tvdbId, traktid), MPTVSeriesLog.LogLevel.Debug);

                var seriesRatings = TraktRatingsClient.GetShowRating(traktid, out failureReason);
                if (seriesRatings == null)
                {
                    lookupFailures++;
                    MPTVSeriesLog.Write("Failed to get series ratings from trakt.tv ({3}). Title = '{0}', TVDb ID = '{1}', Trakt ID = '{2}'", seriesName, tvdbId, traktid, failureReason);
                    continue;
                }

                // update database with community rating and votes - if it has changed
                if ((int)series[DBOnlineSeries.cRatingCount] != seriesRatings.Votes)
                {
                    string rating = Math.Round(seriesRatings.Value, 2).ToString();

                    mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateCommunityRatings, string.Format("{0} - Rating = {1}, Votes = {2}", seriesName, rating, seriesRatings.Votes), nIndex, seriesList.Count, series, null));

                    series[DBOnlineSeries.cRating] = rating;
                    series[DBOnlineSeries.cRatingCount] = seriesRatings.Votes;
                    series.Commit();
                }
                ratingsUpdated++;
                #endregion

                #region Season and Episode Community Ratings
                // one request gets every season with its episodes, including ratings and season overviews
                // (season overviews aren't provided by theTVDb API)
                MPTVSeriesLog.Write(string.Format("Requesting season information for series from trakt.tv. Title = '{0}', TVDb ID = '{1}', Trakt ID = '{2}'", seriesName, tvdbId, traktid), MPTVSeriesLog.LogLevel.Debug);
                var traktSeasons = TraktRatingsClient.GetSeasons(traktid, out failureReason);
                if (traktSeasons == null)
                {
                    MPTVSeriesLog.Write("Failed to get season information for series from trakt.tv ({3}). Title = '{0}', TVDb ID = '{1}', Trakt ID = '{2}'", seriesName, tvdbId, traktid, failureReason);
                    continue;
                }

                #region Seasons
                // get seasons from local database for current series
                var conditions = new SQLCondition(new DBSeason(), DBSeason.cSeriesID, tvdbId, SQLConditionType.Equal);
                var seasons = DBSeason.Get(conditions, false);
                foreach (var season in seasons)
                {
                    TraktRatingsClient.SeasonInfo traktSeason;
                    if (!traktSeasons.TryGetValue((int)season[DBSeason.cIndex], out traktSeason)) continue;

                    // update database with community rating and votes - if it has changed
                    if ((int)season[DBSeason.cRatingCount] != traktSeason.Votes)
                    {
                        string rating = Math.Round(traktSeason.Rating, 2).ToString();

                        mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateCommunityRatings, string.Format("{0} - Season = {1}, Rating = {2}, Votes = {3}", seriesName, traktSeason.Number, rating, traktSeason.Votes), nIndex, seriesList.Count, series, null));

                        season[DBOnlineEpisode.cRating] = rating;
                        season[DBOnlineEpisode.cRatingCount] = traktSeason.Votes;
                        season.Commit();
                    }

                    // bonus: update the season summary while we are here
                    if (!string.IsNullOrEmpty(traktSeason.Overview) && traktSeason.Overview != (string)season[DBSeason.cSummary])
                    {
                        season[DBSeason.cSummary] = traktSeason.Overview;
                        season.Commit();
                    }
                }
                #endregion

                #region Episodes
                // get episodes from local database for current series
                conditions = new SQLCondition(new DBOnlineEpisode(), DBOnlineEpisode.cSeriesID, tvdbId, SQLConditionType.Equal);
                conditions.AddOrderItem(DBOnlineEpisode.Q(DBOnlineEpisode.cID), SQLCondition.orderType.Ascending);

                var episodes = DBEpisode.Get(conditions, false);
                if (episodes == null) continue;

                foreach (var episode in episodes)
                {
                    TraktRatingsClient.SeasonInfo traktSeason;
                    if (!traktSeasons.TryGetValue((int)episode[DBOnlineEpisode.cSeasonIndex], out traktSeason)) continue;

                    TraktRatingsClient.Rating traktEpisode;
                    int episodeNumber = (int)episode[DBOnlineEpisode.cEpisodeIndex];
                    if (!traktSeason.Episodes.TryGetValue(episodeNumber, out traktEpisode)) continue;

                    // update database with community rating and votes - if it has changed
                    if ((int)episode[DBOnlineEpisode.cRatingCount] != traktEpisode.Votes)
                    {
                        string rating = Math.Round(traktEpisode.Value, 2).ToString();

                        mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateCommunityRatings, string.Format("{0} - Episode = {1}x{2}, Rating = {3}, Votes = {4}", seriesName, traktSeason.Number, episodeNumber, rating, traktEpisode.Votes), nIndex, seriesList.Count, series, null));

                        episode[DBOnlineEpisode.cRating] = rating;
                        episode[DBOnlineEpisode.cRatingCount] = traktEpisode.Votes;
                        episode.Commit();
                    }
                }
                #endregion

                #endregion
            }

            MPTVSeriesLog.Write(string.Format("Trakt community ratings: {0} series rated, {1} not on Trakt, {2} could not be fetched", ratingsUpdated, notOnTrakt, lookupFailures));

            // store last time updated
            if (!forceUpdate)
            {
                DBOption.SetOptions(DBOption.cTraktLastDateUpdated, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            }
            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateCommunityRatings, seriesList.Count));
        }

        public void UpdateUserFavourites()
        {
            int nIndex = 0;

            // favourites belong to the TheTVDB account the subscriber PIN logs in as
            if (Online_Parsing_Classes.V4.TvdbV4Favourites.IsAvailable) {
                MPTVSeriesLog.Write(bigLogMessage("Updating User Favourites"), MPTVSeriesLog.LogLevel.Normal);

                GetUserFavourites userFavourites = new GetUserFavourites();
                if (!userFavourites.Retrieved) {
                    // never clear everyone's favourites just because TheTVDB couldn't be reached
                    MPTVSeriesLog.Write("Could not get your favourites from TheTVDB, leaving Online Favourites as they are");
                    mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateUserFavourites, 0));
                    return;
                }
                MPTVSeriesLog.Write(string.Format("TheTVDB favourites: {0} series", userFavourites.Series.Count));

                SQLCondition condition = new SQLCondition();
                condition.Add(new DBSeries(), DBSeries.cID, 0, SQLConditionType.GreaterThan);
                condition.Add(new DBSeries(), DBSeries.cScanIgnore, 0, SQLConditionType.Equal);
                condition.Add(new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal);
                List<DBSeries> seriesList = DBSeries.Get(condition, false, false);

                foreach (DBSeries series in seriesList) {
                    // Update Progress
                    mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateUserFavourites, series[DBOnlineSeries.cPrettyName], ++nIndex, seriesList.Count, series, null));

                    if (userFavourites.Series.Contains(series[DBOnlineSeries.cID])) {
                        MPTVSeriesLog.Write("Retrieved favourite series: " + Helper.getCorrespondingSeries((int)series[DBOnlineSeries.cID]), MPTVSeriesLog.LogLevel.Debug);
                        series[DBOnlineSeries.cViewTags] = Helper.GetSeriesViewTags(series, true, DBView.cTranslateTokenOnlineFavourite);
                        series.Commit();
                    } else {
                        series[DBOnlineSeries.cViewTags] = Helper.GetSeriesViewTags(series, false, DBView.cTranslateTokenOnlineFavourite);
                        series.Commit();
                    }
                }
                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateUserFavourites, seriesList.Count));
            }            
        }

        public void UpdateEpisodeThumbNails()
        {
            if (DBOption.GetOptions(DBOption.cGetEpisodeSnapshots) == true) 
            {
                MPTVSeriesLog.Write(bigLogMessage("Updating Episode Thumbnails"), MPTVSeriesLog.LogLevel.Normal);

                // Get all online episodes that have an image but not yet downloaded                
                string lQuery;

                if (Settings.isConfig)
                {
                    // Be more thorough in configuration, user may have deleted thumbs locally
                    lQuery = "select * from online_episodes where ThumbURL != '' order by SeriesID asc";
                }
                else
                {
                    lQuery = "select * from online_episodes where ThumbURL != '' and thumbFilename = '' order by SeriesID asc";
                }

                DBSeries lSeries = null;
                var lEpisodes = DBEpisode.Get(lQuery);
                var lEpisodesThumbsForDownload = new List<DBEpisode>();

                #region Check for thumbs to download
                foreach (var episode in lEpisodes)
                {
                    if (lSeries == null || lSeries[DBSeries.cID] != episode[DBEpisode.cSeriesID])
                        lSeries = Helper.getCorrespondingSeries(episode[DBOnlineEpisode.cSeriesID]);

                    if (lSeries == null) continue;

                    string lThumbFilename = episode.BuildEpisodeThumbFilename();
                    string lCompletePath = Helper.PathCombine(Settings.GetPath(Settings.Path.banners), lThumbFilename);

                    // if it doesn't exist download it
                    if (!File.Exists(lCompletePath))
                    {
                        lEpisodesThumbsForDownload.Add(episode);
                    }
                    else
                    {
                        // if we already have the file check db entry
                        if (!episode[DBOnlineEpisode.cEpisodeThumbnailFilename].ToString().Equals(lThumbFilename))
                        {
                            episode[DBOnlineEpisode.cEpisodeThumbnailFilename] = lThumbFilename;
                            episode.Commit();
                        }
                    }
                }
                #endregion

                #region Download Thumbs
                int nIndex = 0;

                foreach (var episode in lEpisodesThumbsForDownload)
                {
                    if (lSeries == null || lSeries[DBSeries.cID] != episode[DBEpisode.cSeriesID])
                        lSeries = Helper.getCorrespondingSeries(episode[DBOnlineEpisode.cSeriesID]);

                    if (lSeries == null) continue;

                    string lThumbFilename = episode.BuildEpisodeThumbFilename();
                    string lCompletePath = Helper.PathCombine(Settings.GetPath(Settings.Path.banners), lThumbFilename);
                    string lUrl = Online_Parsing_Classes.OnlineAPI.ResolveImageURL(episode[DBOnlineEpisode.cEpisodeThumbnailUrl]);

                    MPTVSeriesLog.Write(string.Format("New Episode Image found for \"{0}\": {1}", episode.ToString(), episode[DBOnlineEpisode.cEpisodeThumbnailUrl]), MPTVSeriesLog.LogLevel.Debug);
                    WebClient webClient = new WebClient();
                    webClient.Headers.Add("user-agent", Settings.UserAgent);

                    // .NET 4.0: Use TLS v1.2. Many download sources no longer support the older and now insecure TLS v1.0/1.1 and SSL v3.
                    ServicePointManager.SecurityProtocol = ( SecurityProtocolType )0xc00;

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(lCompletePath));
                        
                        if (!lUrl.Contains(".jpg"))
                        {
                            MPTVSeriesLog.Write("Episode thumbnail location is incorrect: " + lUrl, MPTVSeriesLog.LogLevel.Normal);
                            episode[DBOnlineEpisode.cEpisodeThumbnailUrl] = "";
                            episode[DBOnlineEpisode.cEpisodeThumbnailFilename] = "";
                        }
                        else
                        {
                            MPTVSeriesLog.Write("Downloading new image from: " + lUrl, MPTVSeriesLog.LogLevel.Debug);
                            webClient.DownloadFile(lUrl, lCompletePath);

                            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateEpisodeThumbNails, episode.ToString(), ++nIndex, lEpisodesThumbsForDownload.Count, episode, lCompletePath));
                        }
                    }
                    catch (WebException)
                    {
                        MPTVSeriesLog.Write("Episode thumbnail download failed ( " + lUrl + " )");
                        lThumbFilename = "";

                        // try to delete file if it exists on disk. maybe download was cut short. Re-download next time
                        try { File.Delete(lCompletePath); } catch { }
                    }
                    episode[DBOnlineEpisode.cEpisodeThumbnailFilename] = lThumbFilename;
                    episode.Commit();
                }
                #endregion

                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateEpisodeThumbNails, lEpisodesThumbsForDownload.Count));
            }
        }

        void UpdateEpisodeCounts(BackgroundWorker tEpisodeCounts)
        {
            MPTVSeriesLog.Write(bigLogMessage("Updating Episode Counts"));

            tEpisodeCounts.WorkerReportsProgress = true;

            SQLCondition condition = new SQLCondition();
            condition.Add(new DBSeries(), DBSeries.cID, 0, SQLConditionType.GreaterThan);            
            condition.Add(new DBSeries(), DBSeries.cDuplicateLocalName, 0, SQLConditionType.Equal);
            List<DBSeries> series = DBSeries.Get(condition, false, false);            

            if (series.Count > 0)
            {
                tEpisodeCounts.DoWork += new DoWorkEventHandler(asyncEpisodeCounts);
                tEpisodeCounts.RunWorkerCompleted += new RunWorkerCompletedEventHandler(asyncEpisodeCountsCompleted);
                tEpisodeCounts.ProgressChanged += new ProgressChangedEventHandler((s, e) =>
                {
                    if (mWorker.IsBusy)
                    {
                        object[] userState = e.UserState as object[];
                        mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateEpisodeCounts, (userState[0] as DBSeries).ToString(), (int)userState[1], series.Count));
                    }
                });
                tEpisodeCounts.RunWorkerAsync(series);
            }
        }

        void asyncEpisodeCountsCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (mWorker.IsBusy)
                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.UpdateEpisodeCounts, (int)e.Result));
            
            MPTVSeriesLog.Write("Update of Episode Counts complete", MPTVSeriesLog.LogLevel.Debug);
        }

        void asyncEpisodeCounts(object sender, DoWorkEventArgs e)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            List<DBSeries> allSeries = (List<DBSeries>)e.Argument;
            BackgroundWorker worker = sender as BackgroundWorker;
            int nIndex = 1;
            var episodesForCount = DBSeries.GetEpisodesForCount();
            foreach (DBSeries series in allSeries)
            {
                worker.ReportProgress(0, new object[] { series, nIndex++ });
                DBSeries.UpdateEpisodeCounts(series, episodesForCount);
            }
            e.Result = allSeries.Count;
        }

        void MediaInfoParse(BackgroundWorker tMediaInfo)
        {
            tMediaInfo.WorkerReportsProgress = true;

            SQLCondition cond = new SQLCondition();
            cond.Add(new DBEpisode(), DBEpisode.cFilename, "", SQLConditionType.NotEqual);
            cond.Add(new DBEpisode(), DBEpisode.cVideoWidth, "0", SQLConditionType.Equal);

            // Playtime decrements by one every failed attempt(0,-1,-2,..,-5), dont attempt future scans if done more than Maximum attempts
            cond.beginGroup();
            cond.Add(new DBEpisode(), DBEpisode.cLocalPlaytime, (DBEpisode.MAX_MEDIAINFO_RETRIES*-1), SQLConditionType.GreaterThan);
            cond.nextIsOr = true;
            cond.AddCustom( DBEpisode.cTableName + "." + DBEpisode.cLocalPlaytime + " IS NULL");
            cond.endGroup();

            List<DBEpisode> episodes = new List<DBEpisode>();
            // get all the episodes
            episodes = DBEpisode.Get(cond, false);
            
            if (episodes.Count > 0)
            {                
                tMediaInfo.DoWork += new DoWorkEventHandler(asyncReadResolutions);
                tMediaInfo.RunWorkerCompleted += new RunWorkerCompletedEventHandler(asyncReadResolutionsCompleted);
                tMediaInfo.ProgressChanged += new ProgressChangedEventHandler((s, e) =>
                {
                    if (mWorker.IsBusy) { // cannot report progress of finished worker - mepo dies
                        object[] userState = e.UserState as object[];
                        mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.MediaInfo, (userState[0] as DBEpisode)[DBEpisode.cFilenameWOPath], (int)userState[1], episodes.Count));
                    }
                });
                tMediaInfo.RunWorkerAsync(episodes);

            }
            else 
                MPTVSeriesLog.Write("All episodes already contain MediaInfo");
        }

        void asyncReadResolutionsCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (mWorker.IsBusy) { // cannot report progress of finished worker - mepo dies
                mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.MediaInfo, (int)e.Result));
            }
            MPTVSeriesLog.Write("Update of MediaInfo complete (processed " + e.Result.ToString() + " files)", MPTVSeriesLog.LogLevel.Debug);
        }

        void asyncReadResolutions(object sender, DoWorkEventArgs e)
        {
            System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Lowest;
            List<DBEpisode> episodes = (List<DBEpisode>)e.Argument;
            BackgroundWorker w = sender as BackgroundWorker;
            int nIndex = 1;
            foreach (DBEpisode ep in episodes)
            {
                w.ReportProgress(0, new object[] { ep, nIndex++ });
                ep.ReadMediaInfo();
            }
            e.Result = episodes.Count;
        }

        #endregion

        #region SeriesHelpers
        /// <summary>
        /// If the option is on and a show-level metadata file next to this series' episodes carries a TheTVDB
        /// id, fetches that series directly. Returns null when there's nothing usable, so the caller falls back
        /// to the normal name search.
        /// </summary>
        private static DBOnlineSeries TryGetSeriesFromLocalIdFile(DBSeries series, string seriesName)
        {
            // a name given to a same-named show to keep it separate ("Name [tvdb 12345]") already carries its id
            var separated = System.Text.RegularExpressions.Regex.Match(seriesName ?? string.Empty, @"\[tvdb (\d+)\]$");
            if (separated.Success)
            {
                WindowPlugins.GUITVSeries.GetSeries byId = WindowPlugins.GUITVSeries.GetSeries.FromKnownId(int.Parse(separated.Groups[1].Value), seriesName.Substring(0, separated.Index).Trim());
                if (byId != null && byId.PerfectMatch != null) return byId.PerfectMatch;
            }

            if (!DBOption.GetOptions(DBOption.cLookForLocalIdFiles)) return null;

            try
            {
                SQLCondition cond = new SQLCondition();
                cond.Add(new DBEpisode(), DBEpisode.cSeriesID, series[DBSeries.cID], SQLConditionType.Equal);
                List<string> files = DBEpisode.Get(cond, false)
                                              .Select(e => e[DBEpisode.cFilename].ToString())
                                              .Where(f => !string.IsNullOrEmpty(f))
                                              .ToList();

                string idSource;
                int localId = LocalSeriesIdFinder.FindSeriesId(files, out idSource);
                if (localId <= 0) return null;

                // fully qualified: inside OnlineParsing, a bare "GetSeries" means the GetSeries() method
                WindowPlugins.GUITVSeries.GetSeries known = WindowPlugins.GUITVSeries.GetSeries.FromKnownId(localId, seriesName);
                if (known == null) return null;

                MPTVSeriesLog.Write(string.Format("\"{0}\" identified as series {1} from local file '{2}' (name search skipped)", seriesName, localId, idSource));
                return known.PerfectMatch;
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Local series id lookup failed for \"" + seriesName + "\", using name search instead: " + ex.Message);
                return null;
            }
        }

        public static DBOnlineSeries SearchForSeries( string aSeriesName, int aTvdbID, bool aIsNoExactMatch, IFeedback aFeedback )
        {
            string selLang = string.Empty;
            // a name given to a same-named show to keep it separate ("Name [tvdb 12345]") is searched without that suffix
            string nameToSearch = System.Text.RegularExpressions.Regex.Replace(aSeriesName ?? string.Empty, @"\s*\[tvdb \d+\]$", "");

            while (true) 
            {
                // query online db for possible matches
                var getSeriesParser = new GetSeries( nameToSearch, aTvdbID );

                // try to find an exact match in our results, if found, return               
                if (getSeriesParser.PerfectMatch != null && !aIsNoExactMatch)
                {
                    MPTVSeriesLog.Write(string.Format("\"{0}\" was automatically matched to \"{1}\" (SeriesID: {2}), there were a total of {3} matches returned from the Online Database", nameToSearch, getSeriesParser.PerfectMatch.ToString(), getSeriesParser.PerfectMatch[DBOnlineSeries.cID], getSeriesParser.Results.Count));
                    return getSeriesParser.PerfectMatch;
                }

                MPTVSeriesLog.Write(string.Format("Found {0} possible matches for \"{1}\"", getSeriesParser.Results.Count, nameToSearch));

                // User has four choices:
                // 1) Pick a series from the list
                // 2) Simply skip
                // 3) Skip and never ask for this series again
                // 4) Manually Search

                List<CItem> Choices = new List<CItem>();
                Dictionary<int, DBOnlineSeries> uniqueSeriesIds = new Dictionary<int, DBOnlineSeries>();
                foreach (DBOnlineSeries onlineSeries in getSeriesParser.Results) // make them unique (each seriesID) - if possible in users lang
                {
                    // Other language for the Series?
                    if (DBOption.GetOptions(DBOption.cOverrideLanguage))
                    {
                        //Get the prefered language for the Series.
                        selLang = Online_Parsing_Classes.OnlineAPI.GetLanguageOverride(onlineSeries[DBOnlineSeries.cID]);
                    }
                    else
                    {
                        selLang = Online_Parsing_Classes.OnlineAPI.SelLanguageAsString;
                    }

                    if (!uniqueSeriesIds.ContainsKey(onlineSeries[DBOnlineSeries.cID]))
                        uniqueSeriesIds.Add(onlineSeries[DBOnlineSeries.cID], onlineSeries);
                    else if (onlineSeries["language"] == selLang)
                        uniqueSeriesIds[onlineSeries[DBOnlineSeries.cID]] = onlineSeries;
                }
                foreach (KeyValuePair<int, DBOnlineSeries> onlineSeries in uniqueSeriesIds)
                {
                    Choices.Add(new CItem(onlineSeries.Value[DBOnlineSeries.cPrettyName],
                        "SeriesID: " + onlineSeries.Value[DBOnlineSeries.cID] + Environment.NewLine +
                        onlineSeries.Value[DBOnlineSeries.cSummary],
                        onlineSeries.Value));
                }

                if (Choices.Count == 0) 
                    Choices.Add(new CItem(Translation.CFS_No_Match_Manual_Search, String.Empty, null));

                ChooseFromSelectionDescriptor descriptor = new ChooseFromSelectionDescriptor();                
                descriptor.m_sItemToMatchLabel = Translation.CFS_Local_Series;
                descriptor.m_sItemToMatch = nameToSearch;
                descriptor.m_List = Choices;

                bool bKeepTrying = true;
                while (bKeepTrying) {
                    CItem Selected = null;
                    ReturnCode result = aFeedback.ChooseFromSelection(descriptor, out Selected);
                    switch (result) {
                        case ReturnCode.Cancel:
                            MPTVSeriesLog.Write("User cancelled Series Selection");
                            return null;

                        case ReturnCode.Ignore:
                            MPTVSeriesLog.Write("User chose to Ignore \"" + nameToSearch + "\" in the future, setting Hidden=True and ScanIgnore=True");
                            nameToSearch = null;
                            DBSeries series = new DBSeries(aSeriesName);
                            series[DBSeries.cScanIgnore] = 1; // means it will be skipped in the future
                            series[DBSeries.cHidden] = true;
                            series.Commit();
                            return null;

                        case ReturnCode.OK:
                            DBOnlineSeries selectedSeries = Selected.m_Tag as DBOnlineSeries;

                            // Show the Virtual Keyboard to manual enter in name to search                            
                            if (selectedSeries == null && !Settings.isConfig)
                            {
                                GetStringFromUserDescriptor Keyboard = new GetStringFromUserDescriptor();
                                Keyboard.Text = nameToSearch;

                                if (aFeedback.GetStringFromUser(Keyboard, out nameToSearch) == ReturnCode.OK)
                                {
                                    // Search again using manually entered name
                                    bKeepTrying = false;
                                }
                                else
                                {
                                    MPTVSeriesLog.Write("User cancelled Series Selection");
                                    return null;
                                }
                            }
                            else if (nameToSearch != Selected.m_sName || selectedSeries == null)
                            {
                                nameToSearch = Selected.m_sName;
                                bKeepTrying = false;
                            }
                            else
                            {
                                MPTVSeriesLog.Write(string.Format("\"{0}\" was manually matched to \"{1}\" (SeriesID: {2})", nameToSearch, selectedSeries.ToString(), selectedSeries[DBOnlineSeries.cID]));
                                return selectedSeries;
                            }
                            break;

                        case ReturnCode.NotReady: {
                                // plugin's not loaded (yet?) so wait and ask again later
                                Thread.Sleep(2000);
                            }
                            break;
                    }
                }
                if (!bKeepTrying)
                    MPTVSeriesLog.Write("User typed a new Search term: \"" + nameToSearch + "\"", MPTVSeriesLog.LogLevel.Debug);
            }
        }

        private void SetEpisodeOrderForSeries(DBSeries series)
        {
            try 
            {
                // prompt to choose episode order only in GUI.
                // episode orders can be set from import wizard in configuration
                if (string.IsNullOrEmpty(series[DBOnlineSeries.cChosenEpisodeOrder]) && !Settings.isConfig)
                {
                    List<string> episodeOrders = new List<string>(series[DBOnlineSeries.cEpisodeOrders].ToString().Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                    if (episodeOrders.Count > 1 && !DBOption.GetOptions(DBOption.cAutoChooseOrder))
                    {
                        MPTVSeriesLog.Write(string.Format("\"{0}\" supports {1} different ordering options, asking user...", series.ToString(), episodeOrders.Count), MPTVSeriesLog.LogLevel.Debug);
                        // let the user choose
                        string helpText = "Some series expose several ways in which they are ordered, for instance a DVD-release may differ from the original Air schedule." + Environment.NewLine +
                                          "Note that your file numbering must match the option you choose here." + Environment.NewLine +
                                          "Choose the default \"Aired\" option unless you have a specific reason not to!";

                        List<CItem> Choices = new List<CItem>();
                        foreach (string orderOption in episodeOrders)
                            Choices.Add(new CItem(orderOption, helpText, orderOption));
                        Choices.Add(new CItem("Title", helpText, "Title"));

                        ChooseFromSelectionDescriptor descriptor = new ChooseFromSelectionDescriptor();
                        descriptor.m_sTitle = "Multiple ordering Options detected";
                        descriptor.m_sItemToMatchLabel = Translation.ChangeOnlineMatchOrder + ":";
                        descriptor.m_sItemToMatch = series[DBOnlineSeries.cPrettyName];
                        descriptor.m_sListLabel = "Please choose the desired Option:";
                        descriptor.m_List = Choices;
                        descriptor.m_useRadioToSelect = true;
                        descriptor.m_allowAlter = false;
                        descriptor.m_sbtnSkipLabel = String.Empty;
                        descriptor.m_sbtnIgnoreLabel = String.Empty;

                        CItem selectedOrdering = null;
                        ReturnCode result = mFeedback.ChooseFromSelection(descriptor, out selectedOrdering);
                        if (result == ReturnCode.OK)
                        {
                            series[DBOnlineSeries.cChosenEpisodeOrder] = (string)selectedOrdering.m_Tag;
                            // set default episode sort order to match chosen episode order
                            series[DBOnlineSeries.cEpisodeSortOrder] = (string)selectedOrdering.m_Tag == "DVD" ? "DVD" : "Aired";
                            MPTVSeriesLog.Write(string.Format("{0} order option chosen for series \"{1}\"", (string)selectedOrdering.m_Tag, series.ToString()), MPTVSeriesLog.LogLevel.Normal);
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(series[DBOnlineSeries.cEpisodeOrders]))
                        {
                            series[DBOnlineSeries.cChosenEpisodeOrder] = "Aired";
                            series[DBOnlineSeries.cEpisodeSortOrder] = "Aired";
                        }
                        MPTVSeriesLog.Write(string.Format("Aired order option chosen for series \"{0}\"", series.ToString()), MPTVSeriesLog.LogLevel.Normal);
                    }
                }
            } 
            catch (Exception e) 
            {
                MPTVSeriesLog.Write("Error determining episode order for series: {0}, {1}", series.ToString(), e.Message);
            }            
        }

        #endregion

        #region EpisodeHelpers
        private static void matchOnlineToLocalEpisodes(DBSeries series, List<DBEpisode> episodesList, GetEpisodes episodesParser)
        {
            matchOnlineToLocalEpisodes(series, episodesList, episodesParser, null);
        }
        public static void matchOnlineToLocalEpisodes(DBSeries series, List<DBEpisode> episodesList, GetEpisodes episodesParser, string orderOption)
        {
            if (episodesList == null || episodesList.Count == 0)
                return;

            foreach (DBEpisode localEpisode in episodesList) {
                bool bMatchFound = false;
                foreach (DBOnlineEpisode onlineEpisode in episodesParser.Results) {
                    if ((int)localEpisode[DBEpisode.cSeriesID] == (int)onlineEpisode[DBOnlineEpisode.cSeriesID]) {
                        if (matchOnlineToLocalEpisode(series, localEpisode, onlineEpisode, orderOption) == 0)
                        {
                            #region change local episode ids to match online ids
                            bool isSecondPart = false;
                            // check if its a double episode
                            if (!string.IsNullOrEmpty(localEpisode[DBEpisode.cCompositeID2]))
                            {
                                // check if its the second part of a double episode
                                if (localEpisode[DBEpisode.cEpisodeIndex] == localEpisode[DBEpisode.cEpisodeIndex2])
                                    isSecondPart = true;
                            }
                            // change composite id's of local episode
                            if (!string.IsNullOrEmpty(localEpisode[DBEpisode.cFilename]))
                                localEpisode.ChangeIndexes(onlineEpisode[DBOnlineEpisode.cSeasonIndex], onlineEpisode[DBOnlineEpisode.cEpisodeIndex], isSecondPart);
                            #endregion

                            // commit updated data
                            commitOnlineToLocalEpisodeMatch(series, localEpisode, onlineEpisode);
                            bMatchFound = true;
                            break;
                        }
                    }
                }
                // if a local filename failed to find a match, log parsing result
                if (!bMatchFound && !string.IsNullOrEmpty(localEpisode[DBEpisode.cFilename]))
                {
                    string filename = localEpisode[DBEpisode.cFilename];
                    string seriesName = series[DBSeries.cParsedName];
                    string seasonIdx = localEpisode[DBEpisode.cSeasonIndex];
                    string episodeIdx = localEpisode[DBEpisode.cEpisodeIndex];
                    string episodeIdx2 = localEpisode[DBEpisode.cEpisodeIndex2];
                    string episodeId = string.IsNullOrEmpty(episodeIdx2) || episodeIdx2 == "0" ? episodeIdx : string.Concat(episodeIdx, "-", episodeIdx2);
                    MPTVSeriesLog.Write("The following file could not be matched online: ");
                    MPTVSeriesLog.Write("FileName: " + localEpisode[DBEpisode.cFilename]);
                    MPTVSeriesLog.Write("Parsed Series Name: " + seriesName);
                    MPTVSeriesLog.Write("Parsed Season Index: Season " + seasonIdx);
                    MPTVSeriesLog.Write("Parsed Episode Index: Episode " + episodeId);
                    MPTVSeriesLog.Write("Confirm the entry exists in the online database: http://thetvdb.com/");
                }
            }
        }

        private static void commitOnlineToLocalEpisodeMatch(DBSeries series, DBEpisode localEpisode, DBOnlineEpisode onlineEpisode)
        {
            // season update for online data
            DBSeason season = new DBSeason(series[DBSeries.cID], onlineEpisode[DBOnlineEpisode.cSeasonIndex]);
            season[DBSeason.cHasEpisodes] = true;
            season.Commit();

            // update data
            localEpisode[DBOnlineEpisode.cID] = onlineEpisode[DBOnlineEpisode.cID];
            if (localEpisode[DBOnlineEpisode.cEpisodeName].ToString().Length == 0)
                localEpisode[DBOnlineEpisode.cEpisodeName] = onlineEpisode[DBOnlineEpisode.cEpisodeName];

            bool traktCommunityRatings = DBOption.GetOptions(DBOption.cTraktCommunityRatings) && Helper.IsTraktAvailableAndEnabled;
            
            // go over all the fields, (and update only those which haven't been modified by the user - will do that later)
            foreach (String key in onlineEpisode.FieldNames)
            {
                switch (key)
                {
                    case DBOnlineEpisode.cCompositeID:
                    case DBEpisode.cSeriesID:
                    case DBOnlineEpisode.cWatched:
                    case DBOnlineEpisode.cFirstWatchedDate:
                    case DBOnlineEpisode.cLastWatchedDate:
                    case DBOnlineEpisode.cPlayCount:
                    case DBOnlineEpisode.cHidden:
                    case DBOnlineEpisode.cMyRating:
                    case DBOnlineEpisode.cMyRatingAt:
                        // do nothing here, those information are local only
                        break;

                    // dont update community ratings if we get from trakt
                    case DBOnlineEpisode.cRating:
                    case DBOnlineEpisode.cRatingCount:
                        if (traktCommunityRatings)
                            break;
                        goto default;

                    case DBOnlineEpisode.cSeasonIndex:
                    case DBOnlineEpisode.cEpisodeIndex:
                        break; // those must not get overwritten from what they were set to by getEpisodes (because of different order options)

                    case DBOnlineEpisode.cEpisodeThumbnailFilename:
                        // Dont reset as Thumbnail update may not be called in some situations
                        break;

                    default:
                        localEpisode.onlineEpisode.AddColumn(key, new DBField(DBField.cTypeString));
                        localEpisode[key] = onlineEpisode[key];
                        break;
                }
            } 
            localEpisode[DBOnlineEpisode.cOnlineDataImported] = 1;
            MPTVSeriesLog.Write("\"" + localEpisode.ToString() + "\" identified", MPTVSeriesLog.LogLevel.Debug);
            localEpisode.Commit();            
        }


        public static int matchOnlineToLocalEpisode(DBSeries series, DBEpisode localEpisode, DBOnlineEpisode onlineEpisode, string orderingOption)
        {
            // TODO: Enable this for any possible field in local episode (from parsing)
            // just look for a corresponding field in onlineepisode, and depending on the type do a numerical perfect check, or a fuzzy text match
            // also recognize dates
            // also, if  the orderingoptions string should not be passed in here, we should try all fields (perhaps with a certain hardcoded order) and sum up the confidences (or rather errormsg)
            // for instance we could auto detect dvd-ordered local eps, because in aired only the 1x01 will pass, but in dvd both the 1x01 and the title will pass
            // this will also enable fields such as onlineEpisodeIds to be read out from the filename (or possibly in the future nfo files) and be 100% auto matched

            // lastly, this should never overwrite the online-ep ids, instead we should overwrite the local ep ids (but not here) to always the same as the onlineep ids (aired ordering)
            // we can get different ordering on the fly inside the plugin this way

            try
            {
                orderingOption = orderingOption == null ? (string)series[DBOnlineSeries.cChosenEpisodeOrder] : orderingOption;
                switch (orderingOption)
                {
                    case "":
                    case "Aired":
                        int iEpIndex2 = (int)localEpisode[DBEpisode.cEpisodeIndex2];
                        if (iEpIndex2 == 0)
                        {
                            // Don't want to match local episodes with no EpisodeIndex2 with an online episode index of zero
                            iEpIndex2 = -1;
                        }
                        
                        // Check whether we are parsing a Air Date vs Episode/Season Index
                        if (localEpisode[DBEpisode.cEpisodeIndex] < 0 && !string.IsNullOrEmpty(localEpisode[DBOnlineEpisode.cFirstAired]))
                        {
                            string airDate = string.Empty;
                            try { airDate = DateTime.Parse(localEpisode[DBOnlineEpisode.cFirstAired]).ToString("yyyy-MM-dd"); }
                            catch { return int.MaxValue; }

                            if (airDate == onlineEpisode[DBOnlineEpisode.cFirstAired]) return 0;
                            return int.MaxValue;
                        }

                        if (localEpisode[DBEpisode.cEpisodeIndex] >= 0 && (int)localEpisode[DBEpisode.cSeasonIndex] == (int)onlineEpisode[DBOnlineEpisode.cSeasonIndex] &&
                                ((int)localEpisode[DBEpisode.cEpisodeIndex] == (int)onlineEpisode[DBOnlineEpisode.cEpisodeIndex] ||
                                iEpIndex2 == (int)onlineEpisode[DBOnlineEpisode.cEpisodeIndex]))
                        {
                            return 0;
                        }
                        else return int.MaxValue;

                    case "DVD":
                        // use cDVDEpisodeNumber and cDVDSeasonNumber for DVD matching
                        // if the episode field is empty or 0 then fallback to air date fields
                        if ( string.IsNullOrEmpty(onlineEpisode[DBOnlineEpisode.cDVDEpisodeNumber]) || onlineEpisode[DBOnlineEpisode.cDVDEpisodeNumber] == 0 )
                        {
                            return matchOnlineToLocalEpisode( series, localEpisode, onlineEpisode, "Aired" );
                        }

                        System.Globalization.NumberFormatInfo provider = new System.Globalization.NumberFormatInfo();
                        provider.NumberDecimalSeparator = ".";
                                                
                        float onlineSeasonTemp;
                        int onlineSeason = -1;
                        if (float.TryParse(onlineEpisode[DBOnlineEpisode.cDVDSeasonNumber], System.Globalization.NumberStyles.AllowDecimalPoint, provider, out onlineSeasonTemp))
                            onlineSeason = (int)onlineSeasonTemp;

                        int localSeason = 0;
                        int localEp = 0;
                        int localEp2 = 0;
                        if (!string.IsNullOrEmpty(localEpisode[DBEpisode.cOriginalComposite]))
                        {                            
                            Helper.GetEpisodeIndexesFromComposite(localEpisode[DBEpisode.cOriginalComposite2], out localSeason, out localEp2);
                            Helper.GetEpisodeIndexesFromComposite(localEpisode[DBEpisode.cOriginalComposite], out localSeason, out localEp);
                        }
                        else
                        {
                            localSeason = (int)localEpisode[DBEpisode.cSeasonIndex];
                            localEp = (int)localEpisode[DBEpisode.cEpisodeIndex];
                            localEp2 = (int)localEpisode[DBEpisode.cEpisodeIndex2];
                        }

                        float onlineEp = -1;
                        if (onlineSeason != -1 && float.TryParse(onlineEpisode[DBOnlineEpisode.cDVDEpisodeNumber], System.Globalization.NumberStyles.AllowDecimalPoint, provider, out onlineEp))
                        {
                            string localstring;
                            double localcomp;
                            localstring = (localEp.ToString() + "." + localEp2.ToString());
                            localcomp = Convert.ToDouble(localstring, provider);
                            if (localSeason == onlineSeason && 
                               (localcomp == onlineEp || localEp == (int)onlineEp))
                            {
                                return 0;
                            }
                            else
                            {
                                return int.MaxValue;
                            }
                        }
                        break;

                    case "Absolute":
                        System.Globalization.NumberFormatInfo provided = new System.Globalization.NumberFormatInfo();
                        float onlineabs = -1;
                        float onlineabsTemp;
                        if (float.TryParse(onlineEpisode[DBOnlineEpisode.cAbsoluteNumber], System.Globalization.NumberStyles.AllowDecimalPoint, provided, out onlineabsTemp))
                            onlineabs = onlineabsTemp;

                        if (onlineabs != -1)
                        {
                            double localabs = -1;
                            if ((int)localEpisode[DBEpisode.cSeasonIndex] == 0)
                            {
                                /*Now we have to figure out whether we are at ep 100 or more*/
                                localabs = Convert.ToDouble(localEpisode[DBEpisode.cSeasonIndex].ToString() + localEpisode[DBEpisode.cEpisodeIndex].ToString());
                            }
                            else if ((int)localEpisode[DBEpisode.cSeasonIndex] >= 1 && (int)localEpisode[DBEpisode.cEpisodeIndex] < 10)
                            {
                                /* Any episode X0[0-9] should be combined in this manner */
                                localabs = Convert.ToDouble(localEpisode[DBEpisode.cSeasonIndex].ToString() + "0" + localEpisode[DBEpisode.cEpisodeIndex].ToString());
                            }
                            else if ((int)localEpisode[DBEpisode.cSeasonIndex] >= 1 && (int)localEpisode[DBEpisode.cEpisodeIndex] >= 10)
                            {
                                /* All other episodes should fall into this category */
                                localabs = Convert.ToDouble(localEpisode[DBEpisode.cSeasonIndex].ToString() + localEpisode[DBEpisode.cEpisodeIndex].ToString());
                            }

                            float.TryParse(onlineEpisode[DBOnlineEpisode.cAbsoluteNumber], System.Globalization.NumberStyles.AllowDecimalPoint, provided, out onlineabs);
                            if (localabs == onlineabs)
                            {
                                return 0;
                            }
                            else
                            {
                                return int.MaxValue;
                            }
                        }
                        break;
                    case "Title":
                        int fuzzyness = 3;
                        string localTitle = localEpisode[DBEpisode.cRawEpisodeName];
                        string onlineTitle = onlineEpisode[DBOnlineEpisode.cEpisodeName];
                        if (string.IsNullOrEmpty(localTitle) || string.IsNullOrEmpty(onlineTitle)) return int.MaxValue;
                        double maxDistance = Math.Min(localTitle.Length, onlineTitle.Length) * 0.1 + fuzzyness;
                        int isDistance = MediaPortal.Util.Levenshtein.Match(localTitle.ToLower(), onlineTitle.ToLower());
                        if (isDistance > maxDistance) return int.MaxValue; // we dont return
                        else return isDistance;
                }          
            }
            catch (Exception ex)
            {
                // was getting strange results from API where online ep was greater than Int32!
                MPTVSeriesLog.Write("Error: Exception generated whilst matching local episode [{0}x{1}] to online episode [{2}x{3}]: {4}", 
                    localEpisode[DBEpisode.cSeasonIndex], localEpisode[DBEpisode.cEpisodeIndex],
                    onlineEpisode[DBOnlineEpisode.cSeasonIndex], onlineEpisode[DBOnlineEpisode.cEpisodeIndex],
                    ex.Message);                
            }
            return int.MaxValue;
        }
        #endregion

        #region ParseLocal
        /// <summary>
        /// sets:
        ///  - DBEpisode.cImportProcessed to 1
        ///  - DBSeason.cHasLocalFilesTemp to true
        ///  - DBSeries.cHasLocalFilesTemp to true
        /// </summary>
        /// <param name="filenames"></param>
        private static void UpdateStatus(List<string> filenames)
        {
            if (filenames.Count == 0) return;
            SQLCondition cond = new SQLCondition();
            
            // for huge libraries a stringBuilder is much better than adding every filename seperatly to the condition (and for small ones it hardly matters)
            // sqllite expression tree can only be 1000 deep though, to be safe we do 500 at once and no more
            List<SQLCondition> importProcessedConds = new List<SQLCondition>();
            try
            {
                StringBuilder condBuilder = new StringBuilder();
                string field = DBEpisode.Q(DBEpisode.cFilename);
                int count = 0;
                foreach (string file in filenames)
                {
                    if (condBuilder.Length > 0)
                        condBuilder.Append(" or ");
                    condBuilder.Append(field).Append(" = '").Append(file.Replace("'", "''")).Append('\'');
                    if (count++ >= 500)
                    {
                        cond.AddCustom(condBuilder.ToString());
                        importProcessedConds.Add(cond);
                        cond = new SQLCondition();
                        condBuilder.Remove(0, condBuilder.Length);
                        count = 0;
                    }
                }
                if (count > 0)
                {
                    cond.AddCustom(condBuilder.ToString());
                    importProcessedConds.Add(cond);
                }
            }
            catch ( Exception ex)
            {
                MPTVSeriesLog.Write(ex.Message);
                return;
            }

            foreach (SQLCondition condition in importProcessedConds)
            {
                DBEpisode.GlobalSet(DBEpisode.cImportProcessed, 1, condition);                
                DBEpisode.GlobalSet(DBEpisode.cIsAvailable, 1, condition);
            }

            SQLCondition condSeason = new SQLCondition();
            condSeason.AddCustom(" exists( select " + DBEpisode.Q(DBEpisode.cFilename) + " from " + DBEpisode.cTableName
                            + " where " + DBEpisode.cSeriesID + " = " + DBSeason.Q(DBSeason.cSeriesID) + " and "
                            + DBEpisode.cSeasonIndex + " = " + DBSeason.Q(DBSeason.cIndex) + " and " + DBEpisode.Q(DBEpisode.cImportProcessed) + " = 1 "   + ")");
            DBSeason.GlobalSet(DBSeason.cHasLocalFilesTemp, true, condSeason); //takes forever

            SQLCondition condSeries = new SQLCondition();
            condSeries.AddCustom(" exists( select " + DBEpisode.Q(DBEpisode.cFilename) + " from " + DBEpisode.cTableName
                            + " where " + DBEpisode.cSeriesID + " = " + DBOnlineSeries.Q(DBOnlineSeries.cID) +
                            " and " + DBEpisode.Q(DBEpisode.cImportProcessed) + " = 1 " + ")");
            DBSeries.GlobalSet(DBOnlineSeries.cHasLocalFilesTemp, true, condSeries); //takes about 1/4 of the time of above
        }

        /// <summary>
        /// Removes entries from haystack that are already in db
        /// </summary>
        /// <param name="haystack"></param>
        /// <returns>Returns a list of Files Removed</returns>
        /// <summary>
        /// Episodes whose temporary series no longer exists - left behind by an earlier failed or interrupted match.
        /// They belong to no show, so they never appear anywhere, yet their files count as "already imported" and are
        /// never looked at again. Clearing their records (the files themselves are untouched) lets the import pick
        /// them up as new files and match them properly.
        /// </summary>
        static void ClearStrandedEpisodes()
        {
            try
            {
                const string stranded = "from local_episodes where SeriesID < 0 and SeriesID not in (select ID from local_series)";
                SQLite.NET.SQLiteResultSet count = DBTVSeries.Execute("select count(*) " + stranded);
                int n = 0;
                if (count.Rows.Count > 0) int.TryParse(count.Rows[0].fields[0], out n);
                if (n == 0) return;

                DBTVSeries.Execute("delete " + stranded);
                MPTVSeriesLog.Write(string.Format("Found {0} episode files not linked to any show (left over from an earlier failed match) - they will be matched again as new files", n));
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Could not check for episodes not linked to any show: " + ex.Message);
            }
        }

        /// <summary>
        /// Two different shows can produce the same series name from their file names (e.g. "Prime Suspect" for both
        /// the 1991 series and "Prime Suspect 1973"). The plugin links one name to one show, so the second show's
        /// episodes would be filed under the first. When the show folder has its own TheTVDB id (an "id.tvdb" file or
        /// tvshow.nfo / series.xml) and it differs from the show that name already belongs to, the file gets a name
        /// of its own, so the second show is matched separately - by that id.
        /// </summary>
        private readonly Dictionary<string, int> mFolderIdCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> mNameClaimedThisScan = new Dictionary<string, int>();

        private void SeparateSameNamedShow(parseResult file)
        {
            if (!DBOption.GetOptions(DBOption.cLookForLocalIdFiles)) return;
            if (file == null || file.parser == null || !file.parser.Matches.ContainsKey(DBSeries.cParsedName)) return;

            string name = file.parser.Matches[DBSeries.cParsedName];
            if (string.IsNullOrEmpty(name)) return;

            // which show is this name already linked to? (an existing, matched show, or the first folder seen this scan)
            int linkedId = 0;
            try
            {
                SQLCondition cond = new SQLCondition();
                cond.Add(new DBSeries(), DBSeries.cParsedName, name, SQLConditionType.Equal);
                SQLite.NET.SQLiteResultSet r = DBTVSeries.Execute("select ID from local_series" + cond);
                if (r.Rows.Count > 0) int.TryParse(r.Rows[0].fields[0], out linkedId);
            }
            catch (Exception) { return; }

            int claimed;
            bool seenThisScan = mNameClaimedThisScan.TryGetValue(name, out claimed);
            if (linkedId <= 0 && !seenThisScan)
            {
                // first time this name is seen: remember which show its folder says it is, for the files that follow
                mNameClaimedThisScan[name] = FolderSeriesId(file.full_filename);
                return;
            }

            int folderId = FolderSeriesId(file.full_filename);
            if (folderId <= 0) return; // no id in this folder: nothing to go on, keep the normal behaviour

            int expected = linkedId > 0 ? linkedId : claimed;
            if (expected <= 0 || expected == folderId) return;

            string separateName = name + " [tvdb " + folderId + "]";
            file.parser.Matches[DBSeries.cParsedName] = separateName;
            MPTVSeriesLog.Write(string.Format("\"{0}\" is a different show from the one named \"{1}\" (its folder says TheTVDB {2}, not {3}) - matching it separately",
                                              file.match_filename, name, folderId, expected), MPTVSeriesLog.LogLevel.Debug);
        }

        private int FolderSeriesId(string file)
        {
            string dir;
            try { dir = Path.GetDirectoryName(file); } catch (Exception) { return 0; }
            if (string.IsNullOrEmpty(dir)) return 0;

            int id;
            if (mFolderIdCache.TryGetValue(dir, out id)) return id;

            string source;
            id = LocalSeriesIdFinder.FindSeriesId(new[] { file }, out source);
            mFolderIdCache[dir] = id;
            return id;
        }

        public static List<string> RemoveFilesInDB(ref IList<parseResult> haystack)
        {
            ClearStrandedEpisodes();

            // a set, not a list: with tens of thousands of files a list lookup per file was very slow
            var dbEps = new HashSet<string>();
            SQLite.NET.SQLiteResultSet results = DBTVSeries.Execute("select episodefilename from local_episodes");
            if (results.Rows.Count > 0)
            {
                for (int index = 0; index < results.Rows.Count; index++)
                {
                    dbEps.Add(results.Rows[index].fields[0]);
                }
            }
            List<string> updateStatusEps = new List<string>();
            for (int i = 0; i < haystack.Count; i++)
            {
                if (dbEps.Contains(haystack[i].full_filename))
                {
                    updateStatusEps.Add(haystack[i].full_filename);
                    haystack.RemoveAt(i);
                    i--;
                }
            }
            return updateStatusEps;
        }

        private void ParseLocal(List<PathPair> files)
        {
            ParseLocal(files, null);
        }
        private void ParseLocal(List<PathPair> files, IList<parseResult> UserModifiedParsedResults)
        {
            bool resultsFromUser = UserModifiedParsedResults != null;
            MPTVSeriesLog.Write(bigLogMessage("Gathering Local Information"));
            // NOTE: previously skipped this parse when UserModifiedParsedResults was available, since its
            // output gets replaced by UserModifiedParsedResults a few lines below regardless - that part
            // was true and harmless on its own. But it depended on the scan above also being skipped
            // (files == null), and that part was NOT safe (see the comment at this method's only caller).
            // Restored to always parsing so `files` is always genuinely fresh here.
            IList<parseResult> parsedFiles = LocalParse.Parse(files, true);

            // get a list of files that are on disk and still in database
            // also get a list of new files for import returned in parsedFiles
            var updateStatusEps = RemoveFilesInDB(ref parsedFiles);
            // update the importprocessed state for files we previously set as not processed
            UpdateStatus(updateStatusEps);

            // if we have user modified parse results, use these instead
            // user may have manually input for series/season/episodes.
            if (resultsFromUser)
                parsedFiles = UserModifiedParsedResults;

            MPTVSeriesLog.Write("Adding " + parsedFiles.Count.ToString() + " new file(s) to Database");

            // Signal external event listeners that new local files have been added
            mNewLocalFiles = parsedFiles.Count > 0;

            int nSeason = 0;
            List<DBSeries> relatedSeries = new List<DBSeries>();
            List<DBSeason> relatedSeasons = new List<DBSeason>();

            int nIndex = 0;
            mNameClaimedThisScan.Clear();
            foreach (parseResult progress in parsedFiles)
            {
                if (mWorker.CancellationPending)
                    return;
                if (progress.success)
                {
                    SeparateSameNamedShow(progress);

                    DBSeries series = null;
                    DBSeason season = null;
                    if (progress.parser.Matches.ContainsKey(DBOnlineEpisode.cFirstAired))
                    {
                        // series first
                        series = new DBSeries(progress.parser.Matches[DBSeries.cParsedName]);
                        series[DBOnlineSeries.cHasLocalFilesTemp] = 1;
                        // not much to do here except commiting the series
                        series.Commit();
                    }
                    else
                    {
                        nSeason = Convert.ToInt32(progress.parser.Matches[DBEpisode.cSeasonIndex]);

                        // ok, we are sure it's valid now
                        // series first
                        series = new DBSeries(progress.parser.Matches[DBSeries.cParsedName]);
                        series[DBOnlineSeries.cHasLocalFilesTemp] = 1;
                        // not much to do here except commiting the series
                        series.Commit();

                        // season now
                        season = new DBSeason(series[DBSeries.cID], nSeason);
                        season[DBSeason.cHasLocalFilesTemp] = true;
                        season[DBSeason.cHasEpisodes] = true;
                        season.Commit();

                        // already in?
                        bool bSeasonFound = false;
                        foreach (DBSeason seasonLoop in relatedSeasons)
                            if (seasonLoop[DBSeason.cSeriesID] == series[DBOnlineSeries.cID] && seasonLoop[DBSeason.cIndex] == nSeason)
                            {
                                bSeasonFound = true;
                                break;
                            }
                        if (!bSeasonFound)
                            relatedSeasons.Add(season);

                        bool bSeriesFound = false;
                        foreach (DBSeries seriesLoop in relatedSeries)
                            if (seriesLoop[DBOnlineSeries.cID] == series[DBOnlineSeries.cID])
                            {
                                bSeriesFound = true;
                                break;
                            }
                        if (!bSeriesFound)
                            relatedSeries.Add(series);

                    }
                    
                    // then episode
                    DBEpisode episode = new DBEpisode(progress.full_filename, true);
                    bool bNewFile = false;
                    if (episode[DBEpisode.cImportProcessed] != 2) {
                        mDataUpdated = true;
                        bNewFile = true;
                    }
                    
                    episode[DBEpisode.cImportProcessed] = 1;                    
                    episode[DBEpisode.cIsAvailable] = 1;
                    
                    episode[DBEpisode.cSeriesID] = series[DBSeries.cID];
                    if (progress.parser.Matches.ContainsKey(DBEpisode.cEpisodeIndex2))
                    {
                        episode[DBEpisode.cEpisodeIndex2] = progress.parser.Matches[DBEpisode.cEpisodeIndex2];
                        episode[DBEpisode.cCompositeID2] = episode[DBEpisode.cSeriesID] + "_" + nSeason + "x" + episode[DBEpisode.cEpisodeIndex2];

                        if (bNewFile) {
                            //if it's a new file but the DBOnlineEpisode already has the online ID is set, then ensure that the episode is not hidden
                            DBOnlineEpisode doubleEp = new DBOnlineEpisode(episode[DBEpisode.cSeriesID], nSeason, episode[DBEpisode.cEpisodeIndex2]);
                            if (doubleEp[DBOnlineEpisode.cID] > 0 && doubleEp[DBOnlineEpisode.cHidden] == 1) {
                                doubleEp[DBOnlineEpisode.cHidden] = 0;
                                doubleEp.Commit();
                            }
                        }
                    }

                    //episode[DBEpisode.cAvailableSubtitles] = episode.checkHasSubtitles();
                    
                    foreach (KeyValuePair<string, string> match in progress.parser.Matches)
                    {
                        if (match.Key != DBSeries.cParsedName)
                        {
                            episode.AddColumn(match.Key, new DBField(DBField.cTypeString));
                            if (bNewFile || (episode[match.Key] != null && episode[match.Key] != match.Value))
                                episode[match.Key] = match.Value;
                        }
                    }

                    //if it's a new file but the DBOnlineEpisode already has the online ID is set, then ensure that the episode is not hidden
                    if (bNewFile && episode[DBOnlineEpisode.cID] > 0) {
                        episode[DBOnlineEpisode.cHidden] = 0;
                    }

                    // we may need to update the composite id later when matching to online episode                    
                    if (bNewFile)
                        episode[DBEpisode.cCompositeUpdated] = 0;

                    episode.Commit();

                }

                if(++nIndex % 25 == 0)
                    mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.LocalScan, progress.match_filename, nIndex, parsedFiles.Count));
            }

            //now make DBOnlineEpisodes for each unmatched double episode
            //fetch all the episodes that have a CompositeID2 with no matching DBOnlineEpisode
            SQLCondition cond = new SQLCondition(new DBEpisode(), DBEpisode.cCompositeID2, " ", SQLConditionType.NotEqual);
            cond.AddCustom(DBOnlineEpisode.cTableName + "." + DBOnlineEpisode.cCompositeID + " is null");
            List<DBEpisode> missingDoubleEpisodes = DBEpisode.Get(cond, false, true);
            //now foreach episode make a DBOnlineEpisode
            foreach (DBEpisode episode in missingDoubleEpisodes) {
                DBOnlineEpisode onlineEpisode = new DBOnlineEpisode(episode[DBEpisode.cSeriesID], episode[DBEpisode.cSeasonIndex], episode[DBEpisode.cEpisodeIndex2]);
                onlineEpisode.Commit();
            }

            // now go over the touched seasons & series
            var episodes = DBEpisode.Get(new SQLCondition(new DBEpisode(), DBOnlineEpisode.cWatched, false, SQLConditionType.Equal));

            foreach (DBSeason season in relatedSeasons)
            {
                var HasUnwatched = episodes.Any(e => e[DBSeason.cSeriesID] == season[DBSeason.cSeriesID] && e[DBEpisode.cSeasonIndex] == season[DBSeason.cIndex]);
                if (season[DBSeason.cUnwatchedItems] != HasUnwatched || !season[DBSeason.cHidden] != false)
                {
                    season[DBSeason.cUnwatchedItems] = HasUnwatched;
                    season[DBSeason.cHidden] = 0;
                    season.Commit();
                } // else nothing changed, so dont bother
            }
            
            foreach (DBSeries series in relatedSeries)
            {
                var HasUnwatched = episodes.Any(e => e[DBSeries.cID] == series[DBSeries.cID]);
                if (series[DBSeason.cUnwatchedItems] != HasUnwatched || !series[DBSeries.cHidden] != false)
                {
                    series[DBOnlineSeries.cUnwatchedItems] = HasUnwatched;
                    series[DBSeries.cHidden] = 0;
                    series.Commit();
                } // else nothing changed, so dont bother
            }
            
            mWorker.ReportProgress(0, new ParsingProgress(ParsingAction.LocalScan, parsedFiles.Count));
        }
        #endregion

        #region HelperFunctions
        static List<string> generateIDListOfString<T>(List<T> entities, string fieldname) where T : DBTable
        {
            // generate a comma separated list of all the ids
            List<String> sSeriesIDs = new List<String>(entities.Count);
            if (entities.Count > 0)
                foreach (DBTable entity in entities)
                    sSeriesIDs.Add(entity[fieldname].ToString());
            return sSeriesIDs;
        }
        #endregion

        #region MessageFormatters
        static string bigLogMessage(string msg)
        {
            return string.Format("***************     {0}     ***************", msg);
        }

        static string prettyStars(int length)
        {
            StringBuilder b = new StringBuilder(length);
            for (int i = 0; i < length; i++) b.Append('*');
            return b.ToString();
        }
        #endregion
    }
}
