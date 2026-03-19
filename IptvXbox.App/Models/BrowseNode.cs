using System.Collections.Generic;

namespace IptvXbox.App.Models
{
    public sealed class BrowseCard
    {
        public string Title { get; set; }

        public string Subtitle { get; set; }

        public string ImageUrl { get; set; }

        public string Badge { get; set; }

        public string Kind { get; set; }

        public CatalogItem Item { get; set; }

        public BrowseNode TargetNode { get; set; }
    }

    public sealed class BrowseNode
    {
        public string Title { get; set; }

        public string Subtitle { get; set; }

        public List<BrowseCard> Cards { get; } = new List<BrowseCard>();
    }
}
