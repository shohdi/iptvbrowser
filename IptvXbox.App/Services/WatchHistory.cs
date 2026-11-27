using IptvXbox.App.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Windows.Storage;

namespace IptvXbox.App.Services
{
    public sealed class WatchHistory
    {
        private const string ContainerName = "WatchHistory";
        private const int RecentLimit = 50;
        private readonly object _gate = new object();

        public static WatchHistory Current { get; } = new WatchHistory();

        private WatchHistory()
        {
        }

        internal static string GetProfileKey(ConnectionSettings connection)
        {
            if (!connection.IsComplete)
            {
                return null;
            }

            string server = IptvProviderClient.NormalizeServer(connection.Server);
            if (Uri.TryCreate(server, UriKind.Absolute, out Uri uri))
            {
                server = uri.AbsoluteUri.TrimEnd('/');
            }

            return HashKey($"{server}\n{connection.Username}");
        }

        internal IReadOnlyList<WatchHistoryEntry> GetRecent(string profileKey)
        {
            lock (_gate)
            {
                ApplicationDataContainer profile = GetProfile(profileKey, create: false);
                if (profile == null)
                {
                    return Array.Empty<WatchHistoryEntry>();
                }

                return profile.Values.Values.OfType<ApplicationDataCompositeValue>()
                    .OrderByDescending(record => (long)record["lastWatched"])
                    .Take(RecentLimit)
                    .Select(record => ReadEntry(profileKey, record))
                    .ToList();
            }
        }

        public TimeSpan? GetPosition(WatchHistoryEntry entry)
        {
            if (entry == null || entry.IsLive)
            {
                return null;
            }

            lock (_gate)
            {
                ApplicationDataContainer profile = GetProfile(entry.ProfileKey, create: false);
                return ReadPosition(GetRecord(profile, GetItemKey(entry)));
            }
        }

        public void RecordPlayback(WatchHistoryEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ProfileKey))
            {
                return;
            }

            lock (_gate)
            {
                ApplicationDataContainer profile = GetProfile(entry.ProfileKey, create: true);
                string key = GetItemKey(entry);
                var record = new ApplicationDataCompositeValue
                {
                    ["title"] = LimitText(entry.Title, 256),
                    ["subtitle"] = LimitText(entry.Subtitle, 256),
                    ["imageUrl"] = entry.ImageUrl?.Length <= 2048 ? entry.ImageUrl : string.Empty,
                    ["streamKind"] = entry.StreamKind,
                    ["streamId"] = entry.StreamId,
                    ["seriesId"] = entry.SeriesId,
                    ["extension"] = entry.ContainerExtension ?? string.Empty,
                    ["lastWatched"] = DateTime.UtcNow.Ticks
                };

                TimeSpan? position = entry.IsLive ? null : ReadPosition(GetRecord(profile, key));
                if (position.HasValue)
                {
                    record["position"] = position.Value.Ticks;
                }

                SaveRecord(profile, key, record);
            }
        }

        public void SavePosition(WatchHistoryEntry entry, TimeSpan position)
        {
            SetPosition(entry, position > TimeSpan.Zero ? position : (TimeSpan?)null);
        }

        public void CompletePlayback(WatchHistoryEntry entry) => SetPosition(entry, null);

        private void SetPosition(WatchHistoryEntry entry, TimeSpan? position)
        {
            if (entry == null || entry.IsLive)
            {
                return;
            }

            lock (_gate)
            {
                ApplicationDataContainer profile = GetProfile(entry.ProfileKey, create: false);
                string key = GetItemKey(entry);
                ApplicationDataCompositeValue record = GetRecord(profile, key);
                if (record == null || ReadPosition(record) == position)
                {
                    return;
                }

                if (position.HasValue)
                {
                    record["position"] = position.Value.Ticks;
                }
                else
                {
                    record.Remove("position");
                }

                SaveRecord(profile, key, record);
            }
        }

        private static ApplicationDataContainer GetProfile(string profileKey, bool create)
        {
            if (string.IsNullOrEmpty(profileKey))
            {
                return null;
            }

            ApplicationDataContainer settings = ApplicationData.Current.LocalSettings;
            if (create)
            {
                return settings.CreateContainer(ContainerName, ApplicationDataCreateDisposition.Always)
                    .CreateContainer(profileKey, ApplicationDataCreateDisposition.Always);
            }

            return settings.Containers.TryGetValue(ContainerName, out ApplicationDataContainer history) &&
                history.Containers.TryGetValue(profileKey, out ApplicationDataContainer profile) ? profile : null;
        }

        private static ApplicationDataCompositeValue GetRecord(ApplicationDataContainer profile, string key)
        {
            return profile != null && profile.Values.TryGetValue(key, out object value)
                ? value as ApplicationDataCompositeValue : null;
        }

        private static WatchHistoryEntry ReadEntry(string profileKey, ApplicationDataCompositeValue record)
        {
            var entry = new WatchHistoryEntry
            {
                ProfileKey = profileKey,
                Title = (string)record["title"],
                Subtitle = (string)record["subtitle"],
                ImageUrl = (string)record["imageUrl"],
                StreamKind = (string)record["streamKind"],
                StreamId = (string)record["streamId"],
                SeriesId = (long)record["seriesId"],
                ContainerExtension = (string)record["extension"]
            };
            entry.Position = entry.IsLive ? null : ReadPosition(record);
            return entry;
        }

        private static TimeSpan? ReadPosition(ApplicationDataCompositeValue record)
        {
            return record != null && record.TryGetValue("position", out object value) && value is long ticks && ticks > 0
                ? TimeSpan.FromTicks(ticks) : (TimeSpan?)null;
        }

        private static void SaveRecord(ApplicationDataContainer profile, string key, ApplicationDataCompositeValue record)
        {
            try
            {
                profile.Values[key] = record;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not save watch history ({ex.HResult:X8}).");
            }
        }

        private static string GetItemKey(WatchHistoryEntry entry) => HashKey($"{entry.StreamKind}/{entry.SeriesId}/{entry.StreamId}");

        private static string HashKey(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static string LimitText(string value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length > length ? value.Substring(0, length) : value;
        }
    }
}
