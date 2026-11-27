using IptvXbox.App.Models;
using IptvXbox.App.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace IptvXbox.App
{
    public sealed partial class SearchPage : Page
    {
        private readonly AppSession _session = AppSession.Current;
        private BuiltinPlayback _playback;
        private CancellationTokenSource _searchCts;
        private bool _isViewReady;
        private CatalogItem _selectedItem;
        private SeriesEpisode _selectedEpisode;

        public ObservableCollection<AlphaGroup> GroupedItems { get; } = new ObservableCollection<AlphaGroup>();

        public SearchPage()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += SearchPage_Loaded;
            Unloaded += SearchPage_Unloaded;
        }

        private async void SearchPage_Loaded(object sender, RoutedEventArgs e)
        {
            _playback = new BuiltinPlayback();
            PlayerElement.SetMediaPlayer(_playback.Player);
            _isViewReady = true;
            _session.PropertyChanged += Session_PropertyChanged;
            await _session.LoadInitialLocalDataAsync();
            await ApplyFiltersAsync();
        }

        private void SearchPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isViewReady = false;
            _session.PropertyChanged -= Session_PropertyChanged;
            _searchCts?.Cancel();
            _playback?.Stop();
            PlayerElement.SetMediaPlayer(null);
            _playback?.Dispose();
            _playback = null;
        }

        private void Session_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!_isViewReady)
            {
                return;
            }

            if (e.PropertyName == nameof(AppSession.AllItems) ||
                e.PropertyName == nameof(AppSession.StatusMessage) ||
                e.PropertyName == nameof(AppSession.IsCatalogLoaded))
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => await ApplyFiltersAsync());
            }
        }

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => await ApplyFiltersAsync();

        private async void Filters_Changed(object sender, SelectionChangedEventArgs e) => await ApplyFiltersAsync();

        private async Task ApplyFiltersAsync()
        {
            if (!_isViewReady ||
                SearchTextBox == null ||
                ContentTypeComboBox == null ||
                SortComboBox == null ||
                StatusTextBlock == null)
            {
                return;
            }

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            CancellationToken token = _searchCts.Token;

            string query = SearchTextBox.Text?.Trim() ?? string.Empty;
            string selectedType = ((ContentTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string) ?? "All";
            string sortMode = ((SortComboBox.SelectedItem as ComboBoxItem)?.Tag as string) ?? "NameAsc";

            StatusTextBlock.Text = _session.IsCatalogLoaded ? "Filtering catalog..." : "Load a catalog from the Login page.";

            try
            {
                IReadOnlyList<AlphaGroup> filtered = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return _session.FilterItems(query, selectedType, sortMode);
                }, token);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    GroupedItems.Clear();
                    foreach (AlphaGroup group in filtered)
                    {
                        GroupedItems.Add(group);
                    }

                    StatusTextBlock.Text = _session.IsCatalogLoaded
                        ? $"{GroupedItems.Sum(group => group.Count):N0} items shown."
                        : "Load a catalog from the Login page.";
                });
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async void CatalogListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (!(e.ClickedItem is CatalogItem item))
            {
                return;
            }

            if (!_session.HasCredentials)
            {
                StatusTextBlock.Text = "Open Login and save server, username, and password first.";
                return;
            }

            _selectedItem = item;
            _selectedEpisode = null;
            SelectionTextBlock.Text = $"{item.Name}\n{item.Subtitle}";
            _playback.Stop();

            if (item.IsSeries)
            {
                PlaybackButtonsPanel.Visibility = Visibility.Collapsed;
                await LoadSeriesAsync(item);
                return;
            }

            SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
            PlaybackButtonsPanel.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Choose Play or Play on VLC.";
        }

        private async Task LoadSeriesAsync(CatalogItem item)
        {
            SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
            StatusTextBlock.Text = "Loading series episodes...";

            try
            {
                SeriesInfoResponse response = await _session.GetSeriesInfoAsync(item, CancellationToken.None);
                if (!_isViewReady || !ReferenceEquals(item, _selectedItem))
                {
                    return;
                }

                List<SeasonOption> seasons = response.GetSeasonOptions();
                SeasonComboBox.ItemsSource = seasons;
                SeasonComboBox.SelectedIndex = seasons.Count > 0 ? 0 : -1;
                SeriesSelectorsPanel.Visibility = seasons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                StatusTextBlock.Text = seasons.Count > 0 ? "Choose a season and episode, then Play or Play on VLC." : "No episodes found.";
            }
            catch (Exception ex)
            {
                if (_isViewReady && ReferenceEquals(item, _selectedItem))
                {
                    StatusTextBlock.Text = $"Series load failed: {ex.Message}";
                }
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedItem.IsSeries)
            {
                StatusTextBlock.Text = "Select a live channel or movie first.";
                return;
            }

            Play(_session.CreateWatchEntry(_selectedItem));
        }

        private void PlayWithVlcButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedItem.IsSeries)
            {
                StatusTextBlock.Text = "Select a live channel or movie first.";
                return;
            }

            OpenVlcOverlay(_session.CreateWatchEntry(_selectedItem));
        }

        private void CopyUrlButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedItem.IsSeries)
            {
                StatusTextBlock.Text = "Select a live channel or movie first.";
                return;
            }

            CopyUrlToClipboard(_session.BuildStreamUrl(_selectedItem), "URL copied.");
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
            if (_selectedItem == null || _selectedEpisode == null)
            {
                StatusTextBlock.Text = "Select an episode first.";
                return;
            }

            Play(CreateEpisodeWatchEntry());
        }

        private void PlayEpisodeWithVlcButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedEpisode == null)
            {
                StatusTextBlock.Text = "Select an episode first.";
                return;
            }

            OpenVlcOverlay(CreateEpisodeWatchEntry());
        }

        private void CopyEpisodeUrlButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedEpisode == null)
            {
                StatusTextBlock.Text = "Select an episode first.";
                return;
            }

            CopyUrlToClipboard(_session.BuildSeriesEpisodeUrl(_selectedEpisode), "Episode URL copied.");
        }

        private WatchHistoryEntry CreateEpisodeWatchEntry()
        {
            return _session.CreateWatchEntry(_selectedItem, _selectedEpisode,
                (SeasonComboBox.SelectedItem as SeasonOption)?.SeasonNumber);
        }

        private void Play(WatchHistoryEntry entry)
        {
            _playback.Play(_session.BuildWatchUrl(entry), entry);
            StatusTextBlock.Text = "Playback started.";
        }

        private void OpenVlcOverlay(WatchHistoryEntry entry)
        {
            string url = _session.BuildWatchUrl(entry);
            _playback.Stop();
            MainPage.Current?.ShowOverlay(typeof(VlcFullscreenPage), new VlcPlaybackRequest
            {
                Title = entry.Title,
                Subtitle = entry.Subtitle,
                Url = url,
                WatchEntry = entry
            });
        }

        private void CopyUrlToClipboard(string url, string statusMessage)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                StatusTextBlock.Text = "No URL available to copy.";
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(url);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
            StatusTextBlock.Text = statusMessage;
            SelectionTextBlock.Text = $"{SelectionTextBlock.Text}\n{url}";
        }
    }
}
