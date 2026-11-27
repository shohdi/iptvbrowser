using IptvXbox.App.Models;
using IptvXbox.App.Services;
using LibVLCSharp.Platforms.UWP;
using LibVLCSharp.Shared;
using System;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Core;
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
        private readonly DispatcherTimer _checkpointTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private LibVLC _libVlc;
        private VlcMediaPlayer _vlcPlayer;
        private Media _currentMedia;
        private VlcPlaybackRequest _pendingRequest;
        private WatchHistoryEntry _watchEntry;
        private long? _resumePositionMilliseconds;
        private long? _lastSavedPositionMilliseconds;
        private int _resumeSeekAttempts;
        private bool _hasRecordedPlayback;
        private bool _isInFullScreenMode;

        static VlcFullscreenPage()
        {
            Core.Initialize();
        }

        public VlcFullscreenPage()
        {
            InitializeComponent();
            Loaded += VlcFullscreenPage_Loaded;
            Unloaded += VlcFullscreenPage_Unloaded;
            _checkpointTimer.Tick += CheckpointTimer_Tick;
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
            _vlcPlayer.Playing += VlcPlayer_Playing;
            _vlcPlayer.SeekableChanged += VlcPlayer_SeekableChanged;
            _vlcPlayer.EndReached += VlcPlayer_EndReached;
            VlcPlayerView.MediaPlayer = _vlcPlayer;
            TryStartPlayback();
        }

        private void TryStartPlayback()
        {
            if (_pendingRequest == null || _vlcPlayer == null || string.IsNullOrWhiteSpace(_pendingRequest.Url))
            {
                return;
            }

            VlcPlaybackRequest request = _pendingRequest;
            _pendingRequest = null;
            _watchEntry = request.WatchEntry;
            TimeSpan? resumePosition = _watchEntry == null ? null : WatchHistory.Current.GetPosition(_watchEntry);
            _resumePositionMilliseconds = resumePosition.HasValue && resumePosition.Value > TimeSpan.Zero
                ? (long)resumePosition.Value.TotalMilliseconds
                : (long?)null;
            _resumeSeekAttempts = 0;
            _lastSavedPositionMilliseconds = null;
            _hasRecordedPlayback = false;

            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVlc, new Uri(request.Url));
            if (_vlcPlayer.Play(_currentMedia))
            {
                _vlcPlayer.Fullscreen = true;
                if (_watchEntry != null && !_watchEntry.IsLive)
                {
                    _checkpointTimer.Start();
                }
            }
        }

        private void VlcPlayer_Playing(object sender, EventArgs e)
        {
            DispatchPlayerEvent(() =>
            {
                RecordPlayback();
                TryRestorePosition();
            });
        }

        private void VlcPlayer_SeekableChanged(object sender, MediaPlayerSeekableChangedEventArgs e)
        {
            if (e.Seekable != 0)
            {
                DispatchPlayerEvent(() => TryRestorePosition());
            }
        }

        private void VlcPlayer_EndReached(object sender, EventArgs e)
        {
            DispatchPlayerEvent(CompletePlayback);
        }

        private void DispatchPlayerEvent(Action callback)
        {
            VlcMediaPlayer eventPlayer = _vlcPlayer;
            // Leave the native event callback before reading or seeking the player.
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (eventPlayer != null && ReferenceEquals(eventPlayer, _vlcPlayer))
                {
                    callback();
                }
            });
        }

        private void RecordPlayback()
        {
            if (_watchEntry != null && !_hasRecordedPlayback)
            {
                WatchHistory.Current.RecordPlayback(_watchEntry);
                _hasRecordedPlayback = true;
            }
        }

        private void TryRestorePosition(bool allowRetry = false)
        {
            if (_vlcPlayer == null || !(_resumePositionMilliseconds is long target) || !_hasRecordedPlayback || !_vlcPlayer.IsSeekable)
            {
                return;
            }

            long length = _vlcPlayer.Length;
            if (length > 0 && target >= length)
            {
                _resumePositionMilliseconds = null;
                return;
            }

            if (ConfirmResumePosition(_vlcPlayer.Time) || _vlcPlayer.State == VLCState.Ended)
            {
                return;
            }

            if (_resumeSeekAttempts == 0 || (allowRetry && _resumeSeekAttempts == 1))
            {
                _resumeSeekAttempts++;
                _vlcPlayer.Time = target;
            }
        }

        private bool ConfirmResumePosition(long actualPosition)
        {
            if (!_resumePositionMilliseconds.HasValue)
            {
                return true;
            }

            if (_resumeSeekAttempts > 0 && actualPosition >= Math.Max(1, _resumePositionMilliseconds.Value - 500))
            {
                _resumePositionMilliseconds = null;
                return true;
            }

            return false;
        }

        private void CheckpointTimer_Tick(object sender, object e)
        {
            TryRestorePosition(allowRetry: true);
            SavePlaybackPosition();
        }

        private void SavePlaybackPosition()
        {
            if (_vlcPlayer == null || _watchEntry == null)
            {
                return;
            }

            VLCState state = _vlcPlayer.State;
            if (state == VLCState.Ended)
            {
                CompletePlayback();
                return;
            }

            if (state == VLCState.Playing || state == VLCState.Paused)
            {
                RecordPlayback();
            }

            if (!_hasRecordedPlayback || _watchEntry.IsLive ||
                (state != VLCState.Playing && state != VLCState.Paused && state != VLCState.Buffering))
            {
                return;
            }

            long position = _vlcPlayer.Time;
            if (position >= 0 && position != _lastSavedPositionMilliseconds && ConfirmResumePosition(position))
            {
                WatchHistory.Current.SavePosition(_watchEntry, TimeSpan.FromMilliseconds(position));
                _lastSavedPositionMilliseconds = position;
            }
        }

        private void CompletePlayback()
        {
            if (_hasRecordedPlayback)
            {
                if (!_watchEntry.IsLive && ConfirmResumePosition(_vlcPlayer.Time))
                {
                    WatchHistory.Current.CompletePlayback(_watchEntry);
                }

                _hasRecordedPlayback = false;
                _resumeSeekAttempts = 0;
                _lastSavedPositionMilliseconds = null;
            }
        }

        private void VlcFullscreenPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isInFullScreenMode)
            {
                _isInFullScreenMode = ApplicationView.GetForCurrentView().TryEnterFullScreenMode();
            }
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
            Application.Current.Suspending += App_Suspending;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SavePlaybackPosition();
            ExitFullScreenMode();
            MainPage.Current?.CloseOverlay();
        }

        private void VlcFullscreenPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
            Application.Current.Suspending -= App_Suspending;
            ExitFullScreenMode();
            _checkpointTimer.Stop();
            SavePlaybackPosition();
            if (_vlcPlayer != null)
            {
                _vlcPlayer.Playing -= VlcPlayer_Playing;
                _vlcPlayer.SeekableChanged -= VlcPlayer_SeekableChanged;
                _vlcPlayer.EndReached -= VlcPlayer_EndReached;
                _vlcPlayer.Stop();
            }

            _currentMedia?.Dispose();
            _currentMedia = null;
            _vlcPlayer?.Dispose();
            _libVlc?.Dispose();
            _vlcPlayer = null;
            _libVlc = null;
        }

        private void App_Suspending(object sender, SuspendingEventArgs e)
        {
            SavePlaybackPosition();
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
            if (e.OriginalSource is DependencyObject source && IsInsideControlsOverlay(source))
            {
                return;
            }

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
                SavePlaybackPosition();
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
            SeekBy(-30000);
        }

        private void ForwardThirtyButton_Click(object sender, RoutedEventArgs e)
        {
            SeekBy(30000);
        }

        private void BackTwoMinutesButton_Click(object sender, RoutedEventArgs e)
        {
            SeekBy(-120000);
        }

        private void ForwardTwoMinutesButton_Click(object sender, RoutedEventArgs e)
        {
            SeekBy(120000);
        }

        private void SeekBy(long offsetMilliseconds)
        {
            if (_vlcPlayer == null || !_vlcPlayer.IsSeekable)
            {
                return;
            }

            _resumePositionMilliseconds = null;
            long length = _vlcPlayer.Length;
            long target = Math.Max(0, _vlcPlayer.Time + offsetMilliseconds);
            _vlcPlayer.Time = length > 0 ? Math.Min(length, target) : target;
        }

        private bool IsInsideControlsOverlay(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, ControlsOverlay))
                {
                    return true;
                }

                current = Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
