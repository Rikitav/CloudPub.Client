using CloudPub.Options;

namespace CloudPub;

/// <inheritdoc/>
public class CloudPubEndpointsBuilder : ICloudPubEndpointsBuilder
{
    /// <summary>
    /// Final list of endpoints.
    /// </summary>
    public readonly List<CloudPubPublishOptions> Endpoints = [];

    /// <inheritdoc/>
    public ICloudPubEndpointsBuilder AddPublishEndpoint(CloudPubPublishOptions publishOptions)
    {
        Endpoints.Add(publishOptions);
        return this;
    }
}
