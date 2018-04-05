// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 
namespace TVRename
{
    using System;
    using Alphaleonis.Win32.Filesystem;
    using System.Windows.Forms;

    public class ItemMissing : Item
    {
        public string TheFileNoExt;
        private string folder;
        public string Filename;

        public ItemMissing(ProcessedEpisode pe, string whereItShouldBeFolder, string expectedFilenameNoExt)
        {
            this.Episode = pe;
            this.TheFileNoExt = whereItShouldBeFolder + System.IO.Path.DirectorySeparatorChar + expectedFilenameNoExt;
            this.folder = whereItShouldBeFolder;
            this.Filename = expectedFilenameNoExt;
        }

        #region Item Members

        public override bool SameAs(Item o)
        {
            return (o is ItemMissing) && (string.Compare((o as ItemMissing).TheFileNoExt, this.TheFileNoExt) == 0);
        }

        public override int Compare(Item o)
        {
            ItemMissing miss = o as ItemMissing;
            //return (o == null || miss == null) ? 0 : (this.TheFileNoExt + this.Episode.Name).CompareTo(miss.TheFileNoExt + miss.Episode.Name);
            if (o == null || miss == null)
            {
                return 0;
            }

            if (!this.Episode.SI.ShowName.Equals(miss.Episode.SI.ShowName))
            {
                return this.Episode.SI.ShowName.CompareTo(miss.Episode.SI.ShowName);
            }

            if (!this.Episode.AppropriateSeasonNumber.Equals(miss.Episode.AppropriateSeasonNumber))
            {
                return this.Episode.AppropriateSeasonNumber.CompareTo(miss.Episode.AppropriateSeasonNumber);
            }

            return this.Episode.AppropriateEpNum.CompareTo(miss.Episode.AppropriateEpNum);
        }

        #endregion

        #region Item Members

        public override  IgnoreItem Ignore
        {
            get
            {
                if (string.IsNullOrEmpty(this.TheFileNoExt))
                    return null;
                return new IgnoreItem(this.TheFileNoExt);
            }
        }

        public override ListViewItem ScanListViewItem
        {
            get
            {
                ListViewItem lvi = new ListViewItem {
                                                        Text = this.Episode.SI.ShowName
                                                    };

                lvi.SubItems.Add(this.Episode.AppropriateSeasonNumber.ToString());
                lvi.SubItems.Add(this.Episode.NumsAsString());

                DateTime? dt = this.Episode.GetAirDateDT(true);
                if ((dt != null) && (dt.Value.CompareTo(DateTime.MaxValue)) != 0)
                    lvi.SubItems.Add(dt.Value.ToShortDateString());
                else
                    lvi.SubItems.Add("");

                lvi.SubItems.Add(this.folder);
                lvi.SubItems.Add(this.Filename);

                lvi.Tag = this;

                return lvi;
            }
        }

        public override string ScanListViewGroup => "lvgActionMissing";

        public override string TargetFolder
        {
            get
            {
                if (string.IsNullOrEmpty(this.TheFileNoExt))
                    return null;
                return new FileInfo(this.TheFileNoExt).DirectoryName;
            }
        }

        public override int IconNumber => 1;

        #endregion
    }
}
