using System;

namespace IptvXbox.App.Models
{
    public sealed class WatchHistoryEntry
    {
        public string Title { get; internal set; }

        public string Subtitle { get; internal set; }

        public string ImageUrl { get; internal set; }

        public string StreamKind { get; internal set; }

        public TimeSpan? Position { get; internal set; }

        public bool IsLive => StreamKind == "Live";

        internal string ProfileKey { get; set; }

        internal string StreamId { get; set; }

        internal long SeriesId { get; set; }

        internal string ContainerExtension { get; set; }
    }
}
