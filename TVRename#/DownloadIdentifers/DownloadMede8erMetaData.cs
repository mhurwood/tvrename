using System.Collections.Generic;
using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;

namespace TVRename
{
    // ReSharper disable once InconsistentNaming
    class DownloadMede8erMetaData : DownloadIdentifier
    {
        private List<string> doneFiles;
        public DownloadMede8erMetaData()
        {
            reset();
        }

        public override DownloadType GetDownloadType()
        {
            return DownloadType.downloadMetaData;
        }

        public override ItemList ProcessShow(ShowItem si, bool forceRefresh)
        {
            if (TVSettings.Instance.Mede8erXML)
            {
                ItemList TheActionList = new ItemList();

                FileInfo tvshowxml = FileHelper.FileInFolder(si.AutoAdd_FolderBase, "series.xml");

                bool needUpdate = !tvshowxml.Exists ||
                                  (si.TheSeries().Srv_LastUpdated > TimeZone.Epoch(tvshowxml.LastWriteTime));

                if ((forceRefresh || needUpdate) && (!this.doneFiles.Contains(tvshowxml.FullName)))
                {
                    this.doneFiles.Add(tvshowxml.FullName);
                    TheActionList.Add(new ActionMede8erXML(tvshowxml, si));
                }


                //Updates requested by zakwaan@gmail.com on 18/4/2013
                FileInfo viewxml = FileHelper.FileInFolder(si.AutoAdd_FolderBase, "View.xml");
                if ((!viewxml.Exists) && (!this.doneFiles.Contains(viewxml.FullName)))
                {
                    this.doneFiles.Add(viewxml.FullName);
                    TheActionList.Add(new ActionMede8erViewXML(viewxml, si));
                }


                return TheActionList;
            }

            return base.ProcessShow(si, forceRefresh);
        }

        public override ItemList ProcessSeason(ShowItem si, string folder, int snum, bool forceRefresh)
        {
            if (TVSettings.Instance.Mede8erXML)
            {
                ItemList TheActionList = new ItemList();

                //Updates requested by zakwaan@gmail.com on 18/4/2013
                FileInfo viewxml = FileHelper.FileInFolder(folder, "View.xml");
                if (!viewxml.Exists) TheActionList.Add(new ActionMede8erViewXML(viewxml, si, snum));


                return TheActionList;
            }

            return base.ProcessSeason(si, folder, snum, forceRefresh);
        }

        public override ItemList ProcessEpisode(ProcessedEpisode dbep, FileInfo filo, bool forceRefresh)
        {
            if (TVSettings.Instance.Mede8erXML)
            {
                ItemList TheActionList = new ItemList();
                string fn = filo.RemoveExtension() + ".xml";
                FileInfo nfo = FileHelper.FileInFolder(filo.Directory, fn);

                if (forceRefresh || !nfo.Exists || (dbep.Srv_LastUpdated > TimeZone.Epoch(nfo.LastWriteTime)))
                    TheActionList.Add(new ActionMede8erXML(nfo, dbep));

                return TheActionList;

            }
            return base.ProcessEpisode(dbep, filo, forceRefresh);
        }

        public sealed override void reset()
        {
            this.doneFiles = new List<string>();
        }

    }
}
