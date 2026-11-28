using Windows.ApplicationModel.Activation;
using Windows.System.Display;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace IptvXbox.App
{
    sealed partial class App : Application
    {
        private DisplayRequest _displayRequest;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            if (e.PrelaunchActivated)
            {
                return;
            }

            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                rootFrame.Navigate(typeof(MainPage), e.Arguments);
            }

            Window.Current.Activate();

            // Windows deactivates this request when the app is minimized and restores it when shown again.
            if (_displayRequest == null)
            {
                var displayRequest = new DisplayRequest();
                displayRequest.RequestActive();
                _displayRequest = displayRequest;
            }
        }
    }
}
