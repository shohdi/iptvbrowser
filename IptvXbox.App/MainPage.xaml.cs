using Windows.UI.Xaml.Controls;

namespace IptvXbox.App
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
            Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (ContentFrame.Content == null)
            {
                RootNavigationView.SelectedItem = RootNavigationView.MenuItems[0];
                ContentFrame.Navigate(typeof(LoginPage));
            }
        }

        private void RootNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (!(args.SelectedItemContainer?.Tag is string tag))
            {
                return;
            }

            switch (tag)
            {
                case "login":
                    Navigate(typeof(LoginPage));
                    break;
                case "browse":
                    Navigate(typeof(BrowsePage));
                    break;
                case "search":
                    Navigate(typeof(SearchPage));
                    break;
            }
        }

        private void Navigate(System.Type pageType)
        {
            if (ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
