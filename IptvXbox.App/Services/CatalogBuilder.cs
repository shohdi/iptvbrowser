using IptvXbox.App.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IptvXbox.App.Services
{
    public static class CatalogBuilder
    {
        public static List<CatalogItem> Build(CatalogResponse response)
        {
            var liveCategories = response.LiveCategories.ToDictionary(category => category.CategoryId ?? string.Empty, category => category.CategoryName ?? "Live");
            var movieCategories = response.VodCategories.ToDictionary(category => category.CategoryId ?? string.Empty, category => category.CategoryName ?? "Movies");
            var seriesCategories = response.SeriesCategories.ToDictionary(category => category.CategoryId ?? string.Empty, category => category.CategoryName ?? "Series");

            var items = new List<CatalogItem>((response.Live?.Count ?? 0) + (response.Movies?.Count ?? 0) + (response.Series?.Count ?? 0));

            if (response.Live != null)
            {
                foreach (LiveStreamItem stream in response.Live)
                {
                    string category = ResolveCategory(liveCategories, stream.CategoryId, "Live");
                    items.Add(CreateItem(stream.Name, category, "Live", stream.StreamId, 0, stream.StreamIcon, stream.ContainerExtension));
                }
            }

            if (response.Movies != null)
            {
                foreach (MovieItem movie in response.Movies)
                {
                    string category = ResolveCategory(movieCategories, movie.CategoryId, "Movies");
                    items.Add(CreateItem(movie.Name, category, "Movie", movie.StreamId, 0, movie.StreamIcon, movie.ContainerExtension));
                }
            }

            if (response.Series != null)
            {
                foreach (SeriesItem series in response.Series)
                {
                    string category = ResolveCategory(seriesCategories, series.CategoryId, "Series");
                    string subtitle = string.IsNullOrWhiteSpace(series.Plot) ? $"Series - {category}" : $"Series - {category} - {series.Plot}";
                    items.Add(CreateItem(series.Name, category, "Series", 0, series.SeriesId, series.Cover, "mp4", subtitle));
                }
            }

            return items;
        }

        public static BrowseNode BuildBrowseTree(IEnumerable<CatalogItem> items)
        {
            var root = new BrowseNode
            {
                Title = "Browse Library",
                Subtitle = "Choose Live, Movies, or Series."
            };

            foreach (IGrouping<string, CatalogItem> kindGroup in items
                .GroupBy(item => item.StreamKind)
                .OrderBy(group => GetKindOrder(group.Key)))
            {
                string groupTitle = GetKindTitle(kindGroup.Key);
                var groupNode = new BrowseNode
                {
                    Title = groupTitle,
                    Subtitle = $"Open a {groupTitle.ToLowerInvariant()} category."
                };

                foreach (IGrouping<string, CatalogItem> categoryGroup in kindGroup
                    .GroupBy(item => item.CategoryName)
                    .OrderBy(group => group.Key))
                {
                    var categoryNode = new BrowseNode
                    {
                        Title = categoryGroup.Key,
                        Subtitle = $"{categoryGroup.Count()} items"
                    };

                    foreach (CatalogItem item in categoryGroup.OrderBy(entry => entry.SortName))
                    {
                        categoryNode.Cards.Add(new BrowseCard
                        {
                            Title = item.Name,
                            Subtitle = item.Subtitle,
                            ImageUrl = item.ImageUrl,
                            Badge = item.StreamKindLabel,
                            Kind = "item",
                            Item = item
                        });
                    }

                    groupNode.Cards.Add(new BrowseCard
                    {
                        Title = categoryGroup.Key,
                        Subtitle = $"{categoryGroup.Count()} items",
                        Badge = groupTitle,
                        Kind = "category",
                        TargetNode = categoryNode
                    });
                }

                root.Cards.Add(new BrowseCard
                {
                    Title = groupTitle,
                    Subtitle = $"{kindGroup.Count()} items",
                    Badge = "Section",
                    Kind = "group",
                    TargetNode = groupNode
                });
            }

            return root;
        }

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (char character in decomposed)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(character);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static CatalogItem CreateItem(
            string name,
            string category,
            string streamKind,
            long streamId,
            long seriesId,
            string imageUrl,
            string containerExtension,
            string subtitle = null)
        {
            string safeName = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();

            return new CatalogItem
            {
                Name = safeName,
                SortName = safeName,
                NormalizedName = Normalize(safeName),
                CategoryName = category,
                NormalizedCategory = Normalize(category),
                StreamKind = streamKind,
                StreamKindLabel = streamKind,
                StreamId = streamId,
                SeriesId = seriesId,
                ImageUrl = imageUrl,
                ContainerExtension = string.IsNullOrWhiteSpace(containerExtension) ? "mp4" : containerExtension,
                Subtitle = subtitle ?? $"{streamKind} - {category}"
            };
        }

        private static string ResolveCategory(Dictionary<string, string> categories, string categoryId, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(categoryId) && categories.TryGetValue(categoryId, out string categoryName) && !string.IsNullOrWhiteSpace(categoryName))
            {
                return categoryName;
            }

            return fallback;
        }

        private static int GetKindOrder(string streamKind)
        {
            switch (streamKind)
            {
                case "Live":
                    return 0;
                case "Movie":
                    return 1;
                case "Series":
                    return 2;
                default:
                    return 3;
            }
        }

        private static string GetKindTitle(string streamKind)
        {
            switch (streamKind)
            {
                case "Live":
                    return "Live";
                case "Movie":
                    return "Movies";
                case "Series":
                    return "Series";
                default:
                    return streamKind;
            }
        }
    }
}
