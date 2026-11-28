using IptvXbox.App.Models;
using IptvXbox.App.Services;
using System;
using System.Collections.Generic;
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
    public sealed partial class BrowsePage : Page
    {
        private readonly AppSession _session = AppSession.Current;
        private readonly Stack<BrowseNode> _navigationHistory = new Stack<BrowseNode>();
        private readonly BrowseNode _continueNode = new BrowseNode { Title = "Continue" };
        private BuiltinPlayback _playback;
        private BrowseNode _currentNode;
        private CatalogItem _selectedSeries;
        private WatchHistoryEntry _selectedWatchEntry;
        private int _selectionVersion;
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
            ClearItemSelection();
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
                await HandleWatchEntrySelectionAsync(card.WatchEntry);
                return;
            }

            if (card.Item != null)
            {
                await HandleItemSelectionAsync(card.Item);
            }
        }

        private Task HandleWatchEntrySelectionAsync(WatchHistoryEntry entry)
        {
            if (entry.StreamKind == "Episode" && entry.SeriesId > 0)
            {
                CatalogItem series = _session.AllItems.FirstOrDefault(item => item.SeriesId == entry.SeriesId)
                    ?? new CatalogItem { SeriesId = entry.SeriesId, ImageUrl = entry.ImageUrl };
                return HandleItemSelectionAsync(series, entry);
            }

            SelectWatchEntry(entry);
            return Task.CompletedTask;
        }

        private async Task HandleItemSelectionAsync(CatalogItem item, WatchHistoryEntry resumeEntry = null)
        {
            if (!_session.HasCredentials)
            {
                StatusTextBlock.Text = "Open Login and save server, username, and password first.";
                return;
            }

            if (item.IsSeries)
            {
                ClearItemSelection();
                _selectedSeries = item;
                SelectionTextBlock.Text = item.Name == null ? resumeEntry?.Title : $"{item.Name}\n{item.Subtitle}";
                _playback?.Stop();
                await LoadSeriesAsync(item, resumeEntry);
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

            ClearItemSelection();
            _selectedWatchEntry = entry;
            _playback?.Stop();
            SelectionTextBlock.Text = $"{entry.Title}\n{GetWatchSubtitle(entry)}";
            PlaybackButtonsPanel.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Choose Play or Play on VLC.";
        }

        private async Task LoadSeriesAsync(CatalogItem item, WatchHistoryEntry resumeEntry)
        {
            int selectionVersion = _selectionVersion;
            StatusTextBlock.Text = "Loading series episodes...";

            try
            {
                SeriesInfoResponse response = await _session.GetSeriesInfoAsync(item, CancellationToken.None);
                if (!_isViewReady || _selectionVersion != selectionVersion)
                {
                    return;
                }

                List<SeasonOption> seasons = response.GetSeasonOptions();
                SeasonOption savedSeason = null;
                SeriesEpisode savedEpisode = null;
                if (!string.IsNullOrEmpty(resumeEntry?.StreamId))
                {
                    foreach (SeasonOption season in seasons)
                    {
                        savedEpisode = season.Episodes.FirstOrDefault(episode => episode?.Id == resumeEntry.StreamId);
                        if (savedEpisode != null)
                        {
                            savedSeason = season;
                            break;
                        }
                    }
                }

                if (item.Name == null && resumeEntry != null)
                {
                    // Existing history includes the episode title when the catalog is unavailable.
                    string suffix = $" · {savedEpisode?.Title}";
                    item.Name = !string.IsNullOrWhiteSpace(savedEpisode?.Title) &&
                        resumeEntry.Title.EndsWith(suffix, StringComparison.Ordinal)
                        ? resumeEntry.Title.Substring(0, resumeEntry.Title.Length - suffix.Length)
                        : resumeEntry.Title;
                    SelectionTextBlock.Text = item.Name;
                }

                SeasonComboBox.ItemsSource = seasons;
                SeasonComboBox.SelectedItem = savedSeason ?? seasons.FirstOrDefault();
                if (resumeEntry != null)
                {
                    EpisodeComboBox.SelectedItem = savedEpisode;
                }

                if (seasons.Count == 0)
                {
                    StatusTextBlock.Text = "No episodes found.";
                    return;
                }

                SeriesSelectorsPanel.Visibility = Visibility.Visible;
                StatusTextBlock.Text = resumeEntry != null && savedEpisode == null
                    ? "Saved episode unavailable. Choose another episode."
                    : "Choose a season and episode, then Play or Play on VLC.";
            }
            catch (Exception ex)
            {
                if (_isViewReady && _selectionVersion == selectionVersion)
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
            _selectionVersion++;
            _selectedSeries = null;
            _selectedWatchEntry = null;
            SeasonComboBox.ItemsSource = null;
            PlaybackButtonsPanel.Visibility = Visibility.Collapsed;
            SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
        }

        private void SeasonComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            List<SeriesEpisode> episodes = (SeasonComboBox.SelectedItem as SeasonOption)?.Episodes;
            EpisodeComboBox.ItemsSource = episodes;
            EpisodeComboBox.SelectedIndex = episodes?.Count > 0 ? 0 : -1;
        }

        private void PlayEpisodeButton_Click(object sender, RoutedEventArgs e) =>
            PlayEntry(GetSelectedEpisodeEntry(), useVlc: false);

        private void PlayEpisodeWithVlcButton_Click(object sender, RoutedEventArgs e) =>
            PlayEntry(GetSelectedEpisodeEntry(), useVlc: true);

        private void CopyEpisodeUrlButton_Click(object sender, RoutedEventArgs e) =>
            CopyUrlToClipboard(GetSelectedEpisodeEntry(), "Episode URL copied.");

        private WatchHistoryEntry GetSelectedEpisodeEntry() =>
            _selectedSeries != null && SeasonComboBox.SelectedItem is SeasonOption season &&
                EpisodeComboBox.SelectedItem is SeriesEpisode episode
                ? _session.CreateWatchEntry(_selectedSeries, episode, season.SeasonNumber)
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
