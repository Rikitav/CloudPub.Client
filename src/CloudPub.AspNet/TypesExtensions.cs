// The MIT License (MIT)
// 
// CloudPub.Client
// Copyright 2026 © Rikitav Tim4ik
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the “Software”), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using CloudPub.Components;
using CloudPub.Options;
using CloudPub.Protocol;
using CloudPub.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CloudPub;

/// <summary>
/// Service collection extensions for registering CloudPub client integrations in ASP.NET applications.
/// </summary>
public static class ServiceCollectionExtensions
{
    private static ICloudPubClient ClientFactory(IServiceProvider serviceProvider)
    {
        CloudPubClientOptions options = serviceProvider.GetRequiredService<IOptions<CloudPubClientOptions>>().Value;
        ICloudPubRules rules = serviceProvider.GetRequiredService<ICloudPubRules>();
        return new CloudPubClient(options, rules);
    }

    /// <summary>
    /// Registers CloudPub using <see cref="IOptions{TOptions}"/> bound <see cref="CloudPubClientOptions"/>.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    /// <returns>A fluent CloudPub builder.</returns>
    public static ICloudPubClientBuilder AddCloudPub(this IServiceCollection services)
    {
        AddCloudPubCore(services, static sp => ClientFactory(sp));
        return new CloudPubClientBuilder(services);
    }

    /// <summary>
    /// Registers CloudPub with a custom client factory.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    /// <param name="factory">Factory used to create the singleton <see cref="ICloudPubClient"/>.</param>
    /// <returns>A fluent CloudPub builder.</returns>
    public static ICloudPubClientBuilder AddCloudPub(this IServiceCollection services, Func<IServiceProvider, ICloudPubClient> factory)
    {
        AddCloudPubCore(services, factory);
        return new CloudPubClientBuilder(services);
    }

    /// <summary>
    /// Registers CloudPub with explicit client options.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    /// <param name="options">Options used to initialize <see cref="CloudPubClient"/>.</param>
    /// <returns>A fluent CloudPub builder.</returns>
    public static ICloudPubClientBuilder AddCloudPub(this IServiceCollection services, CloudPubClientOptions options)
    {
        AddCloudPubCore(services, sp => new CloudPubClient(options, sp.GetRequiredService<ICloudPubRules>()));
        return new CloudPubClientBuilder(services);
    }

    /// <summary>
    /// Registers CloudPub and binds client options from configuration.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    /// <param name="optionsConfiguration">Configuration section containing client options.</param>
    /// <returns>A fluent CloudPub builder.</returns>
    public static ICloudPubClientBuilder AddCloudPub(this IServiceCollection services, IConfiguration optionsConfiguration)
    {
        services.Configure<CloudPubClientOptions>(optionsConfiguration);
        AddCloudPubCore(services, static sp => ClientFactory(sp));
        return new CloudPubClientBuilder(services);
    }

    private static void AddCloudPubCore(IServiceCollection services, Func<IServiceProvider, ICloudPubClient> clientFactory)
    {
        services.TryAddSingleton<ICloudPubRules, CloudPubRules>();
        services.TryAddSingleton(new CloudPubHostingState());
        services.Replace(ServiceDescriptor.Singleton(clientFactory));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HostedCloudPubLifetimeService>());
    }
}

/// <summary>
/// Convenience methods for endpoint registration on <see cref="ICloudPubClientBuilder"/>.
/// </summary>
public static class CloudPubClientBuilderExtensions
{
    /// <summary>
    /// Adds a preconfigured CloudPub endpoint to the builder.
    /// </summary>
    /// <param name="builder">CloudPub builder.</param>
    /// <param name="publishOptions">Endpoint options to publish.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static ICloudPubClientBuilder AddEndpoint(this ICloudPubClientBuilder builder, CloudPubPublishOptions publishOptions)
        => builder.AddPublishEndpoint(publishOptions);

