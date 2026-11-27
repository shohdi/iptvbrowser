using IptvXbox.App.Models;
using System;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace IptvXbox.App.Services
{
    public sealed class BuiltinPlayback : IDisposable
    {
        private readonly CoreDispatcher _dispatcher = Window.Current.Dispatcher;
        private readonly DispatcherTimer _checkpointTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private MediaSource _source;
        private WatchHistoryEntry _entry;
        private TimeSpan? _resumePosition;
        private bool _resumeSeekAttempted;
        private bool _recorded;

        public MediaPlayer Player { get; } = new MediaPlayer();

        public BuiltinPlayback()
        {
            Player.MediaOpened += Player_MediaOpened;
            Player.MediaEnded += Player_MediaEnded;
            Player.PlaybackSession.PlaybackStateChanged += Session_PlaybackStateChanged;
            Player.PlaybackSession.SeekCompleted += Session_SeekCompleted;
            _checkpointTimer.Tick += CheckpointTimer_Tick;
        }

        public void Play(string url, WatchHistoryEntry entry)
        {
            Stop();
            _entry = entry;
            _resumePosition = WatchHistory.Current.GetPosition(entry);
            _source = MediaSource.CreateFromUri(new Uri(url));
            Application.Current.Suspending += Application_Suspending;
            Player.Source = _source;
            Player.Play();
        }

        public void Stop()
        {
            MediaSource source = _source;
            if (source == null)
            {
                return;
            }

            SavePosition();
            _checkpointTimer.Stop();
            Application.Current.Suspending -= Application_Suspending;
            _source = null;
            _entry = null;
            _resumePosition = null;
            _resumeSeekAttempted = false;
            _recorded = false;
            Player.Pause();
            Player.Source = null;
            source.Dispose();
        }

        public void Dispose()
        {
            Stop();
            _checkpointTimer.Tick -= CheckpointTimer_Tick;
            Player.MediaOpened -= Player_MediaOpened;
            Player.MediaEnded -= Player_MediaEnded;
            Player.PlaybackSession.PlaybackStateChanged -= Session_PlaybackStateChanged;
            Player.PlaybackSession.SeekCompleted -= Session_SeekCompleted;
            Player.Dispose();
        }

        private void Player_MediaOpened(MediaPlayer sender, object args) => OnCurrentSource(TryResume);

        private void Player_MediaEnded(MediaPlayer sender, object args) => OnCurrentSource(() =>
        {
            if (_recorded)
            {
                WatchHistory.Current.CompletePlayback(_entry);
                _recorded = false;
            }

            _resumePosition = null;
            _checkpointTimer.Stop();
        });

        private void Session_PlaybackStateChanged(MediaPlaybackSession sender, object args) => OnCurrentSource(() =>
        {
            switch (sender.PlaybackState)
            {
                case MediaPlaybackState.Playing:
                    TryResume();
                    if (!_recorded && _entry != null)
                    {
                        WatchHistory.Current.RecordPlayback(_entry);
                        _recorded = true;
                        if (!_entry.IsLive)
                        {
                            _checkpointTimer.Start();
                        }
                    }
                    break;
                case MediaPlaybackState.Paused:
                    SavePosition();
                    break;
            }
        });

        private void Session_SeekCompleted(MediaPlaybackSession sender, object args) => OnCurrentSource(() =>
        {
            TimeSpan position = sender.Position;
            if (_resumeSeekAttempted && _resumePosition.HasValue && position > TimeSpan.Zero &&
                position >= _resumePosition.Value - TimeSpan.FromSeconds(1))
            {
                _resumePosition = null;
            }

            SavePosition();
        });

        private void Application_Suspending(object sender, SuspendingEventArgs e) => SavePosition();

        private void CheckpointTimer_Tick(object sender, object args)
        {
            if (Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                TryResume();
                SavePosition();
            }
        }

        private void TryResume()
        {
            MediaPlaybackSession session = Player.PlaybackSession;
            if (!_resumePosition.HasValue || _resumeSeekAttempted || !session.CanSeek)
            {
                return;
            }

            if (session.NaturalDuration > TimeSpan.Zero && _resumePosition.Value >= session.NaturalDuration)
            {
                _resumePosition = null;
                WatchHistory.Current.CompletePlayback(_entry);
                return;
            }

            try
            {
                session.Position = _resumePosition.Value;
                _resumeSeekAttempted = true;
            }
            catch (COMException)
            {
                // Some streams report seeking support before the player is ready to seek.
            }
        }

        private void SavePosition()
        {
            MediaPlaybackSession session = Player.PlaybackSession;
            if (!_recorded || _entry.IsLive || session.PlaybackState == MediaPlaybackState.None ||
                session.PlaybackState == MediaPlaybackState.Opening)
            {
                return;
            }

            TimeSpan position = session.Position;
            if (_resumePosition.HasValue && position < _resumePosition.Value)
            {
                return;
            }

            _resumePosition = null;
            WatchHistory.Current.SavePosition(_entry, position);
        }

        private void OnCurrentSource(Action action)
        {
            MediaSource source = _source;
            if (source == null)
            {
                return;
            }

            _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (_source == source)
                {
                    action();
                }
            });
        }
    }
}
