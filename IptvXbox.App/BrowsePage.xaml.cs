using IptvXbox.App.Models;
using IptvXbox.App.Services;
using LibVLCSharp.Platforms.UWP;
using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using UwpMediaPlayer = Windows.Media.Playback.MediaPlayer;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace IptvXbox.App
{
    public sealed partial class BrowsePage : Page
    {
        private readonly AppSession _session = AppSession.Current;
        private readonly UwpMediaPlayer _player = new UwpMediaPlayer();
        private  LibVLC _libVlc = VlcPlaybackService.SharedLibVlc;
        private  VlcMediaPlayer _vlcPlayer;
        private readonly Stack<BrowseNode> _history = new Stack<BrowseNode>();
        private BrowseNode _currentNode;
        private CatalogItem _selectedItem;
        private SeriesEpisode _selectedEpisode;
        private Media _currentVlcMedia;

        public BrowsePage()
        {
            InitializeComponent();
            PlayerElement.SetMediaPlayer(_player);
            
            Loaded += BrowsePage_Loaded;
            Unloaded += BrowsePage_Unloaded;
        }

		// STEP 1: Setup the "Video Pipe" (Only happens once)
		private void VlcPlayerView_Initialized(object sender, InitializedEventArgs e)
		{
			// This line FIXES the "Sound only" issue by passing SwapChainOptions
			_libVlc = new LibVLC(e.SwapChainOptions);

			_vlcPlayer = new VlcMediaPlayer(_libVlc);

			// Connect the player to the UI
			VlcPlayerView.MediaPlayer = _vlcPlayer;
		}

		private async void BrowsePage_Loaded(object sender, RoutedEventArgs e)
        {
            _session.PropertyChanged += Session_PropertyChanged;
            await _session.LoadInitialLocalDataAsync();
            LoadCurrentNode(resetHistory: true);
        }

        private void BrowsePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _session.PropertyChanged -= Session_PropertyChanged;
            StopBuiltinPlayer();
            StopVlcPlayer();
            _vlcPlayer.Dispose();
        }

        private void Session_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSession.AllItems) ||
                e.PropertyName == nameof(AppSession.IsCatalogLoaded))
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => LoadCurrentNode(resetHistory: true));
            }
        }

        private void LoadCurrentNode(bool resetHistory)
        {
            if (resetHistory)
            {
                _history.Clear();
                _currentNode = _session.IsCatalogLoaded ? _session.BuildBrowseRoot() : new BrowseNode
                {
                    Title = "Browse Library",
                    Subtitle = "Load a catalog from the Login page first."
                };
            }

            NodeTitleTextBlock.Text = _currentNode.Title;
            NodeSubtitleTextBlock.Text = _currentNode.Subtitle;
            BrowseGridView.ItemsSource = _currentNode.Cards;
            BackButton.IsEnabled = _history.Count > 0;
            StatusTextBlock.Text = _session.IsCatalogLoaded
                ? $"{_currentNode.Cards.Count:N0} cards"
                : "No catalog loaded";
        }

        private void BrowseGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (!(e.ClickedItem is BrowseCard card))
            {
                return;
            }

            if (card.TargetNode != null)
            {
                _history.Push(_currentNode);
                _currentNode = card.TargetNode;
                LoadCurrentNode(resetHistory: false);
                SelectionTextBlock.Text = $"{card.Title}\n{card.Subtitle}";
                SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (card.Item != null)
            {
                _ = HandleItemSelectionAsync(card.Item);
            }
        }

        private async Task HandleItemSelectionAsync(CatalogItem item)
        {
            if (!_session.HasCredentials)
            {
                StatusTextBlock.Text = "Open Login and save server, username, and password first.";
                return;
            }

            _selectedItem = item;
            _selectedEpisode = null;
            SelectionTextBlock.Text = $"{item.Name}\n{item.Subtitle}";
            StopBuiltinPlayer();
            StopVlcPlayer();

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
            SeriesSelectorsPanel.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Loading series episodes...";

            try
            {
                SeriesInfoResponse response = await _session.GetSeriesInfoAsync(item, CancellationToken.None);
                List<SeasonOption> seasons = response.GetSeasonOptions();
                SeasonComboBox.ItemsSource = seasons;
                SeasonComboBox.SelectedIndex = seasons.Count > 0 ? 0 : -1;
                StatusTextBlock.Text = seasons.Count > 0 ? "Choose a season and episode, then Play or Play on VLC." : "No episodes found.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Series load failed: {ex.Message}";
                SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedItem.IsSeries)
            {
                StatusTextBlock.Text = "Select a live channel or movie first.";
                return;
            }

            PlayUrl(_session.BuildStreamUrl(_selectedItem), useVlc: false);
        }

        private void PlayWithVlcButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedItem.IsSeries)
            {
                StatusTextBlock.Text = "Select a live channel or movie first.";
                return;
            }

            OpenVlcOverlay(_session.BuildStreamUrl(_selectedItem));
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_history.Count == 0)
            {
                return;
            }

            _currentNode = _history.Pop();
            LoadCurrentNode(resetHistory: false);
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

            PlayUrl(_session.BuildSeriesEpisodeUrl(_selectedEpisode), useVlc: false);
        }

        private void PlayEpisodeWithVlcButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedEpisode == null)
            {
                StatusTextBlock.Text = "Select an episode first.";
                return;
            }

            OpenVlcOverlay(_session.BuildSeriesEpisodeUrl(_selectedEpisode));
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

        private void PlayUrl(string url, bool useVlc)
        {
            SelectionTextBlock.Text = $"{SelectionTextBlock.Text}\n{url}";

            if (useVlc)
            {
                StopBuiltinPlayer();
                ShowVlcPlayer();

                _currentVlcMedia?.Dispose();
                _currentVlcMedia = new Media(_libVlc, new Uri(url));
                _vlcPlayer.Play(_currentVlcMedia);
				_vlcPlayer.Fullscreen = true;
                
				StatusTextBlock.Text = "VLC playback started.";
                return;
            }

            StopVlcPlayer();
            ShowBuiltinPlayer();
            _player.Source = MediaSource.CreateFromUri(new Uri(url));
            _player.Play();
            StatusTextBlock.Text = "Playback started.";
        }

        private void OpenVlcOverlay(string url)
        {
            MainPage.Current?.ShowOverlay(typeof(VlcFullscreenPage), new VlcPlaybackRequest
            {
                Title = _selectedItem?.Name ?? "VLC",
                Subtitle = _selectedItem?.Subtitle ?? url,
                Url = url
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

        private void ShowBuiltinPlayer()
        {
            PlayerElement.Visibility = Visibility.Visible;
            PlayerElement.IsHitTestVisible = true;
            VlcPlayerView.Visibility = Visibility.Collapsed;
            VlcPlayerView.IsHitTestVisible = false;
        }

        private void ShowVlcPlayer()
        {
            PlayerElement.Visibility = Visibility.Collapsed;
            PlayerElement.IsHitTestVisible = false;
            VlcPlayerView.Visibility = Visibility.Visible;
            VlcPlayerView.IsHitTestVisible = true;
            VlcPlayerView.MediaPlayer.Fullscreen = true;
        }

        private void StopBuiltinPlayer()
        {
            _player.Pause();
            _player.Source = null;
        }

        private void StopVlcPlayer()
        {
            _vlcPlayer.Stop();
            _currentVlcMedia?.Dispose();
            _currentVlcMedia = null;
        }
    }
}
