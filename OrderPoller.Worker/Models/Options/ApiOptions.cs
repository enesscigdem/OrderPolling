namespace OrderPoller.Worker.Models.Options;

public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = "";
    public string OrdersPath { get; set; } = "/orders";
}