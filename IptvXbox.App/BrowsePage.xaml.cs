using IptvXbox.App.Models;
using IptvXbox.App.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace IptvXbox.App
{
    public sealed partial class BrowsePage : Page
    {
        private readonly AppSession _session = AppSession.Current;
        private readonly Stack<BrowseNode> _navigationHistory = new Stack<BrowseNode>();
        private readonly BrowseNode _continueNode = new BrowseNode { Title = "Continue" };
        private BuiltinPlayback _playback;
        private BrowseNode _currentNode;
        private CatalogItem _selectedSeries;
        private SeriesEpisode _selectedEpisode;
        private WatchHistoryEntry _selectedWatchEntry;
        private bool _isViewReady;

        public BrowsePage()
        {
            InitializeComponent();
            Loaded += BrowsePage_Loaded;
            Unloaded += BrowsePage_Unloaded;
        }

        private async void BrowsePage_Loaded(object sender, RoutedEventArgs e)
        {
            _isViewReady = true;
            _playback = new BuiltinPlayback();
            PlayerElement.SetMediaPlayer(_playback.Player);
            _session.PropertyChanged += Session_PropertyChanged;
            await _session.LoadInitialLocalDataAsync();
            if (_isViewReady)
            {
                LoadCurrentNode(resetHistory: true);
            }
        }

        private void BrowsePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isViewReady = false;
            _session.PropertyChanged -= Session_PropertyChanged;
            _playback?.Stop();
            PlayerElement.SetMediaPlayer(null);
            _playback?.Dispose();
            _playback = null;
        }

        private void Session_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSession.AllItems))
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (_isViewReady)
                    {
                        LoadCurrentNode(resetHistory: true);
                    }
                });
            }
        }

        private void LoadCurrentNode(bool resetHistory)
        {
            if (resetHistory)
            {
                ClearItemSelection();
                SelectionTextBlock.Text = "Choose a card to start browsing or playback.";
                _navigationHistory.Clear();
                _currentNode = _session.IsCatalogLoaded ? _session.BuildBrowseRoot() : new BrowseNode
                {
                    Title = "Browse Library"
                };
                _currentNode.Subtitle = _session.IsCatalogLoaded
                    ? "Choose Live, Movies, Series, or Continue."
                    : "Open Continue to see recent playback, or load a catalog from Login.";
                _currentNode.Cards.Add(new BrowseCard
                {
                    Title = "Continue",
                    Subtitle = "Return to recent movies, episodes, and live channels.",
                    Badge = "History",
                    TargetNode = _continueNode
                });
            }

            NodeTitleTextBlock.Text = _currentNode.Title;
            NodeSubtitleTextBlock.Text = _currentNode.Subtitle;
            BrowseGridView.ItemsSource = _currentNode.Cards;
            BackButton.IsEnabled = _navigationHistory.Count > 0;
            StatusTextBlock.Text = _session.IsCatalogLoaded || ReferenceEquals(_currentNode, _continueNode)
                ? $"{_currentNode.Cards.Count:N0} cards"
                : "No catalog loaded";
        }

        private void LoadContinueCards()
        {
            _continueNode.Cards.Clear();
            foreach (WatchHistoryEntry entry in _session.GetWatchHistory())
            {
                _continueNode.Cards.Add(new BrowseCard
                {
                    Title = entry.Title,
                    Subtitle = GetWatchSubtitle(entry),
                    ImageUrl = entry.ImageUrl,
                    Badge = entry.StreamKind,
                    WatchEntry = entry
                });
            }

            _continueNode.Subtitle = _continueNode.Cards.Count > 0
                ? "Recently played movies, episodes, and live channels."
                : "Nothing played yet.";
        }

        private static string GetWatchSubtitle(WatchHistoryEntry entry)
        {
            if (entry.IsLive || !entry.Position.HasValue || entry.Position.Value <= TimeSpan.Zero)
            {
                return entry.Subtitle;
            }

            TimeSpan position = entry.Position.Value;
            string time = position.TotalHours >= 1
                ? $"{(int)position.TotalHours}:{position.Minutes:D2}:{position.Seconds:D2}"
                : $"{position.Minutes}:{position.Seconds:D2}";
            return string.IsNullOrWhiteSpace(entry.Subtitle)
                ? $"Resume at {time}"
                : $"Resume at {time}\n{entry.Subtitle}";
        }

        private async void BrowseGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (!(e.ClickedItem is BrowseCard card))
            {
                return;
            }

            if (card.TargetNode != null)
            {
                if (ReferenceEquals(card.TargetNode, _continueNode))
                {
                    LoadContinueCards();
                }

                _navigationHistory.Push(_currentNode);
                _currentNode = card.TargetNode;
                LoadCurrentNode(resetHistory: false);
                ClearItemSelection();
                SelectionTextBlock.Text = $"{card.Title}\n{card.Subtitle}";
                return;
            }

            if (card.WatchEntry != null)
            {
                SelectWatchEntry(card.WatchEntry);
                return;
            }

            if (card.Item != null)
            {
                await HandleItemSelectionAsync(card.Item);
            }
        }

        private async Task HandleItemSelectionAsync(CatalogItem item)
        {
            if (!_session.HasCredentials)
            {
                StatusTextBlock.Text = "Open Login and save server, username, and password first.";
                return;
            }

            if (item.IsSeries)
            {
                _selectedSeries = item;
                _selectedEpisode = null;
                _selectedWatchEntry = null;
                SelectionTextBlock.Text = $"{item.Name}\n{item.Subtitle}";
                _playback?.Stop();
                PlaybackButtonsPanel.Visibility = Visibility.Collapsed;
                await LoadSeriesAsync(item);
                return;
            }

            SelectWatchEntry(_session.CreateWatchEntry(item));
        }

        private void SelectWatchEntry(WatchHistoryEntry entry)
        {
            if (entry == null)
            {
                StatusTextBlock.Text = "No playable item is available.";
                return;
            }

            _selectedSeries = null;
            _selectedEpisode = null;
            _selectedWatchEntry = entry;
            _playback?.Stop();
            SelectionTextBlock.Text = $"{entry.Title}\n{GetWatchSubtitle(entry)}";
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
                if (!_isViewReady || !ReferenceEquals(_selectedSeries, item))
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
                if (_isViewReady && ReferenceEquals(_selectedSeries, item))
                {
                    StatusTextBlock.Text = $"Series load failed: {ex.Message}";
                }
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e) =>
            PlayEntry(_selectedWatchEntry, useVlc: false);

        private void PlayWithVlcButton_Click(object sender, RoutedEventArgs e) =>
            PlayEntry(_selectedWatchEntry, useVlc: true);

        private void CopyUrlButton_Click(object sender, RoutedEventArgs e) =>
            CopyUrlToClipboard(_selectedWatchEntry, "URL copied.");

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_navigationHistory.Count == 0)
            {
                return;
            }

            _currentNode = _navigationHistory.Pop();
            ClearItemSelection();
            SelectionTextBlock.Text = "Choose a card to start browsing or playback.";
            LoadCurrentNode(resetHistory: false);
        }

        private void ClearItemSelection()
        {
            _selectedSeries = null;
            _selectedEpisode = null;
            _selectedWatchEntry = null;
            PlaybackButtonsPanel.Visibility = Visibility.Collapsed;
            SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
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

        private void PlayEpisodeButton_Click(object sender, RoutedEventArgs e) =>
            PlayEntry(GetSelectedEpisodeEntry(), useVlc: false);

        private void PlayEpisodeWithVlcButton_Click(object sender, RoutedEventArgs e) =>
            PlayEntry(GetSelectedEpisodeEntry(), useVlc: true);

        private void CopyEpisodeUrlButton_Click(object sender, RoutedEventArgs e) =>
            CopyUrlToClipboard(GetSelectedEpisodeEntry(), "Episode URL copied.");

        private WatchHistoryEntry GetSelectedEpisodeEntry() =>
            _selectedSeries != null && _selectedEpisode != null && SeasonComboBox.SelectedItem is SeasonOption season
                ? _session.CreateWatchEntry(_selectedSeries, _selectedEpisode, season.SeasonNumber)
                : null;

        private void PlayEntry(WatchHistoryEntry entry, bool useVlc)
        {
            string url = GetWatchUrl(entry);
            if (url == null)
            {
                return;
            }

            if (useVlc)
            {
                _playback?.Stop();
                MainPage.Current?.ShowOverlay(typeof(VlcFullscreenPage), new VlcPlaybackRequest
                {
                    Title = entry.Title,
                    Subtitle = entry.Subtitle,
                    Url = url,
                    WatchEntry = entry
                });
                return;
            }

            _playback?.Play(url, entry);
            StatusTextBlock.Text = "Playback started.";
        }

        private string GetWatchUrl(WatchHistoryEntry entry)
        {
            if (entry == null)
            {
                StatusTextBlock.Text = "Select an item to play first.";
                return null;
            }

            if (!_session.HasCredentials)
            {
                StatusTextBlock.Text = "Open Login and save server, username, and password first.";
                return null;
            }

            string url = _session.BuildWatchUrl(entry);
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                StatusTextBlock.Text = "No playback URL is available for this item.";
                return null;
            }

            return url;
        }

        private void CopyUrlToClipboard(WatchHistoryEntry entry, string statusMessage)
        {
            string url = GetWatchUrl(entry);
            if (url == null)
            {
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(url);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
            StatusTextBlock.Text = statusMessage;
            SelectionTextBlock.Text = $"{entry.Title}\n{GetWatchSubtitle(entry)}\n{url}";
        }
    }
}
