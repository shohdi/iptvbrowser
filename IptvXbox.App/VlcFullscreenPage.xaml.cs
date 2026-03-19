using IptvXbox.App.Models;
using LibVLCSharp.Platforms.UWP;
using LibVLCSharp.Shared;
using System;
using Windows.UI.Core;
using Windows.System;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
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
            TitleTextBlock.Text = _pendingRequest?.Title ?? "VLC";
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
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ExitFullScreenMode();
            MainPage.Current?.CloseOverlay();
        }

        private void VlcFullscreenPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
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

        private void InteractionSurface_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ToggleControlsVisibility();
        }

        private void InteractionSurface_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            HandleKey(e.Key, () => e.Handled = true);
        }

        private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            HandleKey(args.VirtualKey, () => args.Handled = true);
        }

        private void HandleKey(VirtualKey key, Action markHandled)
        {
            if (key == VirtualKey.GamepadA || key == VirtualKey.Enter || key == VirtualKey.Space)
            {
                ToggleControlsVisibility();
                markHandled?.Invoke();
                return;
            }

            if (key == VirtualKey.GamepadB || key == VirtualKey.Escape)
            {
                CloseButton_Click(this, new RoutedEventArgs());
                markHandled?.Invoke();
            }
        }

        private void ToggleControlsVisibility()
        {
            ControlsOverlay.Visibility = ControlsOverlay.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vlcPlayer == null)
            {
                return;
            }

            if (_vlcPlayer.IsPlaying)
            {
                _vlcPlayer.Pause();
                PlayPauseButton.Content = "Play";
            }
            else
            {
                _vlcPlayer.Play();
                PlayPauseButton.Content = "Pause";
            }
        }

        private void BackThirtyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vlcPlayer == null)
            {
                return;
            }

            long target = Math.Max(0, _vlcPlayer.Time - 30000);
            _vlcPlayer.Time = target;
        }

        private void ForwardThirtyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vlcPlayer == null)
            {
                return;
            }

            long maxTime = _vlcPlayer.Length > 0 ? _vlcPlayer.Length : long.MaxValue;
            long target = Math.Min(maxTime, _vlcPlayer.Time + 30000);
            _vlcPlayer.Time = target;
        }
    }
}
