// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using TMDbLib.Client;
using TMDbLib.Objects.Find;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.Trending;
using TVRename.Forms;
using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;

namespace TVRename.TMDB
{
    // ReSharper disable once InconsistentNaming
    public class LocalCache : MediaCache, iMovieSource
    {
        private static readonly TMDbClient Client = new TMDbClient("2dcfd2d08f80439d7ef5210f217b80b4");

        private UpdateTimeTracker latestUpdateTime;

        //We are using the singleton design pattern
        //http://msdn.microsoft.com/en-au/library/ff650316.aspx

        private static volatile LocalCache? InternalInstance;
        private static readonly object SyncRoot = new object();

        [NotNull]
        public static LocalCache Instance
        {
            get
            {
                if (InternalInstance is null)
                {
                    lock (SyncRoot)
                    {
                        InternalInstance ??= new LocalCache();
                    }
                }

                return InternalInstance;
            }
        }


        public void Setup(FileInfo? loadFrom, FileInfo cache, CommandLineArgs cla)
        {
            System.Diagnostics.Debug.Assert(cache != null);
            CacheFile = cache;

            //assume that the data is up to date (this will be overridden by the value in the XML if we have a prior install)
            //If we have no prior install then the app has no shows and is by definition up-to-date
            latestUpdateTime = new UpdateTimeTracker();

            LastErrorMessage = string.Empty;

            LoadOk = loadFrom is null || CachePersistor.LoadMovieCache(loadFrom, this);
        }

        public bool Connect(bool showErrorMsgBox) => true;

        public void SaveCache()
        {
            lock (MOVIE_LOCK)
            {
                CachePersistor.SaveCache(Movies, CacheFile, latestUpdateTime.LastSuccessfulServerUpdateTimecode());
            }
        }

        public bool EnsureUpdated(SeriesSpecifier s, bool bannersToo, bool showErrorMsgBox)
        {
            if (s.Provider != TVDoc.ProviderType.TMDB)
            {
                throw new SourceConsistencyException($"Asked to update {s.Name} from TVDB, but the Id is not for TMDB.", TVDoc.ProviderType.TMDB);
            }

            if (s.Type == MediaConfiguration.MediaType.movie)
            {
                return EnsureMovieUpdated(s.TmdbId, s.Name, showErrorMsgBox);
            }

            return false; //EnsureSeriesUpdated(s.TmdbId, s.Name, showErrorMsgBox);
        }

        private bool EnsureMovieUpdated(int  id, string name, bool showErrorMsgBox)
        {
            lock (MOVIE_LOCK)
            {
                if (Movies.ContainsKey(id) && !Movies[id].Dirty)
                {
                    return true;
                }
            }

            Say($"{name} from TMDB");
            try
            {
                CachedMovieInfo downloadedSi = DownloadMovieNow(id, showErrorMsgBox);

                if (downloadedSi.TmdbCode != id && id == -1)
                {
                    lock (MOVIE_LOCK)
                    {
                        Movies.TryRemove(-1, out _);
                    }
                }

                lock (MOVIE_LOCK)
                {
                    AddSeriesToCache(downloadedSi);
                }
            }
            catch (SourceConnectivityException conex)
            {
                LastErrorMessage = conex.Message;
                return true;
            }
            catch (SourceConsistencyException sce)
            {
                LOGGER.Error(sce.Message);
                LastErrorMessage = sce.Message;
                return true;
            }
            finally
            {
                SayNothing();
            }

            return true;
        }

        private void AddSeriesToCache([NotNull] CachedMovieInfo si)
        {
            int id = si.TmdbCode;
            lock (MOVIE_LOCK)
            {
                if (Movies.ContainsKey(id))
                {
                    Movies[id].Merge(si);
                }
                else
                {
                    Movies[id] = si;
                }
            }
        }

