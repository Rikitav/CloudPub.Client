using Protocol;

namespace CloudPub.Options;

public class CloudPubPublishOptions
{
    public ProtocolType Protocol { get; set; } = ProtocolType.Https;
    public AuthType? Auth { get; set; } = null;
    public string Address { get; set; } = "";
    public string Name { get; set; } = "";

    public IReadOnlyCollection<Acl>? Acl { get; set; }
    public IReadOnlyCollection<Header>? Headers { get; set; }
    public IReadOnlyCollection<FilterRule>? Rules { get; set; }
}
