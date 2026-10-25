namespace StatsHub.Api.IbbaScraping;

/// <summary>
/// IBBA's page or spreadsheet no longer looks the way the parser expects (a
/// renamed column, a missing table header, a date in another format). Thrown
/// instead of returning half-read data: a sync that hits it records the error
/// on the link and saves nothing, rather than storing zeros, wrong dates or a
/// team read off the wrong link.
/// </summary>
public class IbbaLayoutException : Exception
{
    public const string UserMessage = "IBBA changed how their site looks, so this couldn't be read. Nothing was changed - it will work again once we update.";

    public string Details { get; }

    public IbbaLayoutException(string details) : base($"{UserMessage} ({details})") => Details = details;
}
