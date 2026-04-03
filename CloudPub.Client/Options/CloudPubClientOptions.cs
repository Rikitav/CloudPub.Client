namespace CloudPub.Options;

public sealed class CloudPubClientOptions
{
    public Uri ServerUri { get; set; } = new Uri("https://cloudpub.ru");
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool ResumeEndpointsOnConnect { get; set; } = true;

    public string? Token { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }

    public string? AgentId { get; set; }
    public string? Hwid { get; set; }
    public string? ClientVersion { get; set; }
}
