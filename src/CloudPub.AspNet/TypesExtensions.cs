using CloudPub.Components;
using CloudPub.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using CloudPub.Protocol;

namespace CloudPub;

/// <summary>
/// Dependency injection registration helpers for CloudPub client and publish endpoints.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CloudPubClient"/> as <see cref="CloudPub.Components.ICloudPubClient"/> using
    /// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> of <see cref="CloudPub.Options.CloudPubClientOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddCloudPub(this IServiceCollection services)
    {
        services.AddSingleton<ICloudPubClient, CloudPubClient>(sp => new CloudPubClient(sp.GetRequiredService<IOptions<CloudPubClientOptions>>().Value));
        services.AddHostedService<HostedCloudPubLifecycleService>();
        return services;
    }

    /// <summary>
    /// Registers a custom factory that produces the singleton <see cref="CloudPub.Components.ICloudPubClient"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="factory">Factory that creates the client instance.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddCloudPub(this IServiceCollection services, Func<IServiceProvider, ICloudPubClient> factory)
    {
        services.AddSingleton(factory);
        services.AddHostedService<HostedCloudPubLifecycleService>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="CloudPubClient"/> as <see cref="CloudPub.Components.ICloudPubClient"/> with explicit options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Client options instance (not bound from configuration).</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddCloudPub(this IServiceCollection services, CloudPubClientOptions options)
    {
        services.AddSingleton<ICloudPubClient, CloudPubClient>(sp => new CloudPubClient(options));
        services.AddHostedService<HostedCloudPubLifecycleService>();
        return services;
    }

    /// <summary>
    /// Binds <see cref="CloudPub.Options.CloudPubClientOptions"/> from configuration and registers <see cref="CloudPubClient"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsConfiguration">Configuration section or root for client options.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddCloudPub(this IServiceCollection services, IConfiguration optionsConfiguration)
    {
        services.Configure<CloudPubClientOptions>(optionsConfiguration);
        services.AddSingleton<ICloudPubClient, CloudPubClient>(sp => new CloudPubClient(sp.GetRequiredService<IOptions<CloudPubClientOptions>>().Value));
        services.AddHostedService<HostedCloudPubLifecycleService>();
        return services;
    }

    /// <summary>
    /// Registers a publish profile so <see cref="HostedCloudPubLifecycleService"/> publishes it at application startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="publishOptions">Publish options.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddPublishEndpoint(this IServiceCollection services, CloudPubPublishOptions publishOptions)
    {
        services.TryAddSingleton<IEnumerable<CloudPubPublishOptions>>(instance: [publishOptions]);
        return services;
    }

    /// <summary>
    /// Registers an HTTP (or other) endpoint on <c>localhost</c> at the given port.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="port">Local TCP port to expose.</param>
    /// <param name="name">Optional description for the published endpoint.</param>
    /// <param name="protocolType">Application protocol; defaults to HTTP.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddPublishEndpoint(this IServiceCollection services, ushort port, string? name = null, ProtocolType protocolType = ProtocolType.Http)
    {
        return services.AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Address = port.ToString(),
            Auth = AuthType.None,
            Name = name ?? string.Empty,
        });
    }

    /// <summary>
    /// Registers a publish profile using a string address (port, host:port, path, or URL depending on protocol).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="address">Local bind specification as accepted by <see cref="CloudPub.CloudPubClientOptionsExtensions.CreateCleintEndpoint(CloudPub.Options.CloudPubPublishOptions)"/>.</param>
    /// <param name="name">Optional description for the published endpoint.</param>
    /// <param name="protocolType">Application protocol; defaults to HTTP.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddPublishEndpoint(this IServiceCollection services, string address, string? name = null, ProtocolType protocolType = ProtocolType.Http)
    {
        return services.AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Address = address ?? throw new ArgumentNullException(nameof(address)),
            Auth = AuthType.None,
            Name = name ?? string.Empty,
        });
    }

    /// <summary>
    /// Registers a publish profile with empty address; useful when the address is supplied elsewhere or protocol-specific defaults apply.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Optional description for the published endpoint.</param>
    /// <param name="protocolType">Application protocol; defaults to HTTP.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddPublishEndpoint(this IServiceCollection services, string? name = null, ProtocolType protocolType = ProtocolType.Http)
    {
        return services.AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Auth = AuthType.None,
            Name = name ?? string.Empty,
        });
    }

    /// <summary>
    /// Registers a publish profile so <see cref="HostedCloudPubLifecycleService"/> publishes it at application startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="builder">Endpoint builder.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddPublishEndpoint(this IServiceCollection services, Action<ICloudPubEndpointsBuilder> builder)
    {
        CloudPubEndpointsBuilder endPointsBuilder = new CloudPubEndpointsBuilder();
        builder.Invoke(endPointsBuilder);

        services.TryAddSingleton<IEnumerable<CloudPubPublishOptions>>(instance: endPointsBuilder.Endpoints.ToArray());
        return services;
    }
}

/// <summary>
/// Provides extension methods for registering publish endpoints with a cloud publishing service builder.
/// </summary>
/// <remarks>
/// These extension methods enable the configuration of publish endpoints using various specifications, such as local TCP ports, string addresses, or protocol-specific defaults.
/// They support optional naming and protocol type selection, allowing for flexible endpoint registration in cloud-based applications.
/// </remarks>
public static class CloudPubEndpointsBuilderExtensions
{
    /// <summary>
    /// Registers an HTTP (or other) endpoint on <c>localhost</c> at the given port.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="port">Local TCP port to expose.</param>
    /// <param name="name">Optional description for the published endpoint.</param>
    /// <param name="protocolType">Application protocol; defaults to HTTP.</param>
    /// <returns>The same collection for chaining.</returns>
    public static ICloudPubEndpointsBuilder AddPublishEndpoint(this ICloudPubEndpointsBuilder services, ushort port, string? name = null, ProtocolType protocolType = ProtocolType.Http)
    {
        return services.AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Address = port.ToString(),
            Auth = AuthType.None,
            Name = name ?? string.Empty,
        });
    }

    /// <summary>
    /// Registers a publish profile using a string address (port, host:port, path, or URL depending on protocol).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="address">Local bind specification as accepted by <see cref="CloudPub.CloudPubClientOptionsExtensions.CreateCleintEndpoint(CloudPub.Options.CloudPubPublishOptions)"/>.</param>
    /// <param name="name">Optional description for the published endpoint.</param>
    /// <param name="protocolType">Application protocol; defaults to HTTP.</param>
    /// <returns>The same collection for chaining.</returns>
    public static ICloudPubEndpointsBuilder AddPublishEndpoint(this ICloudPubEndpointsBuilder services, string address, string? name = null, ProtocolType protocolType = ProtocolType.Http)
    {
        return services.AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Address = address ?? throw new ArgumentNullException(nameof(address)),
            Auth = AuthType.None,
            Name = name ?? string.Empty,
        });
    }

    /// <summary>
    /// Registers a publish profile with empty address; useful when the address is supplied elsewhere or protocol-specific defaults apply.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Optional description for the published endpoint.</param>
    /// <param name="protocolType">Application protocol; defaults to HTTP.</param>
    /// <returns>The same collection for chaining.</returns>
    public static ICloudPubEndpointsBuilder AddPublishEndpoint(this ICloudPubEndpointsBuilder services, string? name = null, ProtocolType protocolType = ProtocolType.Http)
    {
        return services.AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Auth = AuthType.None,
            Name = name ?? string.Empty,
        });
    }
}
