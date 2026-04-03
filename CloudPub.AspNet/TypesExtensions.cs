using CloudPub.Components;
using CloudPub.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Protocol;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CloudPub;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCloudPub(this IServiceCollection services)
    {
        services.AddSingleton<ICloudPubClient, CloudPubClient>(sp => new CloudPubClient(sp.GetRequiredService<IOptions<CloudPubClientOptions>>().Value));
        return services;
    }

    public static IServiceCollection AddCloudPub(this IServiceCollection services, Func<IServiceProvider, ICloudPubClient> factory)
    {
        services.AddSingleton(factory);
        return services;
    }

    public static IServiceCollection AddCloudPub(this IServiceCollection services, CloudPubClientOptions options)
    {
        services.AddSingleton<ICloudPubClient, CloudPubClient>(sp => new CloudPubClient(options));
        return services;
    }

    public static IServiceCollection AddCloudPub(this IServiceCollection services, IConfiguration optionsConfiguration)
    {
        services.Configure<CloudPubClientOptions>(optionsConfiguration);
        services.AddSingleton<ICloudPubClient, CloudPubClient>(sp => new CloudPubClient(sp.GetRequiredService<IOptions<CloudPubClientOptions>>().Value));
        return services;
    }

    public static IServiceCollection AddPublishEndpoint(this IServiceCollection services, CloudPubPublishOptions options)
    {
        services.TryAddEnumerable(new ServiceDescriptor(typeof(CloudPubPublishOptions), options, ServiceLifetime.Singleton));
        return services;
    }

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

    public static IServiceCollection AddPublishEndpoint(this IServiceCollection services, string address, string? name = null, ProtocolType protocolType = ProtocolType.Http)
    {
        return services.AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Address = address ?? throw new ArgumentNullException(),
            Auth = AuthType.None,
            Name = name ?? string.Empty,
        });
    }

    public static IServiceCollection AddPublishEndpoint(this IServiceCollection services, string? name = null, ProtocolType protocolType = ProtocolType.Http)
    {
        return services.AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Auth = AuthType.None,
            Name = name ?? string.Empty,
        });
    }
}