    /// <summary>
    /// Adds an endpoint by local TCP port.
    /// </summary>
    /// <param name="builder">CloudPub builder.</param>
    /// <param name="port">Local TCP port.</param>
    /// <param name="name">Optional endpoint label.</param>
    /// <param name="protocolType">Protocol type to publish.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static ICloudPubClientBuilder Endpoint(this ICloudPubClientBuilder builder, ushort port, string? name = null, ProtocolType protocolType = ProtocolType.Http)
        => builder.AddEndpoint(port, name, protocolType);

    /// <summary>
    /// Adds an endpoint by custom local address string.
    /// </summary>
    /// <param name="builder">CloudPub builder.</param>
    /// <param name="address">Local address string.</param>
    /// <param name="name">Optional endpoint label.</param>
    /// <param name="protocolType">Protocol type to publish.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static ICloudPubClientBuilder Endpoint(this ICloudPubClientBuilder builder, string address, string? name = null, ProtocolType protocolType = ProtocolType.Http)
        => builder.AddEndpoint(address, name, protocolType);

    /// <summary>
    /// Adds an endpoint with protocol defaults for local address.
    /// </summary>
    /// <param name="builder">CloudPub builder.</param>
    /// <param name="name">Optional endpoint label.</param>
    /// <param name="protocolType">Protocol type to publish.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static ICloudPubClientBuilder Endpoint(this ICloudPubClientBuilder builder, string? name = null, ProtocolType protocolType = ProtocolType.Http)
        => builder.AddEndpoint(name, protocolType);
}

/// <summary>
/// Runtime snapshot of CloudPub configuration and published endpoint state.
/// </summary>
public sealed class CloudPubContext
{
    private readonly ICloudPubClient _client;
    private readonly CloudPubHostingState _hostingState;

    internal CloudPubContext(ICloudPubClient client, CloudPubHostingState hostingState)
    {
        _client = client;
        _hostingState = hostingState;
    }

    /// <summary>
    /// Gets the endpoints configured before startup.
    /// </summary>
    public IEnumerable<CloudPubPublishOptions> PublishOptions => _hostingState.PublishOptions.AsReadOnly();

    /// <summary>
    /// Gets endpoints successfully published during application lifetime.
    /// </summary>
    public IEnumerable<Endpoint> PublishedEndpoints => _hostingState.PublishedEndpoints.AsReadOnly();

    /// <summary>
    /// Gets currently configured proxy mode.
    /// </summary>
    public CloudPubProxyMode ProxyMode => _hostingState.ProxyMode;

    /// <summary>
    /// Gets the CloudPub client instance to interact with the service and query runtime state.
    /// </summary>
    public ICloudPubClient Client => _client;
}

/// <summary>
/// Host extensions for executing CloudPub callbacks when the app is started.
/// </summary>
public static class WebApplicationHostExtensions
{
    /// <summary>
    /// Registers a callback invoked when the host reaches the started state.
    /// </summary>
    /// <typeparam name="T">Host type.</typeparam>
    /// <param name="app">Host instance.</param>
    /// <param name="useAction">Callback that receives a <see cref="CloudPubContext"/> snapshot.</param>
    /// <returns>The same host instance for chaining.</returns>
    public static T OnCloudPubStarted<T>(this T app, Action<CloudPubContext> useAction) where T : IHost
    {
        HostedCloudPubLifetimeService lifetime = app.Services.GetServices<IHostedService>().OfType<HostedCloudPubLifetimeService>().FirstOrDefault()
            ?? throw new InvalidOperationException("CloudPubHostingState not found in services.");

        lifetime.OnStarted(useAction);
        return app;
    }

    /// <summary>
    /// Registers a callback invoked when the host is stopping or has stopped.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="app"></param>
    /// <param name="useAction"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static T OnCloudPubStopped<T>(this T app, Action<CloudPubContext> useAction) where T : IHost
    {
        HostedCloudPubLifetimeService lifetime = app.Services.GetServices<IHostedService>().OfType<HostedCloudPubLifetimeService>().FirstOrDefault()
            ?? throw new InvalidOperationException("CloudPubHostingState not found in services.");

        lifetime.OnStopped(useAction);
        return app;
    }
}
