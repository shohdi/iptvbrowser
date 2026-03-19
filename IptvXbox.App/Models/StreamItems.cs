using Newtonsoft.Json;
using System.Collections.Generic;

namespace IptvXbox.App.Models
{
    public sealed class LiveStreamItem
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("stream_type")]
        public string StreamType { get; set; }

        [JsonProperty("stream_id")]
        public long StreamId { get; set; }

        [JsonProperty("stream_icon")]
        public string StreamIcon { get; set; }

        [JsonProperty("category_id")]
        public string CategoryId { get; set; }

        [JsonProperty("container_extension")]
        public string ContainerExtension { get; set; }
    }

    public sealed class MovieItem
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("stream_type")]
        public string StreamType { get; set; }

        [JsonProperty("stream_id")]
        public long StreamId { get; set; }

        [JsonProperty("stream_icon")]
        public string StreamIcon { get; set; }

        [JsonProperty("category_id")]
        public string CategoryId { get; set; }

        [JsonProperty("container_extension")]
        public string ContainerExtension { get; set; }
    }

    public sealed class SeriesItem
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("series_id")]
        public long SeriesId { get; set; }

        [JsonProperty("cover")]
        public string Cover { get; set; }

        [JsonProperty("plot")]
        public string Plot { get; set; }

        [JsonProperty("category_id")]
        public string CategoryId { get; set; }
    }

    public sealed class SeriesInfoResponse
    {
        [JsonProperty("episodes")]
        public Dictionary<string, List<SeriesEpisode>> Episodes { get; set; } = new Dictionary<string, List<SeriesEpisode>>();

        public List<SeasonOption> GetSeasonOptions()
        {
            var seasons = new List<SeasonOption>();

            foreach (var pair in Episodes)
            {
                if (int.TryParse(pair.Key, out int seasonNumber))
                {
                    seasons.Add(new SeasonOption(seasonNumber, pair.Value ?? new List<SeriesEpisode>()));
                }
            }

            seasons.Sort((left, right) => left.SeasonNumber.CompareTo(right.SeasonNumber));
            return seasons;
        }
    }

    public sealed class SeriesEpisode
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("episode_num")]
        public int EpisodeNumber { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("container_extension")]
        public string ContainerExtension { get; set; }

        public override string ToString() => $"Episode {EpisodeNumber}: {Title}";
    }

    public sealed class SeasonOption
    {
        public SeasonOption(int seasonNumber, List<SeriesEpisode> episodes)
        {
            SeasonNumber = seasonNumber;
            Episodes = episodes ?? new List<SeriesEpisode>();
        }

        public int SeasonNumber { get; }

        public List<SeriesEpisode> Episodes { get; }

        public override string ToString() => $"Season {SeasonNumber}";
    }
}
