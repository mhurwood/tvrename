using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using JetBrains.Annotations;
using DirectoryInfo = Alphaleonis.Win32.Filesystem.DirectoryInfo;
using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;

namespace TVRename.Forms
{
    public partial class DuplicateMovieFinder : Form
    {
        private static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();
        private readonly List<DuplicateMovie> dupMovies;
        private readonly TVDoc mDoc;
        private readonly UI mainUi;

        public DuplicateMovieFinder([NotNull] TVDoc doc, UI main)
        {
            InitializeComponent();
            dupMovies = new List<DuplicateMovie>();
            mDoc = doc;
            mainUi = main;
            Scan();
        }

        // ReSharper disable once InconsistentNaming
        private void UpdateUI()
        {
            olvDuplicates.SetObjects(dupMovies);
        }

        private void AddRcMenuItem(string label, EventHandler command)
        {
            ToolStripMenuItem tsi = new ToolStripMenuItem(label);
            tsi.Click += command;
            possibleMergedEpisodeRightClickMenu.Items.Add(tsi);
        }

        private void PossibleMergedEpisodeRightClickMenu_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            possibleMergedEpisodeRightClickMenu.Close();
        }

        private void BwScan_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bw = (BackgroundWorker)sender;
            int total = mDoc.FilmLibrary.Movies.Count();
            int current = 0;

