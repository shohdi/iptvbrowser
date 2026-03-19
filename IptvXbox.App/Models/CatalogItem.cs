using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IptvXbox.App.Models
{
    public sealed class CatalogItem
    {
        public string Name { get; set; }

        public string SortName { get; set; }

        public string NormalizedName { get; set; }

        public string CategoryName { get; set; }

        public string NormalizedCategory { get; set; }

        public string Subtitle { get; set; }

        public string StreamKind { get; set; }

        public string StreamKindLabel { get; set; }

        public long StreamId { get; set; }

        public long SeriesId { get; set; }

        public string ImageUrl { get; set; }

        public string ContainerExtension { get; set; }

        public bool IsSeries => SeriesId > 0;
    }

    public sealed class AlphaGroup : ObservableCollection<CatalogItem>
    {
        public AlphaGroup(string key, IEnumerable<CatalogItem> items)
            : base(items)
        {
            Key = key;
        }

        public string Key { get; }

        public static IReadOnlyList<AlphaGroup> Create(IEnumerable<CatalogItem> items)
        {
            return items
                .GroupBy(item => string.IsNullOrWhiteSpace(item.SortName) ? "#" : item.SortName.Substring(0, 1).ToUpperInvariant())
                .OrderBy(group => group.Key)
                .Select(group => new AlphaGroup(group.Key, group))
                .ToList();
        }
    }
}
