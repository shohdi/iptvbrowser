namespace IptvXbox.App.Models
{
    public sealed class ConnectionSettings
    {
        public string Server { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(Server) &&
            !string.IsNullOrWhiteSpace(Username) &&
            !string.IsNullOrWhiteSpace(Password);
    }
}
