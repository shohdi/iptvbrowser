using IptvXbox.App.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace IptvXbox.App.Services
{
    public sealed class IptvProviderClient
    {
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public IptvProviderClient()
        {
            if (!Client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                Client.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/json,text/html,*/*");
            }
        }

        public async Task<CatalogResponse> FetchCatalogAsync(string server, string username, string password, CancellationToken cancellationToken)
        {
            string baseUrl = NormalizeServer(server);
            var tasks = new Dictionary<string, Task<string>>
            {
                ["live_categories"] = GetJsonAsync($"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&action=get_live_categories", cancellationToken),
                ["live"] = GetJsonAsync($"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&action=get_live_streams", cancellationToken),
                ["vod_categories"] = GetJsonAsync($"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&action=get_vod_categories", cancellationToken),
                ["movies"] = GetJsonAsync($"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&action=get_vod_streams", cancellationToken),
                ["series_categories"] = GetJsonAsync($"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&action=get_series_categories", cancellationToken),
                ["series"] = GetJsonAsync($"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&action=get_series", cancellationToken),
            };

            await Task.WhenAll(tasks.Values);

            return new CatalogResponse
            {
                LiveCategories = JsonConvert.DeserializeObject<List<CategoryItem>>(tasks["live_categories"].Result) ?? new List<CategoryItem>(),
                Live = JsonConvert.DeserializeObject<List<LiveStreamItem>>(tasks["live"].Result) ?? new List<LiveStreamItem>(),
                VodCategories = JsonConvert.DeserializeObject<List<CategoryItem>>(tasks["vod_categories"].Result) ?? new List<CategoryItem>(),
                Movies = JsonConvert.DeserializeObject<List<MovieItem>>(tasks["movies"].Result) ?? new List<MovieItem>(),
                SeriesCategories = JsonConvert.DeserializeObject<List<CategoryItem>>(tasks["series_categories"].Result) ?? new List<CategoryItem>(),
                Series = JsonConvert.DeserializeObject<List<SeriesItem>>(tasks["series"].Result) ?? new List<SeriesItem>(),
            };
        }

        public async Task<SeriesInfoResponse> FetchSeriesInfoAsync(string server, string username, string password, long seriesId, CancellationToken cancellationToken)
        {
            string baseUrl = NormalizeServer(server);
            string url = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&action=get_series_info&series_id={seriesId}";
            string json = await GetJsonAsync(url, cancellationToken);
            return JsonConvert.DeserializeObject<SeriesInfoResponse>(json) ?? new SeriesInfoResponse();
        }

        public string BuildStreamUrl(string server, string username, string password, CatalogItem item)
        {
            string baseUrl = NormalizeServer(server);

            if (item.StreamKind == "Live")
            {
                return $"{baseUrl}/live/{username}/{password}/{item.StreamId}.ts";
            }

            if (item.StreamKind == "Movie")
            {
                return $"{baseUrl}/movie/{username}/{password}/{item.StreamId}.{item.ContainerExtension}";
            }

            return $"{baseUrl}/series/{username}/{password}/{item.StreamId}.{item.ContainerExtension}";
        }

        public string BuildSeriesEpisodeUrl(string server, string username, string password, SeriesEpisode episode)
        {
            string baseUrl = NormalizeServer(server);
            string extension = string.IsNullOrWhiteSpace(episode.ContainerExtension) ? "mp4" : episode.ContainerExtension;
            return $"{baseUrl}/series/{username}/{password}/{episode.Id}.{extension}";
        }

        private static async Task<string> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (HttpResponseMessage response = await Client.SendAsync(request, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        private static string NormalizeServer(string server)
        {
            string normalized = (server ?? string.Empty).Trim();
            normalized = normalized.Replace("https://", "http://");
            return normalized.TrimEnd('/');
        }
    }
}
