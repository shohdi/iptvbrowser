using IptvXbox.App.Models;
using IptvXbox.App.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace IptvXbox.App
{
    public sealed partial class BrowsePage : Page
    {
        private readonly AppSession _session = AppSession.Current;
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly Stack<BrowseNode> _history = new Stack<BrowseNode>();
        private BrowseNode _currentNode;
        private CatalogItem _selectedItem;
        private SeriesEpisode _selectedEpisode;

        public BrowsePage()
        {
            InitializeComponent();
            PlayerElement.SetMediaPlayer(_player);
            Loaded += BrowsePage_Loaded;
            Unloaded += BrowsePage_Unloaded;
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

            if (item.IsSeries)
            {
                await LoadSeriesAsync(item);
                return;
            }

            SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
            PlayUrl(_session.BuildStreamUrl(item));
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
                StatusTextBlock.Text = seasons.Count > 0 ? "Choose a season and episode." : "No episodes found.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Series load failed: {ex.Message}";
                SeriesSelectorsPanel.Visibility = Visibility.Collapsed;
            }
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

            PlayUrl(_session.BuildSeriesEpisodeUrl(_selectedEpisode));
        }

        private void PlayUrl(string url)
        {
            SelectionTextBlock.Text = $"{SelectionTextBlock.Text}\n{url}";
            _player.Source = MediaSource.CreateFromUri(new Uri(url));
            _player.Play();
            StatusTextBlock.Text = "Playback started.";
        }
    }
}
