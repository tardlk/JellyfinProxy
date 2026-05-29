using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TmdbTuner.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbTuner.Providers
{
    public class MovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
    {
        private TmdbApiClient Tmdb => Plugin.Instance.TmdbClient;
        private ILogger Logger => Plugin.Log;

        public string Name => "TmdbTuner";

        public MovieProvider()
        {
            // Parameterless constructor - providers are auto-discovered by Jellyfin
        }

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Movie>();

            var tmdbId = info.GetProviderId(MetadataProvider.Tmdb);
            if (string.IsNullOrEmpty(tmdbId))
                return result;

            if (!int.TryParse(tmdbId, out var id))
                return result;

            var movie = await Tmdb.Client.GetMovieAsync(id, info.MetadataLanguage,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (movie == null) return result;

            result.HasMetadata = true;
            result.Item = new Movie
            {
                Name = movie.Title,
                OriginalTitle = movie.OriginalTitle,
                Overview = movie.Overview,
                HomePageUrl = movie.Homepage,
                ProductionYear = movie.ReleaseDate?.Year,
                PremiereDate = movie.ReleaseDate,
                CommunityRating = (float?)movie.VoteAverage,
                Genres = movie.Genres?.Select(g => g.Name).ToArray(),
                Tagline = movie.Tagline,
                RunTimeTicks = movie.Runtime.HasValue
                    ? TimeSpan.FromMinutes(movie.Runtime.Value).Ticks
                    : null,
                ProductionLocations = movie.ProductionCountries?.Select(c => c.Name).ToArray()
            };

            // Studios
            if (movie.ProductionCompanies != null)
            {
                result.Item.SetStudios(movie.ProductionCompanies.Select(c => c.Name));
            }

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo,
            CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            var tmdbId = searchInfo.GetProviderId(MetadataProvider.Tmdb);
            if (!string.IsNullOrEmpty(tmdbId) && int.TryParse(tmdbId, out var id))
            {
                var movie = await Tmdb.Client.GetMovieAsync(id, searchInfo.MetadataLanguage,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (movie != null)
                {
                    results.Add(MapSearchResult(movie));
                    return results;
                }
            }

            // Search by name
            var searchResults = await Tmdb.Client.SearchMovieAsync(searchInfo.Name,
                searchInfo.MetadataLanguage, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (searchResults?.Results != null)
            {
                foreach (var sr in searchResults.Results)
                    results.Add(MapSearchResult(sr));
            }

            return results;
        }

        private RemoteSearchResult MapSearchResult(TMDbLib.Objects.Movies.Movie movie)
        {
            var result = new RemoteSearchResult
            {
                Name = movie.Title,
                Overview = movie.Overview,
                ProductionYear = movie.ReleaseDate?.Year,
                PremiereDate = movie.ReleaseDate,
                SearchProviderName = Name
            };

            result.SetProviderId(MetadataProvider.Tmdb, movie.Id.ToString());
            result.SetProviderId(MetadataProvider.Imdb, movie.ImdbId);

            if (movie.PosterPath != null)
                result.ImageUrl = "https://image.tmdb.org/t/p/w500" + movie.PosterPath;

            return result;
        }

        private RemoteSearchResult MapSearchResult(TMDbLib.Objects.Search.SearchMovie sr)
        {
            var result = new RemoteSearchResult
            {
                Name = sr.Title,
                Overview = sr.Overview,
                ProductionYear = sr.ReleaseDate?.Year,
                PremiereDate = sr.ReleaseDate,
                SearchProviderName = Name
            };

            result.SetProviderId(MetadataProvider.Tmdb, sr.Id.ToString());

            if (sr.PosterPath != null)
                result.ImageUrl = "https://image.tmdb.org/t/p/w500" + sr.PosterPath;

            return result;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return Task.FromResult<HttpResponseMessage>(null);
        }
    }
}
