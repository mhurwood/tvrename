using System;
using System.Collections.Generic;
using System.Globalization;
using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;

namespace TVRename
{
    class DownloadKodiMetaData : DownloadIdentifier
    {
        private static List<string> doneNFO;

        public DownloadKodiMetaData() 
        {
            reset();
        }

        public override DownloadType GetDownloadType()
        {
            return DownloadType.downloadMetaData;
        }

        public override void notifyComplete(FileInfo file)
        {
            if (file.FullName.EndsWith(".nfo", true, new CultureInfo("en")))
            {
                DownloadKodiMetaData.doneNFO.Add(file.FullName);
            }
            base.notifyComplete(file);
        }

        public override ItemList ProcessShow(ShowItem si, bool forceRefresh)
        {
            // for each tv show, optionally write a tvshow.nfo file
            if (TVSettings.Instance.NFOShows)
            {
                ItemList TheActionList = new ItemList();
                FileInfo tvshownfo = FileHelper.FileInFolder(si.AutoAdd_FolderBase, "tvshow.nfo");

                bool needUpdate = !tvshownfo.Exists ||
                                  (si.TheSeries().Srv_LastUpdated > TimeZone.Epoch(tvshownfo.LastWriteTime)) ||
                    // was it written before we fixed the bug in <episodeguideurl> ?
                                  (tvshownfo.LastWriteTime.ToUniversalTime().CompareTo(new DateTime(2009, 9, 13, 7, 30, 0, 0, DateTimeKind.Utc)) < 0);

                bool alreadyOnTheList = DownloadKodiMetaData.doneNFO.Contains(tvshownfo.FullName);

                if ((forceRefresh || needUpdate) && !alreadyOnTheList)
                {
                    TheActionList.Add(new ActionNFO(tvshownfo, si));
                    DownloadKodiMetaData.doneNFO.Add(tvshownfo.FullName);
                }
                return TheActionList;

            }
            return base.ProcessShow(si, forceRefresh);
        }

        public override ItemList ProcessEpisode(ProcessedEpisode dbep, FileInfo filo,bool forceRefresh)
        {
            if (TVSettings.Instance.NFOEpisodes)
            {
                ItemList TheActionList = new ItemList();

                string fn = filo.RemoveExtension() + ".nfo";
                FileInfo nfo = FileHelper.FileInFolder(filo.Directory, fn);

                if (!nfo.Exists || (dbep.Srv_LastUpdated > TimeZone.Epoch(nfo.LastWriteTime)) || forceRefresh)
                {
                    //If we do not already have plans to put the file into place
                    if (!(DownloadKodiMetaData.doneNFO.Contains(nfo.FullName)))
                    {
                        TheActionList.Add(new ActionNFO(nfo, dbep));
                        doneNFO.Add(nfo.FullName);
                    }
                }
                return TheActionList;
            }
            return base.ProcessEpisode(dbep, filo, forceRefresh);
        }

        public sealed override void reset()
        {
            doneNFO = new List<String>();
        }

    }
}
