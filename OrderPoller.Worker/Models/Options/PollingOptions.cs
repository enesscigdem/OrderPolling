namespace OrderPoller.Worker.Models.Options;

public sealed class PollingOptions
{
    public int PeriodSeconds { get; set; } = 300;
    public int TokenRefreshSkewSeconds { get; set; } = 60;
    public int MaxTokenRequestsPerHour { get; set; } = 5;
}