        public bool GetUpdates(bool showErrorMsgBox, CancellationToken cts, IEnumerable<SeriesSpecifier> ss)
        {
            Say("Validating TMDB cache");
            MarkPlaceHoldersDirty(ss);

            try
            {
                Say($"Updates list from TMDB since {latestUpdateTime.LastSuccessfulServerUpdateDateTime()}");

                long updateFromEpochTime = latestUpdateTime.LastSuccessfulServerUpdateTimecode();
                if (updateFromEpochTime == 0)
                {
                    MarkAllDirty();
                    latestUpdateTime.RegisterServerUpdate(DateTime.Now.ToUnixTime());
                    return true;
                }

                List<int> updates = Client.GetChangesMovies(cts, latestUpdateTime).Select(item => item.Id).Distinct().ToList();

                Say($"Processing {updates.Count()} updates from TMDB. From between {latestUpdateTime.LastSuccessfulServerUpdateDateTime()} and {latestUpdateTime.ProposedServerUpdateDateTime()}");
                foreach (int id in updates)
                {
                    if (!cts.IsCancellationRequested)
                    {
                        if (HasMovie(id))
                        {
                            CachedMovieInfo? x = GetMovie(id);
                            if (!(x is null))
                            {
                                LOGGER.Info(
                                    $"Identified that show with TMDB Id {id} {x.Name} should be updated.");

                                x.Dirty = true;
                            }
                        }
                    }
                    else
                    {
                        return true;
                    }
                }
                lock (MOVIE_LOCK)
                {
                    Say($"Identified {Movies.Values.Count(info => info.Dirty && !info.IsSearchResultOnly)} TMDB Movies need updating");
                    LOGGER.Info(Movies.Values.Where(info => info.Dirty && !info.IsSearchResultOnly).Select(info => info.Name).ToCsv);
                }
                return true;
            }
            catch (SourceConnectivityException conex)
            {
                LastErrorMessage = conex.Message;
                return false;
            }
            catch (SourceConsistencyException sce)
            {
                LOGGER.Error(sce.Message);
                LastErrorMessage = sce.Message;
                return false;
            }
            finally
            {
                SayNothing();
            }

        }

        private void MarkPlaceHoldersDirty(IEnumerable<SeriesSpecifier> ss)
        {
            foreach (SeriesSpecifier downloadShow in ss)
            {
                if (downloadShow.Type == MediaConfiguration.MediaType.tv)
                {
                    /*
                    if (!HasShow(downloadShow.TMDBId))
                    {
                        AddPlaceholderSeries(downloadShow);
                    }
                    else
                    {
                        var Show = GetShow(downloadShow.TMDBId);
                        if (Show?.IsSearchResultOnly??false)
                        {
                            Show.Dirty = true;
                        }
                    }
                */
                }
                else
                {
                    if (!HasMovie(downloadShow.TmdbId))
                    {
                        AddPlaceholderMovie(downloadShow);
                    }
                    else
                    {
                        CachedMovieInfo? movie = GetMovie(downloadShow.TmdbId);
                        movie?.UpgradeSearchResultToDirty();
                    }
                }
            }
        }

        private void MarkAllDirty()
        {
            lock (MOVIE_LOCK)
            {
                foreach (CachedMovieInfo m in Movies.Values)
                {
                    m.Dirty = true;
                }
            }
        }

        private void MarkPlaceholdersDirty()
        {
            lock (MOVIE_LOCK)
            {
                // anything with a srv_lastupdated of 0 should be marked as dirty
                // typically, this'll be placeholder cachedSeries
                foreach (CachedMovieInfo ser in Movies.Values.Where(ser => ser.SrvLastUpdated == 0))
                {
                    ser.Dirty = true;
                }
            }
        }

        private void AddPlaceholderSeries([NotNull] SeriesSpecifier ss)
            => AddPlaceholderSeries(ss.TvdbSeriesId, ss.TvMazeSeriesId,ss.TmdbId, ss.CustomLanguageCode);

        private void AddPlaceholderMovie([NotNull] SeriesSpecifier ss)
            => AddPlaceholderMovie(ss.TvdbSeriesId, ss.TvMazeSeriesId, ss.TmdbId, ss.CustomLanguageCode);