            dupMovies.Clear();
            foreach (MovieConfiguration? movie in mDoc.FilmLibrary.Movies)
            {
                ProcessMovie(movie);

                bw.ReportProgress(100 * current++ / total, movie.ShowName);
            }
        }

        private void ProcessMovie(MovieConfiguration movie)
        {
            List<FileInfo> files = movie.Locations
                                .Select(s => new DirectoryInfo(s))
                                .Where(info => info.Exists)
                                .SelectMany(d => d.GetFiles())
                                .Where(f => f.IsMovieFile())
                                .Distinct()
                                .ToList();

            if (files.Count > 1)
            {
                DuplicateMovie duplicateMovie = new DuplicateMovie { Movie = movie, Files = files };
                dupMovies.Add(duplicateMovie);

                duplicateMovie.IsSample = files.Any(f => f.IsSampleFile());
                duplicateMovie.IsDeleted = files.Any(f => f.IsDeletedStubFile());

                if (files.Count == 2)
                {
                    duplicateMovie.IsDoublePart = FileHelper.IsDoublePartMovie(files[0], files[1]);
                }
            }
        }

        private void BwScan_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            pbProgress.Value = e.ProgressPercentage;
            lblStatus.Text = e.UserState.ToString();
        }

        private void BwScan_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            btnRefresh.Visible = true;
            pbProgress.Visible = false;
            lblStatus.Visible = false;
            if (olvDuplicates.IsDisposed)
            {
                return;
            }

            UpdateUI();
        }
        private void BtnRefresh_Click_1(object sender, EventArgs e)
        {
            Scan();
        }

        private void Scan()
        {
            btnRefresh.Visible = false;
            pbProgress.Visible = true;
            lblStatus.Visible = true;
            bwScan.RunWorkerAsync();
        }

        private void olvDuplicates_CellRightClick(object sender, BrightIdeasSoftware.CellRightClickEventArgs e)
        {
            if (e.Model is null)
            {
                return;
            }

            DuplicateMovie mlastSelected = (DuplicateMovie)e.Model;
            MovieConfiguration si = mlastSelected.Movie;

            possibleMergedEpisodeRightClickMenu.Items.Clear();

            AddRcMenuItem("Force Refresh", (o, args) =>
            {
                mainUi.ForceMovieRefresh(new List<MovieConfiguration> {si}, false);
                Update(mlastSelected);
            });
            AddRcMenuItem("Edit Movie", (o, args) =>
            {
                mainUi.EditMovie(si);
                Update(mlastSelected);
            });
            AddRcMenuItem("Choose Best", (o, args) => MergeItems(mlastSelected, mainUi));

            possibleMergedEpisodeRightClickMenu.Items.Add(new ToolStripSeparator());

            foreach (FileInfo? f in mlastSelected.Files)
            {
                AddRcMenuItem("Visit " + f.FullName, (o, args) =>
                {
                    Helpers.OpenFolderSelectFile(f.FullName);
                    Update(mlastSelected);
                });
            }
        }
        private void Update(DuplicateMovie duplicate)
        {
            if (dupMovies.Contains(duplicate))
            {
                dupMovies.Remove(duplicate);
            }
            ProcessMovie(duplicate.Movie);
            UpdateUI();
        }
        private void MergeItems(DuplicateMovie mlastSelected, UI ui)
        {
            foreach (var file1 in mlastSelected.Files)
            {
                foreach (FileInfo file2 in mlastSelected.Files)
                {
                    if (string.CompareOrdinal(file1.FullName, file2.FullName) > 0)
                    {
                        MergeConfigurationAndFiles(mlastSelected.Movie, file1, file2, ui);
                    }
                }
            }
            Update(mlastSelected);
        }

        private void MergeConfigurationAndFiles(MovieConfiguration mlastSelectedMovie, FileInfo file1, FileInfo file2, UI ui)
        {
            FileHelper.VideoComparison result = FileHelper.BetterQualityFile(file1, file2);

            FileHelper.VideoComparison newResult = result;

            switch (newResult)
            {
                case FileHelper.VideoComparison.secondFileBetter:
                    //remove first file and combine locations
                    UpgradeFile("System had identified to", file2, mlastSelectedMovie, file1);
                    break;
                case FileHelper.VideoComparison.cantTell:
                case FileHelper.VideoComparison.similar:
                    {
                        AskUserAboutFileReplacement(file1, file2, mlastSelectedMovie, ui);
                        return;
                    }
                //the other cases of the files being the same or the existing file being better are not enough to save the file
                case FileHelper.VideoComparison.firstFileBetter:
                case FileHelper.VideoComparison.same:
                    //remove second file and combine locations
                    UpgradeFile("System had identified to", file1, mlastSelectedMovie, file2);
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void AskUserAboutFileReplacement(FileInfo file1, FileInfo file2, [NotNull] MovieConfiguration pep, IDialogParent owner)
        {
            try
            {
                ChooseFile question = new ChooseFile(file1, file2);

                owner.ShowChildDialog(question);
                ChooseFile.ChooseFileDialogResult result = question.Answer;
                question.Dispose();

                switch (result)
                {
                    case ChooseFile.ChooseFileDialogResult.ignore:
                        LOGGER.Info($" User has selected keeping {file1.FullName} and {file2.FullName} and they will not be merged");
                        return;
                    case ChooseFile.ChooseFileDialogResult.left:
                        UpgradeFile("User selected to", file1, pep, file2);
                        return;
                    case ChooseFile.ChooseFileDialogResult.right:
                        UpgradeFile("User selected to", file2, pep, file1);
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (FileNotFoundException)
            {
                return;
            }
        }

        private static void UpgradeFile(string message, FileInfo keepFile, MovieConfiguration movie, FileInfo removeFile)
        {
            LOGGER.Info($"{message} remove {removeFile.FullName} as it is not as good quality than {keepFile.FullName}");
            try
            {
                if (movie.ManualLocations.Contains(removeFile.DirectoryName))
                {
                    movie.ManualLocations.Remove(removeFile.DirectoryName);
                }

                removeFile.Delete();

                if (removeFile.Directory.GetDirectories().Length > 0)
                {
                    return;
                }

                if (removeFile.Directory.GetFiles().Any(f => f.IsMovieFile()))
                {
                    return;
                }

                FileHelper.DoTidyUp(removeFile.Directory, TVSettings.Instance.Tidyup);
            }
            catch (FileNotFoundException)
            { //ignored}
            }
            catch (DirectoryNotFoundException)
            { //ignored}
            }
            catch (IOException)
            { //ignored}
            }
        }
    }



    public class DuplicateMovie
    {
        internal MovieConfiguration Movie;
        internal List<FileInfo> Files;
        public bool IsDoublePart;
        public bool IsSample;
        public bool IsDeleted;
        public string Name => Movie.ShowName;
        public string Filenames => Files.Select(info => info.FullName).ToCsv();
        public int NumberOfFiles => Files.Count;
    }
}

