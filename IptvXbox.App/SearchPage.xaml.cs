using IptvXbox.App.Models;
using IptvXbox.App.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace IptvXbox.App
{
    public sealed partial class SearchPage : Page
    {
        private readonly AppSession _session = AppSession.Current;
        private readonly MediaPlayer _player = new MediaPlayer();
        private CancellationTokenSource _searchCts;
        private CatalogItem _selectedItem;
        private SeriesEpisode _selectedEpisode;

        public ObservableCollection<AlphaGroup> GroupedItems { get; } = new ObservableCollection<AlphaGroup>();

        public SearchPage()
        {
            InitializeComponent();
            DataContext = this;
            PlayerElement.SetMediaPlayer(_player);
            Loaded += SearchPage_Loaded;
            Unloaded += SearchPage_Unloaded;
        }

        private async void SearchPage_Loaded(object sender, RoutedEventArgs e)
        {
            _session.PropertyChanged += Session_PropertyChanged;
            await _session.LoadInitialLocalDataAsync();
            await ApplyFiltersAsync();
        }

        private void SearchPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _session.PropertyChanged -= Session_PropertyChanged;
            _searchCts?.Cancel();
        }

        private void Session_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
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
            if (SearchTextBox == null || ContentTypeComboBox == null || SortComboBox == null)
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
