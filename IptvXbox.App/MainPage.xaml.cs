using IptvXbox.App.Models;
using IptvXbox.App.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace IptvXbox.App
{
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private const string ServerSettingKey = "iptv.server";
        private const string UsernameSettingKey = "iptv.username";
        private const string PasswordSettingKey = "iptv.password";

        private readonly IptvProviderClient _providerClient = new IptvProviderClient();
        private readonly List<CatalogItem> _allItems = new List<CatalogItem>(70000);
        private readonly Dictionary<string, SeriesInfoResponse> _seriesCache = new Dictionary<string, SeriesInfoResponse>();
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;
        private CancellationTokenSource _searchCts;
        private CatalogItem _selectedItem;
        private SeriesEpisode _selectedEpisode;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<AlphaGroup> GroupedItems { get; } = new ObservableCollection<AlphaGroup>();

        public MainPage()
        {
            InitializeComponent();
            DataContext = this;
            PlayerElement.SetMediaPlayer(_player);
            Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSavedConnectionSettings();
            await TryLoadInitialLocalDataAsync();
        }

        private async Task TryLoadInitialLocalDataAsync()
        {
            try
            {
                StorageFile localFile = await StorageFile.GetFileFromPathAsync(Path.Combine(AppContext.BaseDirectory, "channels.json"));
                await LoadCatalogFromFileAsync(localFile);
            }
            catch
            {
                StatusTextBlock.Text = "Ready. Load a local file or refresh from the API.";
            }
        }

        private async void RefreshApiButton_Click(object sender, RoutedEventArgs e) => await RefreshFromApiAsync();

        private async void LoadLocalFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(Path.Combine(AppContext.BaseDirectory, "channels.json"));
                await LoadCatalogFromFileAsync(file);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Unable to load channels.json: {ex.Message}";
            }
        }

        private async Task RefreshFromApiAsync()
        {
            if (!TrySaveConnectionSettings())
            {
                return;
            }

            StatusTextBlock.Text = "Refreshing from IPTV API...";
            try
            {
                var response = await _providerClient.FetchCatalogAsync(
                    ServerTextBox.Text,
                    UsernameTextBox.Text,
                    PasswordTextBox.Password,
                    CancellationToken.None);

                await SetCatalogAsync(response);
                StatusTextBlock.Text = $"Loaded {_allItems.Count:N0} items from the API.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"API refresh failed: {ex.Message}";
            }
        }

        private async Task LoadCatalogFromFileAsync(StorageFile file)
        {
            StatusTextBlock.Text = "Loading channels.json...";
            string json = await FileIO.ReadTextAsync(file);
            var response = JsonConvert.DeserializeObject<CatalogResponse>(json) ?? new CatalogResponse();
            await SetCatalogAsync(response);
            StatusTextBlock.Text = $"Loaded {_allItems.Count:N0} items from channels.json.";
        }

        private async Task SetCatalogAsync(CatalogResponse response)
        {
            _seriesCache.Clear();
            List<CatalogItem> items = await Task.Run(() => CatalogBuilder.Build(response));

            _allItems.Clear();
            _allItems.AddRange(items);
            await ApplyFiltersAsync();
        }

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => await ApplyFiltersAsync();

        private async void Filters_Changed(object sender, SelectionChangedEventArgs e) => await ApplyFiltersAsync();

        private async Task ApplyFiltersAsync()
        {
            if (SearchTextBox == null || ContentTypeComboBox == null || SortComboBox == null || StatusTextBlock == null)
            {
                return;
            }

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            CancellationToken token = _searchCts.Token;

            string query = SearchTextBox.Text?.Trim() ?? string.Empty;
            string selectedType = ((ContentTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string) ?? "All";
            string sortMode = ((SortComboBox.SelectedItem as ComboBoxItem)?.Tag as string) ?? "NameAsc";

            StatusTextBlock.Text = "Filtering catalog...";

            try
            {
                IReadOnlyList<AlphaGroup> filtered = await Task.Run(() =>
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

                    token.ThrowIfCancellationRequested();
                    return AlphaGroup.Create(queryable);
                }, token);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    GroupedItems.Clear();
                    foreach (AlphaGroup group in filtered)
                    {
                        GroupedItems.Add(group);
                    }

                    StatusTextBlock.Text = $"{GroupedItems.Sum(group => group.Count):N0} items shown.";
                });
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async void CatalogListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CatalogItem item)
            {
                if (!TrySaveConnectionSettings())
                {
                    return;
                }

                _selectedItem = item;
                _selectedEpisode = null;
                SelectionTextBlock.Text = $"{item.Name}\n{item.Subtitle}";

                if (item.IsSeries)
                {
                    await LoadSeriesAsync(item);
                    return;
                }

                SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
                string url = _providerClient.BuildStreamUrl(ServerTextBox.Text, UsernameTextBox.Text, PasswordTextBox.Password, item);
                PlayUrl(url);
            }
        }

        private async Task LoadSeriesAsync(CatalogItem item)
        {
            SeriesSelectorsPanel.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Loading series episodes...";

            try
            {
                string cacheKey = item.SeriesId.ToString();
                if (!_seriesCache.TryGetValue(cacheKey, out SeriesInfoResponse response))
                {
                    response = await _providerClient.FetchSeriesInfoAsync(
                        ServerTextBox.Text,
                        UsernameTextBox.Text,
                        PasswordTextBox.Password,
                        item.SeriesId,
                        CancellationToken.None);
                    _seriesCache[cacheKey] = response;
                }

                List<SeasonOption> seasons = response.GetSeasonOptions();
                SeasonComboBox.ItemsSource = seasons;
                SeasonComboBox.SelectedIndex = seasons.Count > 0 ? 0 : -1;
                StatusTextBlock.Text = seasons.Count > 0 ? "Choose a season and episode." : "No episodes found.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Series load failed: {ex.Message}";
                SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void SeasonComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SeasonComboBox.SelectedItem is SeasonOption season)
            {
                EpisodeComboBox.ItemsSource = season.Episodes;
                EpisodeComboBox.SelectedIndex = season.Episodes.Count > 0 ? 0 : -1;
            }
        }

        private void EpisodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedEpisode = EpisodeComboBox.SelectedItem as SeriesEpisode;
        }

        private void PlayEpisodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveConnectionSettings())
            {
                return;
            }

            if (_selectedItem is null || _selectedEpisode is null)
            {
                StatusTextBlock.Text = "Select an episode first.";
                return;
            }

            string url = _providerClient.BuildSeriesEpisodeUrl(
                ServerTextBox.Text,
                UsernameTextBox.Text,
                PasswordTextBox.Password,
                _selectedEpisode);

            PlayUrl(url);
        }

        private void PlayUrl(string url)
        {
            SelectionTextBlock.Text = $"{SelectionTextBlock.Text}\n{url}";
            _player.Source = MediaSource.CreateFromUri(new Uri(url));
            _player.Play();
            StatusTextBlock.Text = "Playback started.";
        }

        private void LoadSavedConnectionSettings()
        {
            IPropertySet values = _localSettings.Values;
            ServerTextBox.Text = values.ContainsKey(ServerSettingKey) ? values[ServerSettingKey] as string ?? string.Empty : string.Empty;
            UsernameTextBox.Text = values.ContainsKey(UsernameSettingKey) ? values[UsernameSettingKey] as string ?? string.Empty : string.Empty;
            PasswordTextBox.Password = values.ContainsKey(PasswordSettingKey) ? values[PasswordSettingKey] as string ?? string.Empty : string.Empty;
        }

        private bool TrySaveConnectionSettings()
        {
            string server = (ServerTextBox.Text ?? string.Empty).Trim();
            string username = (UsernameTextBox.Text ?? string.Empty).Trim();
            string password = PasswordTextBox.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                StatusTextBlock.Text = "Enter server, username, and password first.";
                return false;
            }

            _localSettings.Values[ServerSettingKey] = server;
            _localSettings.Values[UsernameSettingKey] = username;
            _localSettings.Values[PasswordSettingKey] = password;
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
