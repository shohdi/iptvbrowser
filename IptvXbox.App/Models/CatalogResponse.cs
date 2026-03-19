using Newtonsoft.Json;
using System.Collections.Generic;

namespace IptvXbox.App.Models
{
    public sealed class CatalogResponse
    {
        [JsonProperty("live_categories")]
        public List<CategoryItem> LiveCategories { get; set; } = new List<CategoryItem>();

        [JsonProperty("live")]
        public List<LiveStreamItem> Live { get; set; } = new List<LiveStreamItem>();

        [JsonProperty("vod_categories")]
        public List<CategoryItem> VodCategories { get; set; } = new List<CategoryItem>();

        [JsonProperty("movies")]
        public List<MovieItem> Movies { get; set; } = new List<MovieItem>();

        [JsonProperty("series_categories")]
        public List<CategoryItem> SeriesCategories { get; set; } = new List<CategoryItem>();

        [JsonProperty("series")]
        public List<SeriesItem> Series { get; set; } = new List<SeriesItem>();
    }
}
