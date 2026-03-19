using IptvXbox.App.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace IptvXbox.App
{
    public sealed partial class LoginPage : Page
    {
        private readonly AppSession _session = AppSession.Current;

        public LoginPage()
        {
            InitializeComponent();
            Loaded += LoginPage_Loaded;
            Unloaded += LoginPage_Unloaded;
        }

        private async void LoginPage_Loaded(object sender, RoutedEventArgs e)
        {
            _session.PropertyChanged += Session_PropertyChanged;
            LoadSavedValues();
            UpdateStatus();
            await _session.LoadInitialLocalDataAsync();
            UpdateStatus();
        }

        private void LoginPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _session.PropertyChanged -= Session_PropertyChanged;
        }

        private void Session_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSession.StatusMessage) ||
                e.PropertyName == nameof(AppSession.IsCatalogLoaded) ||
                e.PropertyName == nameof(AppSession.AllItems) ||
                e.PropertyName == nameof(AppSession.Connection))
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, UpdateStatus);
            }
        }

        private void LoadSavedValues()
        {
            ServerTextBox.Text = _session.Connection.Server;
            UsernameTextBox.Text = _session.Connection.Username;
            PasswordTextBox.Password = _session.Connection.Password;
        }

        private async void RefreshApiButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_session.TryUpdateConnection(ServerTextBox.Text, UsernameTextBox.Text, PasswordTextBox.Password, out string errorMessage))
            {
                StatusTextBlock.Text = errorMessage;
                return;
            }

            try
            {
                await _session.RefreshFromApiAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"API refresh failed: {ex.Message}";
            }

            UpdateStatus();
        }

        private async void LoadLocalFileButton_Click(object sender, RoutedEventArgs e)
        {
            _session.TryUpdateConnection(ServerTextBox.Text, UsernameTextBox.Text, PasswordTextBox.Password, out _);

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(Path.Combine(Package.Current.InstalledLocation.Path, "channels.json"));
                await _session.LoadCatalogFromFileAsync(file);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Unable to load channels.json: {ex.Message}";
            }

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            StatusTextBlock.Text = _session.StatusMessage;
            LoadedSummaryTextBlock.Text = _session.IsCatalogLoaded
                ? $"{_session.AllItems.Count:N0} items are loaded and ready for Browse and Search."
                : "No catalog loaded yet.";
        }
    }
}
