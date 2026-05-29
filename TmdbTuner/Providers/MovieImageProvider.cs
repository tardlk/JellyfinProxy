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

namespace Jellyfin.Plugin.TmdbTuner.Providers
{
    public class MovieImageProvider : IRemoteImageProvider
    {
        private TmdbApiClient Tmdb => Plugin.Instance.TmdbClient;

        public string Name => "TmdbTuner";

        public MovieImageProvider()
        {
            // Parameterless constructor
        }

        public bool Supports(BaseItem item) => item is Movie;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Logo };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var results = new List<RemoteImageInfo>();
            var config = Plugin.Instance?.Configuration;
            var imageHost = config?.TmdbImageHost;

            var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
            if (string.IsNullOrEmpty(tmdbId) || !int.TryParse(tmdbId, out var id))
                return results;

            var movie = await Tmdb.Client.GetMovieAsync(id, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (movie == null) return results;

            var language = item.PreferredMetadataLanguage ?? "en";

            // Poster
            if (movie.PosterPath != null)
            {
                var url = "https://image.tmdb.org/t/p/original" + movie.PosterPath;
                results.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    Language = language,
                    Url = Tmdb.RewriteImageUrl(url, imageHost)
                });
            }

            // Backdrops
            if (movie.Images?.Backdrops != null)
            {
                foreach (var bd in movie.Images.Backdrops)
                {
                    var url = "https://image.tmdb.org/t/p/original" + bd.FilePath;
                    results.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Backdrop,
                        Language = bd.Iso_639_1 ?? language,
                        Url = Tmdb.RewriteImageUrl(url, imageHost)
                    });
                }
            }

            // Logos
            if (movie.Images?.Logos != null)
            {
                foreach (var logo in movie.Images.Logos)
                {
                    var url = "https://image.tmdb.org/t/p/original" + logo.FilePath;
                    results.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Logo,
                        Language = logo.Iso_639_1 ?? language,
                        Url = Tmdb.RewriteImageUrl(url, imageHost)
                    });
                }
            }

            return results;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            // Jellyfin will download the image from the URL we returned.
            // Since the URL is already rewritten to the custom host, we don't need to do anything special.
            return Task.FromResult<HttpResponseMessage>(null);
        }
    }
}
