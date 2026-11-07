using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowPlugins.GUITVSeries.Configuration
{
    /// <summary>
    /// community fork 4.8.0.9: Details tab, right-click a show or season > "Delete several..." - tick any number of
    /// episodes and remove them in one go (Mark, 7 Nov). "Tick the ones whose files are gone" picks out episodes
    /// still listed after their files were renamed or moved (Magoo's "E01" files after they became "S01E01").
    /// </summary>
    public class DeleteSeveralDialog : Form
    {
        private readonly ListView mList = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill };
        private readonly RadioButton mLibraryOnly = new RadioButton { Text = "Remove them from the library only (the files stay where they are)", AutoSize = true, Checked = true };
        private readonly RadioButton mFilesToo = new RadioButton { Text = "Delete the files too", AutoSize = true };
        private readonly Label mCount = new Label { AutoSize = true };

        public List<DBEpisode> Ticked { get; private set; }
        public bool AlsoFiles => mFilesToo.Checked;

        public static bool FileGone(DBEpisode ep)
        {
            string f = ep[DBEpisode.cFilename];
            if (string.IsNullOrEmpty(f)) return false;
            try { return !File.Exists(f); } catch { return false; }
        }

        public DeleteSeveralDialog(string heading, List<DBEpisode> episodes)
        {
            Text = "Delete several - " + heading;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(560, 360);

            Label intro = new Label
            {
                Text = "Tick the episodes to remove. Grey = you don't have the file (it's only listed online); red = the file has gone (renamed, moved or deleted).",
                Dock = DockStyle.Top, Height = 38, Padding = new Padding(8, 8, 8, 0)
            };
            mList.Columns.Add("Episode", 90);
            mList.Columns.Add("Title", 250);
            mList.Columns.Add("File", 390);
            foreach (DBEpisode ep in episodes.OrderBy(e => (int)e[DBOnlineEpisode.cSeasonIndex]).ThenBy(e => (int)e[DBOnlineEpisode.cEpisodeIndex]))
            {
                string file = ep[DBEpisode.cFilename];
                bool gone = FileGone(ep);
                string code = "S" + ((int)ep[DBOnlineEpisode.cSeasonIndex]).ToString("00") + "E" + ((int)ep[DBOnlineEpisode.cEpisodeIndex]).ToString("00");
                ListViewItem li = new ListViewItem(new[] { code, (string)ep[DBOnlineEpisode.cEpisodeName], string.IsNullOrEmpty(file) ? "(no file)" : gone ? "GONE: " + file : file }) { Tag = ep };
                if (gone) li.ForeColor = Color.Firebrick;
                else if (string.IsNullOrEmpty(file)) li.ForeColor = Color.Gray;
                mList.Items.Add(li);
            }
            mList.ItemChecked += (s, e) => Count();

            FlowLayoutPanel ticks = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4) };
            Button gone2 = new Button { Text = "Tick the ones whose files are gone", AutoSize = true };
            Button noFile = new Button { Text = "Tick the ones with no file", AutoSize = true };
            Button all = new Button { Text = "Tick all", AutoSize = true };
            Button none = new Button { Text = "Untick all", AutoSize = true };
            gone2.Click += (s, e) => TickWhere(ep => FileGone(ep));
            noFile.Click += (s, e) => TickWhere(ep => string.IsNullOrEmpty((string)ep[DBEpisode.cFilename]));
            all.Click += (s, e) => TickWhere(ep => true);
            none.Click += (s, e) => TickWhere(ep => false);
            ticks.Controls.AddRange(new Control[] { gone2, noFile, all, none, mCount });

            Panel how = new Panel { Dock = DockStyle.Bottom, Height = 54 };
            mLibraryOnly.Location = new Point(10, 6);
            mFilesToo.Location = new Point(10, 28);
            how.Controls.Add(mLibraryOnly);
            how.Controls.Add(mFilesToo);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            Button go = new Button { Text = "Delete the ticked ones", AutoSize = true };
            go.Click += (s, e) =>
            {
                Ticked = mList.CheckedItems.Cast<ListViewItem>().Select(i => (DBEpisode)i.Tag).ToList();
                if (Ticked.Count == 0) return;
                string q = AlsoFiles ? "Delete " + Ticked.Count + " episode(s) AND their files? Files are deleted for good."
                                     : "Remove " + Ticked.Count + " episode(s) from the library? (The files are not touched.)";
                if (MessageBox.Show(this, q, Text, MessageBoxButtons.OKCancel, AlsoFiles ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK) return;
                DialogResult = DialogResult.OK;
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(go);
            CancelButton = cancel;

            Controls.Add(mList);
            Controls.Add(ticks);
            Controls.Add(how);
            Controls.Add(buttons);
            Controls.Add(intro);
            Count();
        }

        private void TickWhere(Func<DBEpisode, bool> test)
        {
            mList.BeginUpdate();
            foreach (ListViewItem li in mList.Items) li.Checked = test((DBEpisode)li.Tag);
            mList.EndUpdate();
            Count();
        }

        private void Count()
        {
            mCount.Text = "   " + mList.CheckedItems.Count + " ticked of " + mList.Items.Count;
        }
    }
}