        public void UpdatesDoneOk()
        {
            // call when all downloading and updating is done.  updates local Srv_Time with the tentative
            // new_srv_time value.
            latestUpdateTime.RecordSuccessfulUpdate();
        }

        public CachedMovieInfo? GetMovie(PossibleNewMovie show, bool showErrorMsgBox)
        {
            Search(show.RefinedHint, showErrorMsgBox);

            string showName = show.RefinedHint;

            if (string.IsNullOrEmpty(showName))
            {
                return null;
            }

            showName = showName.ToLower();

            List<CachedMovieInfo> matchingShows = GetSeriesDictMatching(showName).Values.ToList();

            if (matchingShows.Count == 0)
            {
                return null;
            }

            if (matchingShows.Count == 1)
            {
                return matchingShows.First();
            }

            var exactMatchingShows = matchingShows
                .Where(info => info.Name.CompareName().Equals(showName, StringComparison.InvariantCultureIgnoreCase)).ToList();

            if (exactMatchingShows.Count == 0)
            {
                return null;
            }

            if (exactMatchingShows.Count == 1)
            {
                return exactMatchingShows.First();
            }

            if (show.PossibleYear is null)
            {
                return null;
            }

            var exactMatchingShowsWithYear = exactMatchingShows
                .Where(info => info.Year==show.PossibleYear).ToList();

            if (exactMatchingShowsWithYear.Count == 0)
            {
                return null;
            }

            if (exactMatchingShowsWithYear.Count == 1)
            {
                return exactMatchingShowsWithYear.First();
            }

            return null;
        }

