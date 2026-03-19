using LibVLCSharp.Shared;

namespace IptvXbox.App.Services
{
    public static class VlcPlaybackService
    {
        private static readonly object SyncRoot = new object();
        private static LibVLC _sharedLibVlc;

        public static LibVLC SharedLibVlc
        {
            get
            {
                lock (SyncRoot)
                {
                    if (_sharedLibVlc == null)
                    {
                        Core.Initialize();
                        _sharedLibVlc = new LibVLC("--network-caching=1000");
                    }

                    return _sharedLibVlc;
                }
            }
        }
    }
}
