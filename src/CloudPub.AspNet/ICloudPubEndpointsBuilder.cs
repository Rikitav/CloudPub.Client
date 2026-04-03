using CloudPub.Options;

namespace CloudPub;

/// <summary>
/// Provides a contract for configuring and adding publish endpoints for cloud-based messaging services.
/// </summary>
/// <remarks>
/// Implementations of this interface allow developers to specify options for publishing messages to cloud services.
/// Endpoints added through this builder should be properly configured to ensure reliable message delivery.</remarks>
public interface ICloudPubEndpointsBuilder
{
    /// <summary>
    /// Adds a publish endpoint to the builder using the specified cloud publishing options.
    /// </summary>
    /// <remarks>
    /// Use this method to configure cloud publishing for the application.
    /// Ensure that all required properties of the options parameter are set to valid values to avoid configuration errors at runtime.
    /// </remarks>
    /// <param name="publishOptions">The options that configure the publish endpoint, including target environment, authentication, and other publishing settings. Cannot be null.</param>
    ICloudPubEndpointsBuilder AddPublishEndpoint(CloudPubPublishOptions publishOptions);
}
