using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WindowPlugins.GUITVSeries.Feedback;

namespace WindowPlugins.GUITVSeries.Configuration
{
    /// <summary>
    /// community fork 4.8.0.8: shows a scan couldn't match on its own are collected here, at the end, instead of a
    /// "Choose item" box popping up for each one in the middle of the scan (15 of them on the laptop, 7 Nov).
    /// Pick / Search / Skip each one, or Skip all. Skipped shows stay skipped (they're never asked about again
    /// until you pick them here or clear the list).
    /// </summary>
    public class NeedsHelpDialog : Form
    {
        public class Item
        {
            public string Name;
            public ChooseFromSelectionDescriptor Descriptor;
            public DBOnlineSeries Picked;
            public bool Skipped;
        }

        public const string SkippedOption = "CommunityFork_SkippedSeries";

        private readonly List<Item> mItems;
        private readonly ListView mList = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        private readonly Button mPick = new Button { Text = "Pick...", AutoSize = true };
        private readonly Button mSearch = new Button { Text = "Search...", AutoSize = true };
        private readonly Button mSkip = new Button { Text = "Skip", AutoSize = true };
        private readonly Button mSkipAll = new Button { Text = "Skip all the rest", AutoSize = true };
        private readonly Button mDone = new Button { Text = "Done", AutoSize = true, DialogResult = DialogResult.OK };

