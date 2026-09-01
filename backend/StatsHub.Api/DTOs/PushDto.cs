namespace StatsHub.Api.DTOs
{
    public class SubscribePushDto
    {
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
    }

    public class UnsubscribePushDto
    {
        public string Endpoint { get; set; } = string.Empty;
    }
}
