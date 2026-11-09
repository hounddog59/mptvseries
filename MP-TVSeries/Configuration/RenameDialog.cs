using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WindowPlugins.GUITVSeries.Configuration
{
    /// <summary>
    /// Community fork: the TV renamer (Details tab: right-click a show, "Rename files..."). Like Media Center Master:
    /// one show at a time - the show folder first (you can type over it), then its season folders and episode files,
    /// each with a tick. Episode titles come from the online data, subtitles / .nfo / thumbnails are renamed with their
    /// episode, MP-TVSeries' database is updated as it goes (no rescan needed) and "Undo the last rename" puts it back.
    /// Only matched episodes are renamed - a file MP-TVSeries hasn't matched is left alone.
    /// </summary>
    public class RenameDialog : Form
    {
        // ---------------------------------------------------------------- naming

        public const string Pieces = "{show} {sorttitle} {year} {season:00} {episode:00} {title} {airdate} {quality} {rating} {genre}";

        static readonly string[][] EpisodeStyles =
        {
            new[] { "Show S01E01 Episode Title [2026-08-07]", "{show} S{season:00}E{episode:00} {title} [{airdate}]" },
            new[] { "Show S01E01 Episode Title [720p][2026-08-07]", "{show} S{season:00}E{episode:00} {title} [{quality}][{airdate}]" },
            new[] { "Show - S01E01 - Episode Title", "{show} - S{season:00}E{episode:00} - {title}" },
            new[] { "Show S01E01 Episode Title", "{show} S{season:00}E{episode:00} {title}" },
            new[] { "Show - S01E01", "{show} - S{season:00}E{episode:00}" },
            new[] { "Show 1x01 Episode Title", "{show} {season}x{episode:00} {title}" },
            new[] { "Show.S01E01.Episode.Title", "{show}.S{season:00}E{episode:00}.{title}" },
        };

        static readonly string[][] SeasonStyles =
        {
            new[] { "Leave the season folders as they are", "" },
            new[] { "Show Season 01", "{show} Season {season:00}" },
            new[] { "Season 01", "Season {season:00}" },
            new[] { "Season 1", "Season {season}" },
        };

        static readonly string[][] ShowStyles =
        {
            new[] { "Leave the show folder as it is", "" },
            new[] { "Show (Year)", "{show} ({year})" },
            new[] { "Show (Year) [7.9]", "{show} ({year}) [{rating}]" },
            new[] { "Show (Year) [7.9][Genre]", "{show} ({year}) [{rating}][{genre}]" },
            new[] { "Show, The (Year) [7.9][Genre]  (like Media Center Master)", "{sorttitle} ({year}) [{rating}][{genre}]" },
        };

        const string OptEpisode = "CommunityFork_RenameEpisode", OptSeason = "CommunityFork_RenameSeason", OptShow = "CommunityFork_RenameShow";

        static string Saved(string option, string myTorrentsKey, string fallback)
        {
            string v = DBOption.GetOptions(option);
            if (!string.IsNullOrEmpty(v)) return v == "-" ? "" : v;
            // first time: the same naming as MyTorrents' "When finished" settings, so both name things alike
            string mt = MyTorrentsSetting(myTorrentsKey);
            return string.IsNullOrEmpty(mt) ? fallback : mt;
        }

        static string MyTorrentsSetting(string key)
        {
            try
            {
                string f = MediaPortal.Configuration.Config.GetFile(MediaPortal.Configuration.Config.Dir.Config, "MyTorrents.settings.xml");
                if (!File.Exists(f)) return null;
                System.Xml.XmlDocument x = new System.Xml.XmlDocument();
                x.Load(f);
                System.Xml.XmlNode n = x.SelectSingleNode("/MyTorrents/entry[@name='" + key + "']");
                return n == null ? null : n.InnerText.Trim();
            }
            catch { return null; }
        }

        class ShowInfo { public string Name = "", SortTitle = "", Year = "", Rating = "", Genre = ""; }

        class EpInfo
        {
            public DBEpisode Ep;
            public string File = "", Title = "", AirDate = "", Quality = "";
            public int Season, Number, Number2;
        }

        static string Fill(string pattern, ShowInfo s, EpInfo e, int season)
        {
            if (string.IsNullOrEmpty(pattern)) return "";
            bool dots = Regex.IsMatch(pattern, @"\}\.\w|\}\.\{");
            Func<string, string> piece = v => dots ? Regex.Replace((v ?? "").Trim(), @"\s+", ".") : (v ?? "").Trim();
            string ep = "", epShort = "";
            if (e != null)
            {
                ep = e.Number.ToString("00") + (e.Number2 > 0 ? "E" + e.Number2.ToString("00") : "");
                epShort = e.Number.ToString("00") + (e.Number2 > 0 ? "-" + e.Number2.ToString("00") : "");
            }
            string n = pattern
                .Replace("{show}", piece(s.Name))
                .Replace("{sorttitle}", piece(s.SortTitle))
                .Replace("{year}", s.Year)
                .Replace("{rating}", s.Rating)
                .Replace("{genre}", piece(s.Genre))
                .Replace("{season:00}", season.ToString("00"))
                .Replace("{season}", season.ToString())
                .Replace("E{episode:00}", "E" + ep)
                .Replace("x{episode:00}", "x" + epShort)
                .Replace("{episode:00}", ep)
                .Replace("{title}", piece(e == null ? "" : e.Title))
                .Replace("{airdate}", e == null ? "" : e.AirDate)
                .Replace("{quality}", e == null ? "" : e.Quality);
            // tidy away pieces with nothing in them: "[]", "()", "- -", spare spaces / dots
            n = Regex.Replace(n, @"\[\s*\]|\(\s*\)", "");
            n = Regex.Replace(n, @"\s+-\s+(?=-|$)", " ");
            n = Regex.Replace(n, @"\.{2,}", ".");
            n = Regex.Replace(n, @"\s{2,}", " ");
            return Safe(n.Trim(' ', '-', '.'));
        }

        /// No characters Windows won't allow in a name; "Mission: Impossible" -> "Mission - Impossible".
        static string Safe(string name)
        {
            name = Regex.Replace(name ?? "", @"\s*:\s*", " - ");
            name = name.Replace("?", "").Replace("\"", "'").Replace("*", "").Replace("<", "").Replace(">", "").Replace("|", "-").Replace("/", "-").Replace("\\", "-");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
            name = Regex.Replace(name, @"\s{2,}", " ");
            return name.Trim(' ', '.');
        }

        static string SortTitle(string t)
        {
            Match m = Regex.Match(t ?? "", @"^(The|A|An)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return t ?? "";
            string rest = m.Groups[2].Value;
            Match y = Regex.Match(rest, @"^(.*?)(\s*\((19|20)\d{2}\))$");
            return y.Success ? y.Groups[1].Value + ", " + m.Groups[1].Value + y.Groups[2].Value : rest + ", " + m.Groups[1].Value;
        }

        static readonly Regex SeasonDirName = new Regex(@"(^|\s)(season|series|staffel|s)\s*\d+(\s*\[[^\]]*\])*$|^specials?\b", RegexOptions.IgnoreCase);
        static readonly HashSet<string> VideoExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".mpg", ".mpeg", ".ts", ".m2ts", ".divx", ".flv", ".webm", ".ogm", ".vob", ".iso" };

        // ---------------------------------------------------------------- one planned change

        class Change
        {
            public bool IsFolder;
            public string From = "", To = "";   // full paths (files: within their current folder)
            public string Note = "";
            public bool Problem, Same;
            public List<string[]> Companions = new List<string[]>(); // subtitles / nfo / thumbnails: from, to
        }

        // ---------------------------------------------------------------- the dialog

        readonly List<DBSeries> _shows;
        int _index;
        readonly int _season; // -1 = every season
        ShowInfo _show;
        List<EpInfo> _eps = new List<EpInfo>();
        string _showFolder; // null = the files aren't in one show folder
        List<Change> _changes = new List<Change>();
        bool _loading;
        public bool Renamed { get; private set; }

        readonly Label _heading = new Label { AutoSize = false, Font = new Font("Microsoft Sans Serif", 10f, FontStyle.Bold) };
        readonly Label _help = new Label { AutoSize = false, ForeColor = Color.DimGray };
        readonly ComboBox _epStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox _epPattern = new TextBox();
        readonly ComboBox _seasonStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox _seasonPattern = new TextBox();
        readonly ComboBox _showStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox _showPattern = new TextBox();
        readonly Label _pieces = new Label { AutoSize = true, ForeColor = Color.DimGray, Text = "Pieces you can use: " + Pieces };
        readonly Label _showNow = new Label { AutoSize = false };
        readonly TextBox _showNew = new TextBox();
        readonly ListView _list = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, ShowItemToolTips = true };
        readonly CheckBox _showSame = new CheckBox { Text = "Show the ones already named right", AutoSize = true };
        readonly Button _tickAll = new Button { Text = "Tick all", AutoSize = true };
        readonly Button _untickAll = new Button { Text = "Untick all", AutoSize = true };
        readonly Button _undo = new Button { Text = "Undo the last rename", AutoSize = true };
        readonly Label _status = new Label { AutoSize = false, ForeColor = Color.DarkGreen };
        readonly Button _rename = new Button { Text = "Rename the ticked ones", AutoSize = true };
        readonly Button _skip = new Button { Text = "Skip this show", AutoSize = true };
        readonly Button _close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };

        public RenameDialog(List<DBSeries> shows, int season)
        {
            _shows = shows ?? new List<DBSeries>();
            _season = season;
            Text = "Rename files";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 680);
            MinimumSize = new Size(820, 520);
            Font = new Font("Microsoft Sans Serif", 8.25f);
            ShowIcon = false;
            CancelButton = _close;

            _help.Text = "Check the show folder, then the episodes - untick anything you don't want renamed. Episode titles come from " +
                         "the online data. Subtitles, .nfo files and thumbnails are renamed with their episode. Files MP-TVSeries hasn't " +
                         "matched are left alone. Grey: already right. Red: can't be renamed (see the note).";

            _list.Columns.Add("Now", 330);
            _list.Columns.Add("Will be", 380);
            _list.Columns.Add("Note", 220);

            Fill(_epStyle, EpisodeStyles);
            Fill(_seasonStyle, SeasonStyles);
            Fill(_showStyle, ShowStyles);
            _epPattern.Text = Saved(OptEpisode, "Finished.NamePattern", EpisodeStyles[0][1]);
            _seasonPattern.Text = Saved(OptSeason, "Finished.SeasonPattern", SeasonStyles[1][1]);
            _showPattern.Text = Saved(OptShow, "Finished.ShowFolderPattern", ShowStyles[0][1]);
            Pick(_epStyle, EpisodeStyles, _epPattern.Text);
            Pick(_seasonStyle, SeasonStyles, _seasonPattern.Text);
            Pick(_showStyle, ShowStyles, _showPattern.Text);

            Wire(_epStyle, EpisodeStyles, _epPattern);
            Wire(_seasonStyle, SeasonStyles, _seasonPattern);
            Wire(_showStyle, ShowStyles, _showPattern);
            _showPattern.TextChanged += (s, e) => { if (!_loading) { _showNew.Text = ShowFolderName(); } };
            _showNew.TextChanged += (s, e) => { if (!_loading) Plan(); };
            _showSame.CheckedChanged += (s, e) => ShowList();
            _tickAll.Click += (s, e) => { foreach (ListViewItem i in _list.Items) if (!((Change)i.Tag).Problem && !((Change)i.Tag).Same) i.Checked = true; };
            _untickAll.Click += (s, e) => { foreach (ListViewItem i in _list.Items) i.Checked = false; };
            _list.ItemCheck += (s, e) =>
            {
                Change c = _list.Items[e.Index].Tag as Change;
                if (c != null && (c.Problem || c.Same) && e.NewValue == CheckState.Checked) e.NewValue = CheckState.Unchecked;
            };
            _undo.Click += (s, e) => UndoLast();
            _rename.Click += (s, e) => DoRename();
            _skip.Click += (s, e) => Next();
            _undo.Enabled = RenameLog.HasLast;

            Controls.AddRange(new Control[] { _heading, _help, _epStyle, _epPattern, _seasonStyle, _seasonPattern, _showStyle, _showPattern, _pieces,
                                              _showNow, _showNew, _list, _showSame, _tickAll, _untickAll, _undo, _status, _rename, _skip, _close });
            AddLabel("Episode names:", 0);
            AddLabel("Season folders:", 1);
            AddLabel("Show folder:", 2);
            Resize += (s, e) => LayOut();
            LayOut();
            _skip.Visible = _shows.Count > 1;
            Shown += (s, e) => LoadShow();
        }

        readonly List<Label> _rowLabels = new List<Label>();
        void AddLabel(string text, int row)
        {
            Label l = new Label { Text = text, AutoSize = true };
            _rowLabels.Add(l);
            Controls.Add(l);
        }

        static void Fill(ComboBox c, string[][] styles)
        {
            foreach (string[] s in styles) c.Items.Add(s[0]);
            c.Items.Add("Your own (type it in the box)");
        }

        static void Pick(ComboBox c, string[][] styles, string pattern)
        {
            int i = Array.FindIndex(styles, s => s[1] == pattern);
            c.SelectedIndex = i >= 0 ? i : styles.Length;
        }

        void Wire(ComboBox c, string[][] styles, TextBox box)
        {
            c.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;
                if (c.SelectedIndex >= 0 && c.SelectedIndex < styles.Length) box.Text = styles[c.SelectedIndex][1];
            };
            box.TextChanged += (s, e) =>
            {
                if (_loading) return;
                _loading = true;
                Pick(c, styles, box.Text);
                _loading = false;
                if (box != _showPattern) Plan();
            };
        }

        void LayOut()
        {
            int w = ClientSize.Width - 24;
            _heading.SetBounds(12, 10, w, 20);
            _help.SetBounds(12, 32, w, 30);
            int y = 68;
            for (int r = 0; r < 3; r++)
            {
                ComboBox c = r == 0 ? _epStyle : r == 1 ? _seasonStyle : _showStyle;
                TextBox t = r == 0 ? _epPattern : r == 1 ? _seasonPattern : _showPattern;
                if (r < _rowLabels.Count) _rowLabels[r].Location = new Point(12, y + 4);
                c.SetBounds(110, y, 330, 21);
                t.SetBounds(450, y, w - 438, 21);
                y += 27;
            }
            _pieces.Location = new Point(110, y);
            y += 22;
            _showNow.SetBounds(12, y + 3, w, 18);
            y += 22;
            _showNew.SetBounds(12, y, w, 21);
            y += 30;
            int bottom = ClientSize.Height - 40;
            _list.SetBounds(12, y, w, Math.Max(120, bottom - y - 30));
            int cw = Math.Max(600, w - 30);
            _list.Columns[0].Width = cw * 36 / 100;
            _list.Columns[1].Width = cw * 42 / 100;
            _list.Columns[2].Width = cw * 22 / 100;
            int ly = _list.Bottom + 6;
            _showSame.Location = new Point(12, ly + 3);
            _tickAll.Location = new Point(_showSame.Right + 20, ly);
            _untickAll.Location = new Point(_tickAll.Right + 6, ly);
            _undo.Location = new Point(_untickAll.Right + 20, ly);
            _status.SetBounds(_undo.Right + 12, ly + 5, Math.Max(100, w - _undo.Right), 18);
            _close.Location = new Point(ClientSize.Width - 12 - _close.Width, bottom + 6);
            _skip.Location = new Point(_close.Left - 6 - _skip.Width, bottom + 6);
            _rename.Location = new Point((_skip.Visible ? _skip.Left : _close.Left) - 6 - _rename.Width, bottom + 6);
        }

        // ---------------------------------------------------------------- reading a show

        void LoadShow()
        {
            if (_index >= _shows.Count) { DialogResult = Renamed ? DialogResult.OK : DialogResult.Cancel; Close(); return; }
            DBSeries series = _shows[_index];
            _show = ReadShow(series);
            _heading.Text = "Rename files - " + _show.Name + (_shows.Count > 1 ? string.Format("   ({0} of {1})", _index + 1, _shows.Count) : "") + (_season >= 0 ? "   - season " + _season : "");
            _eps = ReadEpisodes(series);
            _showFolder = FindShowFolder(_eps);
            _loading = true;
            _showNow.Text = _showFolder == null
                ? "Show folder: its files are in more than one folder, so the show folder is left as it is."
                : "Show folder now: " + _showFolder;
            _showNew.Text = ShowFolderName();
            _showNew.Enabled = _showFolder != null && _season < 0;
            _loading = false;
            _status.Text = "";
            Plan();
        }

        static ShowInfo ReadShow(DBSeries series)
        {
            ShowInfo s = new ShowInfo();
            s.Name = Safe((string)series[DBOnlineSeries.cPrettyName]);
            s.SortTitle = Safe(SortTitle(s.Name));
            string fa = series[DBOnlineSeries.cFirstAired];
            if (!string.IsNullOrEmpty(fa) && fa.Length >= 4) s.Year = fa.Substring(0, 4);
            // a show called "Night Sky (2022)" already has its year - don't add it twice
            if (Regex.IsMatch(s.Name, @"\((19|20)\d{2}\)$")) s.Year = "";
            double r;
            if (double.TryParse((string)series[DBOnlineSeries.cRating], NumberStyles.Float, CultureInfo.InvariantCulture, out r) && r > 0)
                s.Rating = r.ToString("0.0", CultureInfo.InvariantCulture);
            string g = series[DBOnlineSeries.cGenre];
            s.Genre = string.Join("-", (g ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Take(2).ToArray());
            return s;
        }

        List<EpInfo> ReadEpisodes(DBSeries series)
        {
            List<EpInfo> list = new List<EpInfo>();
            List<DBEpisode> all = _season >= 0 ? DBEpisode.Get((int)series[DBSeries.cID], _season, false) : DBEpisode.Get((int)series[DBSeries.cID], false);
            if (all == null) return list;
            Dictionary<string, DBEpisode> byNumber = new Dictionary<string, DBEpisode>();
            foreach (DBEpisode ep in all)
                byNumber[(int)ep[DBOnlineEpisode.cSeasonIndex] + "x" + (int)ep[DBOnlineEpisode.cEpisodeIndex]] = ep;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DBEpisode ep in all)
            {
                string file = ep[DBEpisode.cFilename];
                if (string.IsNullOrEmpty(file) || !seen.Add(file)) continue;
                if ((int)ep[DBOnlineEpisode.cID] <= 0) continue; // not matched - left alone
                EpInfo e = new EpInfo { Ep = ep, File = file };
                e.Season = ep[DBOnlineEpisode.cSeasonIndex];
                e.Number = ep[DBOnlineEpisode.cEpisodeIndex];
                int n2 = ep[DBEpisode.cEpisodeIndex2];
                e.Number2 = n2 > 0 && n2 != e.Number ? n2 : 0;
                e.Title = ep[DBOnlineEpisode.cEpisodeName];
                DBEpisode second;
                if (e.Number2 > 0 && byNumber.TryGetValue(e.Season + "x" + e.Number2, out second))
                {
                    string t2 = second[DBOnlineEpisode.cEpisodeName];
                    if (!string.IsNullOrEmpty(t2) && !string.Equals(t2, e.Title, StringComparison.OrdinalIgnoreCase)) e.Title = e.Title + " & " + t2;
                }
                string aired = ep[DBOnlineEpisode.cFirstAired];
                e.AirDate = aired != null && aired.Length >= 10 ? aired.Substring(0, 10) : "";
                int h = ep[DBEpisode.cVideoHeight];
                e.Quality = h >= 1700 ? "2160p" : h >= 1000 ? "1080p" : h >= 700 ? "720p" : h >= 560 ? "576p" : h >= 400 ? "480p" : "";
                list.Add(e);
            }
            return list.OrderBy(x => x.Season).ThenBy(x => x.Number).ToList();
        }

        static string ShowDirOf(string file)
        {
            string dir = Path.GetDirectoryName(file);
            if (dir == null) return null;
            return SeasonDirName.IsMatch(Path.GetFileName(dir)) ? Path.GetDirectoryName(dir) : dir;
        }

        static string FindShowFolder(List<EpInfo> eps)
        {
            List<string> dirs = eps.Select(e => ShowDirOf(e.File)).Where(d => d != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return dirs.Count == 1 ? dirs[0] : null;
        }

        string ShowFolderName()
        {
            if (_showFolder == null) return "";
            string n = Fill(_showPattern.Text, _show, null, 0);
            return n.Length > 0 ? n : Path.GetFileName(_showFolder);
        }

        // ---------------------------------------------------------------- planning

        void Plan()
        {
            if (_show == null) return;
            _changes = new List<Change>();
            HashSet<string> targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // episode files (renamed in the folder they're in now)
            foreach (EpInfo e in _eps)
            {
                Change c = new Change { From = e.File };
                string dir = Path.GetDirectoryName(e.File);
                string ext = Path.GetExtension(e.File);
                string name = Fill(_epPattern.Text, _show, e, e.Season);
                if (name.Length == 0) { c.To = e.File; c.Same = true; _changes.Add(c); continue; }
                c.To = Path.Combine(dir, name + ext);
                if (string.Equals(c.From, c.To, StringComparison.Ordinal)) c.Same = true;
                else if (!targets.Add(c.To)) { c.Problem = true; c.Note = "another file would get the same name"; }
                else if (!string.Equals(c.From, c.To, StringComparison.OrdinalIgnoreCase) && File.Exists(c.To)) { c.Problem = true; c.Note = "a file with that name is already there"; }
                else if (c.To.Length >= 250) { c.Problem = true; c.Note = "the name would be too long for Windows"; }
                else if (string.IsNullOrEmpty(e.Title) && _epPattern.Text.Contains("{title}")) c.Note = "no title online yet";
                if (!c.Same && !c.Problem) c.Companions = Companions(e.File, Path.GetFileNameWithoutExtension(c.To));
                if (c.Companions.Count > 0 && c.Note.Length == 0) c.Note = "+ " + c.Companions.Count + " subtitle / extra file(s)";
                _changes.Add(c);
            }

            // season folders
            if (_seasonPattern.Text.Length > 0 && _showFolder != null)
            {
                foreach (var g in _eps.GroupBy(e => Path.GetDirectoryName(e.File), StringComparer.OrdinalIgnoreCase))
                {
                    string dir = g.Key;
                    if (!SeasonDirName.IsMatch(Path.GetFileName(dir)) || !string.Equals(Path.GetDirectoryName(dir), _showFolder, StringComparison.OrdinalIgnoreCase)) continue;
                    List<int> seasons = g.Select(e => e.Season).Distinct().ToList();
                    Change c = new Change { IsFolder = true, From = dir };
                    if (seasons.Count != 1) { c.To = dir; c.Problem = true; c.Note = "episodes of more than one season in it"; _changes.Add(c); continue; }
                    string name = Fill(_seasonPattern.Text, _show, null, seasons[0]);
                    c.To = Path.Combine(_showFolder, name);
                    if (string.Equals(c.From, c.To, StringComparison.Ordinal)) c.Same = true;
                    else if (!targets.Add(c.To)) { c.Problem = true; c.Note = "another folder would get the same name"; }
                    else if (!string.Equals(c.From, c.To, StringComparison.OrdinalIgnoreCase) && Directory.Exists(c.To)) { c.Problem = true; c.Note = "a folder with that name is already there"; }
                    _changes.Add(c);
                }
            }

            // the show folder
            if (_showFolder != null && _season < 0)
            {
                string name = Safe(_showNew.Text);
                if (name.Length > 0)
                {
                    Change c = new Change { IsFolder = true, From = _showFolder, To = Path.Combine(Path.GetDirectoryName(_showFolder) ?? "", name) };
                    if (string.Equals(c.From, c.To, StringComparison.Ordinal)) c.Same = true;
                    else if (!string.Equals(c.From, c.To, StringComparison.OrdinalIgnoreCase) && Directory.Exists(c.To)) { c.Problem = true; c.Note = "a folder with that name is already there"; }
                    else c.Note = "the show folder";
                    _changes.Add(c);
                }
            }
            ShowList();
        }

        /// Subtitles, .nfo, thumbnails... next to the episode: "Old name.en.srt", "Old name-thumb.jpg" (never another video).
        static List<string[]> Companions(string video, string newBase)
        {
            List<string[]> list = new List<string[]>();
            try
            {
                string dir = Path.GetDirectoryName(video);
                string oldBase = Path.GetFileNameWithoutExtension(video);
                foreach (string f in Directory.GetFiles(dir, oldBase + "*"))
                {
                    if (string.Equals(f, video, StringComparison.OrdinalIgnoreCase)) continue;
                    string rest = Path.GetFileName(f).Substring(oldBase.Length);
                    if (rest.Length == 0 || (rest[0] != '.' && rest[0] != '-' && rest[0] != '_')) continue;
                    if (VideoExt.Contains(Path.GetExtension(f))) continue;
                    list.Add(new[] { f, Path.Combine(dir, newBase + rest) });
                }
            }
            catch { }
            return list;
        }

        void ShowList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            int todo = 0, same = 0, problems = 0;
            foreach (Change c in _changes.OrderBy(c => c.IsFolder ? 1 : 0))
            {
                if (c.Same) same++; else if (c.Problem) problems++; else todo++;
                if (c.Same && !_showSame.Checked) continue;
                string now = c.IsFolder ? "[folder] " + Path.GetFileName(c.From) : Path.GetFileName(c.From);
                string to = c.Same ? "(already right)" : c.Problem ? "" : (c.IsFolder ? "[folder] " : "") + Path.GetFileName(c.To);
                ListViewItem i = new ListViewItem(new[] { now, to, c.Note }) { Tag = c, ToolTipText = c.From };
                if (c.Same) i.ForeColor = Color.Gray;
                else if (c.Problem) i.ForeColor = Color.Firebrick;
                else i.Checked = true;
                _list.Items.Add(i);
            }
            _list.EndUpdate();
            _rename.Enabled = todo > 0;
            _status.ForeColor = Color.DimGray;
            _status.Text = string.Format("{0} to rename, {1} already right{2}", todo, same, problems > 0 ? ", " + problems + " can't be renamed" : "");
        }

        // ---------------------------------------------------------------- renaming

        void DoRename()
        {
            List<Change> ticked = _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (Change)i.Tag).ToList();
            if (ticked.Count == 0) return;
            SavePatterns();

            string batch = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            List<string> failed = new List<string>();
            int done = 0;
            Cursor = Cursors.WaitCursor;
            try
            {
                // 1. files, in the folder they're in now
                foreach (Change c in ticked.Where(c => !c.IsFolder))
                {
                    string err = MoveFile(c.From, c.To);
                    if (err != null) { failed.Add(Path.GetFileName(c.From) + ": " + err); continue; }
                    RenameLog.Add(batch, false, c.From, c.To);
                    UpdateDatabase(c.From, c.To, false);
                    done++;
                    foreach (string[] comp in c.Companions)
                        if (MoveFile(comp[0], comp[1]) == null) RenameLog.Add(batch, false, comp[0], comp[1]);
                }
                // 2. season folders, 3. the show folder last (the season folders' paths change with it)
                foreach (Change c in ticked.Where(c => c.IsFolder).OrderBy(c => string.Equals(c.From, _showFolder, StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                {
                    string err = MoveFolder(c.From, c.To);
                    if (err != null) { failed.Add(Path.GetFileName(c.From) + ": " + err); continue; }
                    RenameLog.Add(batch, true, c.From, c.To);
                    UpdateDatabase(c.From, c.To, true);
                    done++;
                }
            }
            finally { Cursor = Cursors.Default; }

            cache.dump();
            Renamed |= done > 0;
            MPTVSeriesLog.Write(string.Format("Renamer: {0} - {1} renamed, {2} not", _show.Name, done, failed.Count));
            _undo.Enabled = RenameLog.HasLast;
            if (failed.Count > 0)
                MessageBox.Show(this, done + " renamed. These couldn't be:\r\n\r\n" + string.Join("\r\n", failed.Take(15).ToArray()) + (failed.Count > 15 ? "\r\n..." : ""),
                    "Rename files", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            if (_shows.Count > 1 && failed.Count == 0) { Next(); return; }
            LoadShow();
            _status.ForeColor = Color.DarkGreen;
            _status.Text = done + " renamed." + (failed.Count > 0 ? " " + failed.Count + " couldn't be." : "");
        }

        void Next()
        {
            _index++;
            LoadShow();
        }

        void SavePatterns()
        {
            DBOption.SetOptions(OptEpisode, _epPattern.Text.Length == 0 ? "-" : _epPattern.Text);
            DBOption.SetOptions(OptSeason, _seasonPattern.Text.Length == 0 ? "-" : _seasonPattern.Text);
            DBOption.SetOptions(OptShow, _showPattern.Text.Length == 0 ? "-" : _showPattern.Text);
        }

        static string MoveFile(string from, string to)
        {
            try
            {
                if (!File.Exists(from)) return "the file isn't there any more";
                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                {
                    // only the capitals change: Windows needs a step in between
                    string temp = from + ".renaming";
                    File.Move(from, temp);
                    File.Move(temp, to);
                }
                else
                {
                    if (File.Exists(to)) return "a file with that name is already there";
                    File.Move(from, to);
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static string MoveFolder(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from)) return "the folder isn't there any more";
                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                {
                    string temp = from + ".renaming";
                    Directory.Move(from, temp);
                    Directory.Move(temp, to);
                }
                else
                {
                    if (Directory.Exists(to)) return "a folder with that name is already there";
                    Directory.Move(from, to);
                }
                return null;
            }
            catch (Exception ex) { return ex.Message + " (is a file in it open, or MediaPortal playing it?)"; }
        }

        /// MP-TVSeries' records follow the file (or every file in the folder) to its new name.
        internal static void UpdateDatabase(string from, string to, bool folder)
        {
            try
            {
                if (!folder)
                {
                    DBTVSeries.Execute(string.Format("update local_episodes set EpisodeFilename = '{0}' where EpisodeFilename = '{1}'", Q(to), Q(from)));
                }
                else
                {
                    string f = from.TrimEnd('\\') + "\\", t = to.TrimEnd('\\') + "\\";
                    DBTVSeries.Execute(string.Format("update local_episodes set EpisodeFilename = '{0}' || substr(EpisodeFilename, {1}) where substr(EpisodeFilename, 1, {2}) = '{3}'",
                        Q(t), f.Length + 1, f.Length, Q(f)));
                }
            }
            catch (Exception ex) { MPTVSeriesLog.Write("Renamer: couldn't update the database for " + from + ": " + ex.Message); }
        }

        static string Q(string s) { return (s ?? "").Replace("'", "''"); }

        void UndoLast()
        {
            List<RenameLog.Entry> last = RenameLog.Last();
            if (last.Count == 0) return;
            if (MessageBox.Show(this, string.Format("Put back the last rename ({0}, {1} change(s))?", last[0].Batch, last.Count), "Undo the last rename",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int ok = 0, bad = 0;
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (RenameLog.Entry e in Enumerable.Reverse(last))
                {
                    string err = e.Folder ? MoveFolder(e.To, e.From) : MoveFile(e.To, e.From);
                    if (err != null) { bad++; continue; }
                    UpdateDatabase(e.To, e.From, e.Folder);
                    ok++;
                }
            }
            finally { Cursor = Cursors.Default; }
            RenameLog.RemoveLast();
            cache.dump();
            Renamed = true;
            MPTVSeriesLog.Write(string.Format("Renamer: undo - {0} put back, {1} not", ok, bad));
            _undo.Enabled = RenameLog.HasLast;
            LoadShow();
            _status.ForeColor = bad > 0 ? Color.Firebrick : Color.DarkGreen;
            _status.Text = ok + " put back" + (bad > 0 ? ", " + bad + " couldn't be" : "") + ".";
        }
    }

    /// <summary>What the renamer did, so the last rename can be undone: MP-TVSeries.rename-log.txt (batch, kind, from, to).</summary>
    static class RenameLog
    {
        public class Entry { public string Batch, From, To; public bool Folder; }

        static string FilePath
        {
            get { return MediaPortal.Configuration.Config.GetFile(MediaPortal.Configuration.Config.Dir.Config, "MP-TVSeries.rename-log.txt"); }
        }

        public static void Add(string batch, bool folder, string from, string to)
        {
            try { File.AppendAllText(FilePath, batch + "\t" + (folder ? "folder" : "file") + "\t" + from + "\t" + to + Environment.NewLine, Encoding.UTF8); }
            catch (Exception ex) { MPTVSeriesLog.Write("Renamer: couldn't write the rename log: " + ex.Message); }
        }

        static List<Entry> All()
        {
            List<Entry> list = new List<Entry>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (string l in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string[] p = l.Split('\t');
                    if (p.Length == 4) list.Add(new Entry { Batch = p[0], Folder = p[1] == "folder", From = p[2], To = p[3] });
                }
            }
            catch { }
            return list;
        }

        public static bool HasLast { get { return All().Count > 0; } }

        public static List<Entry> Last()
        {
            List<Entry> all = All();
            if (all.Count == 0) return all;
            string b = all[all.Count - 1].Batch;
            return all.Where(e => e.Batch == b).ToList();
        }

        public static void RemoveLast()
        {
            try
            {
                List<Entry> all = All();
                if (all.Count == 0) return;
                string b = all[all.Count - 1].Batch;
                File.WriteAllLines(FilePath, all.Where(e => e.Batch != b).Select(e => e.Batch + "\t" + (e.Folder ? "folder" : "file") + "\t" + e.From + "\t" + e.To).ToArray(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
