using CloudPub.Components;
using CloudPub.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Endpoint = CloudPub.Protocol.Endpoint;

namespace CloudPub;

/// <summary>
/// Hosted service that connects the CloudPub client during host startup, publishes configured endpoints,
/// adds their public URLs to <see cref="Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature"/>,
/// and unpublishes them on shutdown.
/// </summary>
/// <param name="logger">Logger for publish/unpublish events.</param>
/// <param name="client">CloudPub client registered in DI.</param>
/// <param name="server">The ASP.NET Core server whose address list is updated with published URLs.</param>
/// <param name="serviceProvider">Used to resolve registered <see cref="CloudPub.Options.CloudPubPublishOptions"/> instances.</param>
public class HostedCloudPubLifecycleService(ILogger<HostedCloudPubLifecycleService> logger, ICloudPubClient client, IServer server, IServiceProvider serviceProvider) : IHostedLifecycleService
{
    private readonly List<Endpoint> registeredEndpoints = [];

    /// <summary>
    /// Gets the collection of registered endpoints as an array.
    /// </summary>
    /// <remarks>
    /// The returned array contains all endpoints that have been registered.
    /// Modifications to the collection of endpoints after retrieval will not affect the returned array.
    /// </remarks>
    public IEnumerable<Endpoint> Endpoints => registeredEndpoints.ToArray();

    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        IServerAddressesFeature? addressesFeature = server.Features.Get<IServerAddressesFeature>();
        if (addressesFeature == null)
            throw new InvalidOperationException("Сервер не поддерживает IServerAddressesFeature.");

        await client.ConnectAsync(cancellationToken);
        foreach (CloudPubPublishOptions publishOptions in serviceProvider.GetServices<CloudPubPublishOptions>())
        {
            try
            {
                Endpoint registeredEndpoint = await client.PublishAsync(publishOptions, cancellationToken);
                addressesFeature.Addresses.Add(registeredEndpoint.Url);

                registeredEndpoints.Add(registeredEndpoint);
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Now listening on '{url}'", registeredEndpoint.Url);
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                    logger.LogError(ex, "Failed to publish endpoint {url} during startup", publishOptions.Address);

                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        foreach (Endpoint endpoint in registeredEndpoints)
        {
            try
            {
                await client.UnpublishAsync(endpoint, cancellationToken);
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Successfully unpublished endpoint {url}", endpoint.Url);
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                    logger.LogError(ex, "Failed to unpublish endpoint {url} during shutdown", endpoint.Url);
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