        [NotNull]
        private Dictionary<int, CachedMovieInfo> GetSeriesDictMatching(string testShowName)
        {
            Dictionary<int, CachedMovieInfo> matchingSeries = new Dictionary<int, CachedMovieInfo>();

            testShowName = testShowName.CompareName();

            if (string.IsNullOrEmpty(testShowName))
            {
                return matchingSeries;
            }

            lock (MOVIE_LOCK)
            {
                foreach (KeyValuePair<int, CachedMovieInfo> kvp in Movies)
                {
                    string show = kvp.Value.Name.CompareName();

                    if (show.Contains(testShowName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        //We have a match
                        matchingSeries.Add(kvp.Key, kvp.Value);
                    }
                }
            }
            return matchingSeries;
        }

        public CachedMovieInfo? GetMovie(int? id)
        {
            if (!id.HasValue)
            {
                return null;
            }
            lock (MOVIE_LOCK)
            {
                return HasMovie(id.Value) ? Movies[id.Value] : null;
            }
        }

        public bool HasMovie(int id)
        {
            lock (MOVIE_LOCK)
            {
                return Movies.ContainsKey(id);
            }
        }


        public void Tidy(ICollection<MovieConfiguration> libraryValues)
        {
            // remove any shows from cache that aren't in My Movies
            List<int> removeList = new List<int>();

            lock (MOVIE_LOCK)
            {
                foreach (KeyValuePair<int, CachedMovieInfo> kvp in Movies)
                {
                    bool found = libraryValues.Any(si => si.TmdbCode == kvp.Key);
                    if (!found)
                    {
                        removeList.Add(kvp.Key);
                    }
                }

                foreach (int i in removeList)
                {
                    ForgetMovie(i);
                }
            }
        }

        public void ForgetEverything()
        {
            lock (MOVIE_LOCK)
            {
                Movies.Clear();
            }

            SaveCache();
            //All cachedSeries will be forgotten and will be fully refreshed, so we'll only need updates after this point
            latestUpdateTime.Reset();
            LOGGER.Info($"Forget everything, so we assume we have TMDB updates until {latestUpdateTime}");
        }

        public void ForgetMovie(int id)
        {
            lock (MOVIE_LOCK)
            {
                if (Movies.ContainsKey(id))
                {
                    Movies.TryRemove(id, out _);
                }
            }
        }

        public void ForgetMovie(int tvdb,int tvmaze,int tmdb, bool makePlaceholder, bool useCustomLanguage, string? langCode)
        {
            lock (MOVIE_LOCK)
            {
                if (Movies.ContainsKey(tmdb))
                {
                    Movies.TryRemove(tmdb, out CachedMovieInfo _);
                    if (makePlaceholder)
                    {
                        if (useCustomLanguage && langCode.HasValue())
                        {
                            AddPlaceholderSeries(tvdb,tvmaze,tmdb, langCode!);
                        }
                        else
                        {
                            AddPlaceholderSeries(tvdb, tvmaze,tmdb);
                        }
                    }
                }
                else
                {
                    if (tmdb > 0 && makePlaceholder)
                    {
                        AddPlaceholderSeries(tvdb, tvmaze,tmdb);
                    }
                }
            }
        }

        public void AddPoster(int seriesId, IEnumerable<Banner> @select)
        {
            throw new NotImplementedException();
        }

        private void AddPlaceholderSeries(int tvdb, int tvmaze,int tmdb) 
        {
            lock (MOVIE_LOCK)
            {
                Movies[tmdb] = new CachedMovieInfo(tvdb,tvmaze,tmdb) { Dirty = true };
            }
        }

        private void AddPlaceholderSeries(int tvdb, int tvmaze, int tmdb, string customLanguageCode)
        {
            lock (MOVIE_LOCK)
            {
                Movies[tmdb] = new CachedMovieInfo(tvdb, tvmaze, tmdb, customLanguageCode) {Dirty = true};
            }
        }

        private void AddPlaceholderMovie(int tvdb, int tvmaze, int tmdb, string customLanguageCode)
        {
            lock (MOVIE_LOCK)
            {
                Movies[tmdb] = new CachedMovieInfo(tvdb, tvmaze, tmdb, customLanguageCode) { Dirty = true };
            }
        }

        public void Update(CachedMovieInfo si)
        {
            lock (MOVIE_LOCK)
            {
                Movies[si.TmdbCode] = si;
            }
        }

        public void LatestUpdateTimeIs(string time)
        {
            latestUpdateTime.Load(time);
            LOGGER.Info($"Loaded file with updates until {latestUpdateTime.LastSuccessfulServerUpdateDateTime()}");
        }

        public Language PreferredLanguage => throw new NotImplementedException();

        public ConcurrentDictionary<int,CachedMovieInfo> CachedData
        {
            get {
                lock (MOVIE_LOCK)
                {
                    return Movies;
                }
            }
        }

        public Language GetLanguageFromCode(string customLanguageCode) => throw new NotImplementedException();

        public CachedMovieInfo? GetMovieAndDownload(int id, bool showErrorMsgBox) => HasMovie(id)
            ? CachedData[id]
            : DownloadMovieNow(id,showErrorMsgBox);

        internal CachedMovieInfo DownloadMovieNow(int id, bool showErrorMsgBox)
        {
            Movie downloadedMovie = Client.GetMovieAsync(id, MovieMethods.ExternalIds|MovieMethods.Images|MovieMethods.AlternativeTitles|MovieMethods.ReleaseDates |MovieMethods.Changes|MovieMethods.Videos|MovieMethods.Credits).Result;
            if (downloadedMovie is null)
            {
                throw new ShowNotFoundException(id,"TMDB no longer has this movie",TVDoc.ProviderType.TMDB,TVDoc.ProviderType.TMDB);
            }
            CachedMovieInfo m = new CachedMovieInfo
            {
                Imdb = downloadedMovie.ExternalIds.ImdbId,
                TmdbCode = downloadedMovie.Id,
                Name = downloadedMovie.Title,
                Runtime = downloadedMovie.Runtime.ToString(),
                FirstAired = downloadedMovie.ReleaseDate,
                Genres = downloadedMovie.Genres.Select(genre => genre.Name).ToList(),
                Overview = downloadedMovie.Overview,
                Network = downloadedMovie.ProductionCompanies.FirstOrDefault()?.Name,
                Status = downloadedMovie.Status,
                ShowLanguage= downloadedMovie.OriginalLanguage,
                SiteRating = (float)downloadedMovie.VoteAverage,
                SiteRatingVotes = downloadedMovie.VoteCount,
                PosterUrl = ImageURL(downloadedMovie.PosterPath),
                SrvLastUpdated = DateTime.UtcNow.Date.ToUnixTime(),
                CollectionName = downloadedMovie.BelongsToCollection?.Name,
                CollectionId = downloadedMovie.BelongsToCollection?.Id,
                TagLine = downloadedMovie.Tagline,
                TwitterId=downloadedMovie.ExternalIds.TwitterId,
                InstagramId = downloadedMovie.ExternalIds.InstagramId,
                FacebookId=downloadedMovie.ExternalIds.InstagramId,
                FanartUrl = ImageURL(downloadedMovie.BackdropPath),
                ContentRating = GetCertification(downloadedMovie, "AU") ?? GetCertification(downloadedMovie, "US") ?? string.Empty,// todo allow user to choose
                OfficialUrl =downloadedMovie.Homepage,
                TrailerUrl = GetYouTubeUrl(downloadedMovie),
                Dirty = false,
            };

            foreach (var s in downloadedMovie.AlternativeTitles.Titles.Select(title => title.Title))
            {
                m.AddAlias(s);
            }
            foreach (var s in downloadedMovie.Credits.Cast)
            {
                m.AddActor(new Actor(s.Id,s.ProfilePath,s.Name,s.Character,s.CastId,s.Order));
            }
            foreach (var s in downloadedMovie.Credits.Crew)
            {
                m.AddCrew(new Crew(s.Id, s.ProfilePath, s.Name,  s.Job, s.Department, s.CreditId));
            }

            File(m);

            return m;
        }

        private string GetYouTubeUrl(Movie downloadedMovie)
        {
            string yid = downloadedMovie.Videos.Results.Where(video => video.Type == "Trailer" && video.Site == "YouTube").OrderByDescending(v => v.Size).Select(video => video.Key).FirstOrDefault() ?? string.Empty;
            return yid.HasValue() ? $"https://www.youtube.com/watch?v={yid}" : string.Empty;
        }

        private string? GetCertification(Movie downloadedMovie, string country)
        {
            return downloadedMovie.ReleaseDates.Results
                .Where(rel => rel.Iso_3166_1 == country)
                .Select(rel => rel.ReleaseDates.First().Certification)
                .FirstOrDefault();
        }

        public void Search(string text, bool showErrorMsgBox)
        {
            SearchContainer<SearchMovie> results = Client.SearchMovieAsync(text).Result;
            LOGGER.Info($"Got {results.Results.Count:N0} of {results.TotalResults:N0} results searching for {text}");
            foreach (SearchMovie result in results.Results)
            {
                File(result);
                DownloadMovieNow(result.Id, showErrorMsgBox);
            }
        }

        private CachedSeriesInfo File(SearchTv result)
        {
            CachedSeriesInfo m = new CachedSeriesInfo
            {
                TmdbCode = result.Id,
                Name = result.Name,
                FirstAired = result.FirstAirDate,
                Overview = result.Overview,
                //Network = result.ProductionCompanies.FirstOrDefault()?.Name,
                //Status = result.Status,
                ShowLanguage = result.OriginalLanguage,
                SiteRating = (float)result.VoteAverage,
                SiteRatingVotes = result.VoteCount,
                PosterUrl = ImageURL(result.PosterPath),
                IsSearchResultOnly = true,
                Dirty = false,
            };

            File(m);
            return m;
        }

        private CachedMovieInfo File(SearchMovie result)
        {
            CachedMovieInfo m = new CachedMovieInfo
            {
                TmdbCode = result.Id,
                Name = result.Title,
                FirstAired = result.ReleaseDate,
                Overview = result.Overview,
                ShowLanguage = result.OriginalLanguage,
                SiteRating = (float)result.VoteAverage,
                SiteRatingVotes = result.VoteCount,
                PosterUrl = ImageURL(result.PosterPath),
                FanartUrl = ImageURL(result.BackdropPath),
                IsSearchResultOnly = true,
                Dirty = false,
            };


            File(m);
            return m;
        }

        static string? ImageURL(string source)
        {
            if (source.HasValue())
            {
                return "https://image.tmdb.org/t/p/w600_and_h900_bestv2" + source;
            }

            return null;
        }

        public CachedMovieInfo? LookupMovieByImdb(string imdbToTest, bool showErrorMsgBox)
        {
            var results = Client.FindAsync(FindExternalSource.Imdb, imdbToTest).Result;
            LOGGER.Info($"Got {results.MovieResults.Count:N0} results searching for {imdbToTest}");
            foreach (SearchMovie result in results.MovieResults)
            {
                DownloadMovieNow(result.Id, showErrorMsgBox); 
            }

            if (results.MovieResults.Count == 0)
            {
                return null;
            }

            if (results.MovieResults.Count == 1)
            {
                lock (MOVIE_LOCK)
                {
                    return Movies[results.MovieResults.First().Id];
                }
            }

            return null;
        }


        public int? LookupTvbdIdByImdb(string imdbToTest, bool showErrorMsgBox)
        {
            var results = Client.FindAsync(FindExternalSource.Imdb, imdbToTest).Result;
            LOGGER.Info($"Got {results.TvResults.Count:N0} results searching for {imdbToTest}");


            if (results.TvResults.Count == 0)
            {
                return null;
            }

            if (results.TvResults.Count == 1)
            {
                return results.TvResults.First().Id;
            }

            return null;
        }

        private void File(CachedMovieInfo cachedMovie)
        {
            lock (MOVIE_LOCK)
            {
                if (Movies.ContainsKey(cachedMovie.TmdbCode))
                {
                    Movies[cachedMovie.TmdbCode].Merge(cachedMovie);
                }
                else
                {
                    Movies[cachedMovie.TmdbCode] = cachedMovie;
                }
            }
        }

        private void File(CachedSeriesInfo s)
        {
            lock (SERIES_LOCK)
            {
                if (Series.ContainsKey(s.TmdbCode))
                {
                    Series[s.TmdbCode].Merge(s);
                }
                else
                {
                    Series[s.TmdbCode] = s;
                }
            }
        }

        public Dictionary<int, CachedMovieInfo> GetMovieIdsFromCollection(int collectionId)
        {
            var returnValue = new Dictionary<int, CachedMovieInfo>();
            TMDbLib.Objects.Collections.Collection collection = Client.GetCollectionAsync(collectionId).Result;
            if (collection == null)
            {
                return returnValue;
            }

            foreach (var m in collection.Parts)
            {
                int id = m.Id;
                CachedMovieInfo info = File(m);
                returnValue.Add(id, info);
            }

            return returnValue;
        }

        public CachedMovieInfo? LookupMovieByTvdb(int tvdbId, bool showErrorMsgBox)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<CachedMovieInfo> ServerAccuracyCheck()
        {
            Say("TMDB Accuracy Check running");
            TmdbAccuracyCheck check = new TmdbAccuracyCheck(this);
            lock (MOVIE_LOCK)
            {
                foreach (CachedMovieInfo si in Movies.Values.Where(info => !info.IsSearchResultOnly).OrderBy(s => s.Name).ToList())
                {
                    check.ServerAccuracyCheck(si);
                }
            }

            foreach (string issue in check.Issues)
            {
                LOGGER.Warn(issue);
            }

            SayNothing();
            return check.ShowsToUpdate;
        }

        public async Task<Recomendations> GetRecommendations(TVDoc mDoc, BackgroundWorker sender, List<ShowConfiguration> shows)
        {
            string lang = "en";
            int total = shows.Count;
            int current = 0;
            Task<SearchContainer<SearchTv>> topRated = Client.GetTvShowTopRatedAsync();
            Task<SearchContainer<SearchTv>> trending = Client.GetTrendingTvAsync(TimeWindow.Week);
            await topRated;
            await trending;

            Recomendations returnValue = new Recomendations();

            foreach (var top in topRated.Result.Results)
            {
                File(top);
                returnValue.AddTopRated(top.Id);
            }
            foreach (var top in trending.Result.Results)
            {
                File(top);
                returnValue.AddTrending(top.Id);
            }


            foreach (var arg in shows)
            {
                try
                {
                    AddRecommendationsFrom(arg, returnValue);

                    sender.ReportProgress(100 * current++ / total, arg.CachedShow?.Name);
                }
                catch
                {
                    //todo record and resolve /retry errors
                }
            }

            return returnValue;
        }

        private void AddRecommendationsFrom(ShowConfiguration arg, Recomendations returnValue)
        {
            if (arg.TmdbCode == 0)
            {
                string? imdb = arg.CachedShow?.Imdb;
                if (!imdb.HasValue())
                {
                    return;
                }

                int? tmdbcode = LookupTvbdIdByImdb(imdb!, false);
                if (!tmdbcode.HasValue)
                {
                    return;
                }

                arg.TmdbCode = tmdbcode.Value;
            }

            var related = Client.GetTvShowRecommendationsAsync(arg.TmdbCode);
            var similar = Client.GetTvShowSimilarAsync(arg.TmdbCode);

            Task.WaitAll(related, similar);
            if (related.Result != null)
            {
                foreach (var s in related.Result.Results)
                {
                    File(s);
                    returnValue.AddRelated(s.Id, arg);
                }
            }

            if (similar.Result != null)
            {
                foreach (var s in similar.Result.Results)
                {
                    File(s);
                    returnValue.AddSimilar(s.Id, arg);
                }
            }
        }

        public async Task<Recomendations> GetRecommendations(TVDoc mDoc, BackgroundWorker sender, List<MovieConfiguration> movies)
        {
            const string lang = "en"; //todo make work with multi language
            int total = movies.Count;
                int current = 0;
            Task<SearchContainer<SearchMovie>> topRated = Client.GetMovieTopRatedListAsync(lang);
            Task<SearchContainer<SearchMovie>> trending = Client.GetTrendingMoviesAsync(TimeWindow.Week);
            await topRated;
            await trending;

            Recomendations returnValue = new Recomendations();

            foreach (var top in topRated.Result.Results)
            {
                File(top);
                returnValue.AddTopRated(top.Id);
            }
            foreach (var top in trending.Result.Results)
            {
                File(top);
                returnValue.AddTrending(top.Id);
            }


            foreach (var arg in movies)
            {
                try
                {
                    var related = Client.GetMovieRecommendationsAsync(arg.TmdbCode);
                    var similar = Client.GetMovieSimilarAsync(arg.TmdbCode);

                    Task.WaitAll(related, similar);
                    foreach (var movie in related.Result.Results)
                    {
                        File(movie);
                        returnValue.AddRelated(movie.Id, arg);
                    }


                    foreach (var movie in similar.Result.Results)
                    {
                        File(movie);
                        returnValue.AddSimilar(movie.Id, arg);

                    }

                    sender.ReportProgress(100 * current++ / total, arg.CachedMovie?.Name);
                }
                catch
                {
                    //todo - record error, retry etc
                }
            }


            //var related = movies.Select(arg => (arg.TmdbCode,Client.GetMovieRecommendationsAsync(arg.TmdbCode))).ToList();
            //var similar = movies.Select(arg => (arg.TmdbCode,Client.GetMovieSimilarAsync(arg.TmdbCode))).ToList();

            //Task.WaitAll(related.Select(tuple => tuple.Item2).ToArray());
            //Task.WaitAll(similar.Select(tuple => tuple.Item2).ToArray());





            return returnValue;
        }
    }
}