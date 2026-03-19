using IptvXbox.App.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace IptvXbox.App.Services
{
    public sealed class AppSession : INotifyPropertyChanged
    {
        private const string ServerSettingKey = "iptv.server";
        private const string UsernameSettingKey = "iptv.username";
        private const string PasswordSettingKey = "iptv.password";

        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;
        private readonly IptvProviderClient _providerClient = new IptvProviderClient();
        private readonly Dictionary<string, SeriesInfoResponse> _seriesCache = new Dictionary<string, SeriesInfoResponse>();
        private ConnectionSettings _connection = new ConnectionSettings();
        private CatalogResponse _catalog = new CatalogResponse();
        private List<CatalogItem> _allItems = new List<CatalogItem>();
        private string _statusMessage = "Ready. Load a local file or refresh from the API.";
        private bool _isCatalogLoaded;

        public static AppSession Current { get; } = new AppSession();

        public event PropertyChangedEventHandler PropertyChanged;

        public ConnectionSettings Connection
        {
            get => _connection;
            private set
            {
                _connection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCredentials));
            }
        }

        public CatalogResponse Catalog
        {
            get => _catalog;
            private set
            {
                _catalog = value ?? new CatalogResponse();
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<CatalogItem> AllItems => _allItems;

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsCatalogLoaded
        {
            get => _isCatalogLoaded;
            private set
            {
                _isCatalogLoaded = value;
                OnPropertyChanged();
            }
        }

        public bool HasCredentials => Connection.IsComplete;

        private AppSession()
        {
            LoadSavedConnectionSettings();
        }

        public void LoadSavedConnectionSettings()
        {
            IPropertySet values = _localSettings.Values;
            Connection = new ConnectionSettings
            {
                Server = values.ContainsKey(ServerSettingKey) ? values[ServerSettingKey] as string ?? string.Empty : string.Empty,
                Username = values.ContainsKey(UsernameSettingKey) ? values[UsernameSettingKey] as string ?? string.Empty : string.Empty,
                Password = values.ContainsKey(PasswordSettingKey) ? values[PasswordSettingKey] as string ?? string.Empty : string.Empty
            };
        }

        public bool TryUpdateConnection(string server, string username, string password, out string errorMessage)
        {
            string cleanServer = (server ?? string.Empty).Trim();
            string cleanUsername = (username ?? string.Empty).Trim();
            string cleanPassword = password ?? string.Empty;

            _localSettings.Values[ServerSettingKey] = cleanServer;
            _localSettings.Values[UsernameSettingKey] = cleanUsername;
            _localSettings.Values[PasswordSettingKey] = cleanPassword;

            Connection = new ConnectionSettings
            {
                Server = cleanServer,
                Username = cleanUsername,
                Password = cleanPassword
            };

            if (string.IsNullOrWhiteSpace(cleanServer) || string.IsNullOrWhiteSpace(cleanUsername) || string.IsNullOrWhiteSpace(cleanPassword))
            {
                errorMessage = "Enter server, username, and password first.";
                StatusMessage = errorMessage;
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public async Task LoadInitialLocalDataAsync()
        {
            if (IsCatalogLoaded)
            {
                return;
            }

            try
            {
                StorageFile localFile = await StorageFile.GetFileFromPathAsync(Path.Combine(Package.Current.InstalledLocation.Path, "channels.json"));
                await LoadCatalogFromFileAsync(localFile);
            }
            catch
            {
                StatusMessage = "Ready. Load a local file or refresh from the API.";
            }
        }

        public async Task RefreshFromApiAsync(CancellationToken cancellationToken)
        {
            if (!HasCredentials)
            {
                throw new InvalidOperationException("Enter server, username, and password first.");
            }

            StatusMessage = "Refreshing from IPTV API...";
            CatalogResponse response = await _providerClient.FetchCatalogAsync(
                Connection.Server,
                Connection.Username,
                Connection.Password,
                cancellationToken);

            await SetCatalogAsync(response);
            StatusMessage = $"Loaded {_allItems.Count:N0} items from the API.";
        }

        public async Task LoadCatalogFromFileAsync(StorageFile file)
        {
            StatusMessage = "Loading channels.json...";
            string json = await FileIO.ReadTextAsync(file);
            var response = JsonConvert.DeserializeObject<CatalogResponse>(json) ?? new CatalogResponse();
            await SetCatalogAsync(response);
            StatusMessage = $"Loaded {_allItems.Count:N0} items from channels.json.";
        }

        public IReadOnlyList<AlphaGroup> FilterItems(string query, string selectedType, string sortMode)
        {
            IEnumerable<CatalogItem> queryable = _allItems;

            if (!string.Equals(selectedType, "All", StringComparison.OrdinalIgnoreCase))
            {
                queryable = queryable.Where(item => string.Equals(item.StreamKind, selectedType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                string normalizedQuery = CatalogBuilder.Normalize(query);
                queryable = queryable.Where(item =>
                    item.NormalizedName.Contains(normalizedQuery) ||
                    item.NormalizedCategory.Contains(normalizedQuery));
            }

            queryable = sortMode == "NameDesc"
                ? queryable.OrderByDescending(item => item.SortName).ThenBy(item => item.CategoryName)
                : queryable.OrderBy(item => item.SortName).ThenBy(item => item.CategoryName);

            return AlphaGroup.Create(queryable);
        }

        public BrowseNode BuildBrowseRoot()
        {
            return CatalogBuilder.BuildBrowseTree(_allItems);
        }

        public string BuildStreamUrl(CatalogItem item)
        {
            return _providerClient.BuildStreamUrl(Connection.Server, Connection.Username, Connection.Password, item);
        }

        public string BuildSeriesEpisodeUrl(SeriesEpisode episode)
        {
            return _providerClient.BuildSeriesEpisodeUrl(Connection.Server, Connection.Username, Connection.Password, episode);
        }

        public async Task<SeriesInfoResponse> GetSeriesInfoAsync(CatalogItem item, CancellationToken cancellationToken)
        {
            string cacheKey = item.SeriesId.ToString();
            if (!_seriesCache.TryGetValue(cacheKey, out SeriesInfoResponse response))
            {
                response = await _providerClient.FetchSeriesInfoAsync(
                    Connection.Server,
                    Connection.Username,
                    Connection.Password,
                    item.SeriesId,
                    cancellationToken);
                _seriesCache[cacheKey] = response;
            }

            return response;
        }

        private async Task SetCatalogAsync(CatalogResponse response)
        {
            _seriesCache.Clear();
            Catalog = response ?? new CatalogResponse();
            _allItems = await Task.Run(() => CatalogBuilder.Build(Catalog));
            IsCatalogLoaded = _allItems.Count > 0;
            OnPropertyChanged(nameof(AllItems));
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
