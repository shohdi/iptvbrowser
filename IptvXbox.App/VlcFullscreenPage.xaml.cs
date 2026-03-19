using IptvXbox.App.Models;
using LibVLCSharp.Platforms.UWP;
using LibVLCSharp.Shared;
using System;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace IptvXbox.App
{
    public sealed partial class VlcFullscreenPage : Page
    {
        private LibVLC _libVlc;
        private VlcMediaPlayer _vlcPlayer;
        private Media _currentMedia;
        private VlcPlaybackRequest _pendingRequest;
        private bool _isInFullScreenMode;

        public VlcFullscreenPage()
        {
            InitializeComponent();
            Loaded += VlcFullscreenPage_Loaded;
            Unloaded += VlcFullscreenPage_Unloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pendingRequest = e.Parameter as VlcPlaybackRequest;
            TryStartPlayback();
        }

        private void VlcPlayerView_Initialized(object sender, InitializedEventArgs e)
        {
            _libVlc = new LibVLC(e.SwapChainOptions);
            _vlcPlayer = new VlcMediaPlayer(_libVlc);
            VlcPlayerView.MediaPlayer = _vlcPlayer;
            TryStartPlayback();
        }

        private void TryStartPlayback()
        {
            if (_pendingRequest == null || _vlcPlayer == null || string.IsNullOrWhiteSpace(_pendingRequest.Url))
            {
                return;
            }

            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVlc, new Uri(_pendingRequest.Url));
            _vlcPlayer.Play(_currentMedia);
            _vlcPlayer.Fullscreen = true;
            _pendingRequest = null;
        }

        private void VlcFullscreenPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isInFullScreenMode)
            {
                _isInFullScreenMode = ApplicationView.GetForCurrentView().TryEnterFullScreenMode();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ExitFullScreenMode();
            MainPage.Current?.CloseOverlay();
        }

        private void VlcFullscreenPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ExitFullScreenMode();
            _vlcPlayer?.Stop();
            _currentMedia?.Dispose();
            _currentMedia = null;
            _vlcPlayer?.Dispose();
            _libVlc?.Dispose();
            _vlcPlayer = null;
            _libVlc = null;
        }

        private void ExitFullScreenMode()
        {
            if (_isInFullScreenMode)
            {
                ApplicationView.GetForCurrentView().ExitFullScreenMode();
                _isInFullScreenMode = false;
            }
        }
    }
}
