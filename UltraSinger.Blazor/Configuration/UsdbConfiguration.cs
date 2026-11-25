namespace UltraSinger.Blazor.Configuration;

public class UsdbConfiguration
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string BaseUrl { get; set; } = "https://usdb.animux.de";
}
