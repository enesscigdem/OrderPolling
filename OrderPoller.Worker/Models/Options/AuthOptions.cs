namespace OrderPoller.Worker.Models.Options;

public sealed class AuthOptions
{
    public string TokenUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string? Scope { get; set; }
}