        public NeedsHelpDialog(List<Item> items)
        {
            mItems = items;
            Text = "Needs your help - shows the scan couldn't match";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 440);
            MinimumSize = new Size(560, 320);
            Label intro = new Label
            {
                Text = "These shows couldn't be matched to an online show by themselves. For each one:\r\n" +
                       "Pick = choose from what was found,  Search = try a different name,  Skip = leave it (it won't be asked about again).\r\n" +
                       "When you press Done, the picked ones are added to your library.",
                Dock = DockStyle.Top, Height = 58, Padding = new Padding(8, 8, 8, 0)
            };
            mList.Columns.Add("Show (from your files)", 280);
            mList.Columns.Add("Found", 60);
            mList.Columns.Add("What happens", 340);
            mList.Dock = DockStyle.Fill;
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), FlowDirection = FlowDirection.LeftToRight };
            buttons.Controls.AddRange(new Control[] { mPick, mSearch, mSkip, mSkipAll, mDone });
            Controls.Add(mList);
            Controls.Add(buttons);
            Controls.Add(intro);
            AcceptButton = mDone;
            mPick.Click += (s, e) => Pick(false);
            mSearch.Click += (s, e) => Pick(true);
            mList.DoubleClick += (s, e) => Pick(false);
            mSkip.Click += (s, e) => { Item it = Selected(); if (it != null) { it.Skipped = true; it.Picked = null; Refill(); } };
            mSkipAll.Click += (s, e) => { foreach (Item it in mItems.Where(x => x.Picked == null)) it.Skipped = true; Refill(); };
            mList.SelectedIndexChanged += (s, e) => Buttons();
            Refill();
            if (mList.Items.Count > 0) mList.Items[0].Selected = true;
        }

        private Item Selected() => mList.SelectedItems.Count == 1 ? mList.SelectedItems[0].Tag as Item : null;

        private void Buttons()
        {
            bool one = Selected() != null;
            mPick.Enabled = mSearch.Enabled = mSkip.Enabled = one;
        }

        private void Refill()
        {
            int sel = mList.SelectedIndices.Count > 0 ? mList.SelectedIndices[0] : 0;
            mList.BeginUpdate();
            mList.Items.Clear();
            foreach (Item it in mItems)
            {
                int found = it.Descriptor?.m_List?.Count(c => c.m_Tag is DBOnlineSeries) ?? 0;
                string what = it.Picked != null ? "add as: " + it.Picked.ToString() + " (ID " + it.Picked[DBOnlineSeries.cID] + ")"
                    : it.Skipped ? "skipped" : "waiting - not added yet";
                ListViewItem li = new ListViewItem(new[] { it.Name, found.ToString(), what }) { Tag = it };
                if (it.Picked != null) li.ForeColor = Color.DarkGreen;
                else if (it.Skipped) li.ForeColor = Color.Gray;
                mList.Items.Add(li);
            }
            mList.EndUpdate();
            if (mList.Items.Count > 0) mList.Items[Math.Min(sel, mList.Items.Count - 1)].Selected = true;
            Buttons();
        }

        private void Pick(bool searchFirst)
        {
            Item it = Selected();
            if (it == null) return;
            ChooseFromSelectionDescriptor d = it.Descriptor;
            string name = it.Name;
            for (int tries = 0; tries < 10; tries++)
            {
                if (searchFirst || d == null || d.m_List == null || !d.m_List.Any(c => c.m_Tag is DBOnlineSeries))
                {
                    string typed = Ask("Search for which name?", name);
                    if (string.IsNullOrWhiteSpace(typed)) return;
                    name = typed.Trim();
                    d = Search(name);
                    searchFirst = false;
                    if (d.m_List.Count == 0)
                    {
                        MessageBox.Show(this, "Nothing found for \"" + name + "\". Try another name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        searchFirst = true;
                        continue;
                    }
                }
                using (ChooseFromSelectionDialog dlg = new ChooseFromSelectionDialog(d))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    CItem chosen = dlg.SelectedItem;
                    if (chosen?.m_Tag is DBOnlineSeries)
                    {
                        it.Picked = (DBOnlineSeries)chosen.m_Tag;
                        it.Skipped = false;
                        it.Descriptor = d;
                        Refill();
                        return;
                    }
                    // typed a different name in the box: search for that
                    name = string.IsNullOrWhiteSpace(chosen?.m_sName) ? name : chosen.m_sName;
                    d = Search(name);
                }
            }
        }

        private ChooseFromSelectionDescriptor Search(string name)
        {
            Cursor = Cursors.WaitCursor;
            List<CItem> choices = new List<CItem>();
            try
            {
                GetSeries found = new GetSeries(name);
                HashSet<int> seen = new HashSet<int>();
                foreach (DBOnlineSeries s in found.Results)
                {
                    int id = s[DBOnlineSeries.cID];
                    if (!seen.Add(id)) continue;
                    choices.Add(new CItem(s[DBOnlineSeries.cPrettyName], "SeriesID: " + id + Environment.NewLine + s[DBOnlineSeries.cSummary], s));
                }
            }
            catch (Exception ex)
            {
                MPTVSeriesLog.Write("Needs your help: search for '" + name + "' failed: " + ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            return new ChooseFromSelectionDescriptor { m_sItemToMatchLabel = Translation.CFS_Local_Series, m_sItemToMatch = name, m_List = choices };
        }

        private string Ask(string question, string start)
        {
            using (Form f = new Form { Text = Text, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(420, 110), AutoScaleMode = AutoScaleMode.Font })
            {
                Label l = new Label { Text = question, Location = new Point(10, 10), AutoSize = true };
                TextBox t = new TextBox { Text = start, Location = new Point(10, 34), Width = 398 };
                Button ok = new Button { Text = "Search", DialogResult = DialogResult.OK, Location = new Point(252, 72), Width = 75 };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(333, 72), Width = 75 };
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
            }
        }

        // ---- the skipped list (kept in the TV Series database's options)

        public const string PendingOption = "CommunityFork_NeedsHelp";

        /// shows a scan in MediaPortal couldn't match - Configuration asks about them when it opens
        public static void AddPending(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                if (SkippedNames().Contains(name)) return;
                HashSet<string> set = PendingNames();
                if (set.Add(name.Trim()))
                {
                    DBOption.SetOptions(PendingOption, string.Join("|", set.Select(n => n.Replace("|", " ")).OrderBy(n => n).ToArray()));
                    MPTVSeriesLog.Write("\"" + name + "\" couldn't be matched by itself - Configuration will ask about it ('Needs your help')");
                }
            }
            catch
            {
            }
        }

        public static HashSet<string> PendingNames()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string v = DBOption.GetOptions(PendingOption);
                if (!string.IsNullOrEmpty(v)) foreach (string n in v.Split('|')) if (n.Trim().Length > 0) set.Add(n.Trim());
            }
            catch
            {
            }
            return set;
        }

        public static void ClearPending()
        {
            DBOption.SetOptions(PendingOption, "");
        }

        public static HashSet<string> SkippedNames()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string v = DBOption.GetOptions(SkippedOption);
                if (!string.IsNullOrEmpty(v)) foreach (string n in v.Split('|')) if (n.Trim().Length > 0) set.Add(n.Trim());
            }
            catch
            {
            }
            return set;
        }

        public static void SaveSkipped(HashSet<string> names)
        {
            DBOption.SetOptions(SkippedOption, string.Join("|", names.Select(n => n.Replace("|", " ")).OrderBy(n => n).ToArray()));
        }
    }
}
