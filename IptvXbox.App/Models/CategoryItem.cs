using Newtonsoft.Json;

namespace IptvXbox.App.Models
{
    public sealed class CategoryItem
    {
        [JsonProperty("category_id")]
        public string CategoryId { get; set; }

        [JsonProperty("category_name")]
        public string CategoryName { get; set; }
    }
}
