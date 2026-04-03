using CloudPub.Components;
using CloudPub.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Endpoint = Protocol.Endpoint;

namespace CloudPub;
 
public class HostedCloudPubLifecycleService(ILogger<HostedCloudPubLifecycleService> logger, ICloudPubClient client, IServer server, IServiceProvider serviceProvider) : IHostedLifecycleService
{
    private List<Endpoint> registeredEndpoints = [];

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
                logger.LogInformation("Now listening on '{url}'", registeredEndpoint.Url);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish endpoint {url} during startup", publishOptions.Address);
                throw;
            }
        }
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        foreach (Endpoint endpoint in registeredEndpoints)
        {
            try
            {
                await client.UnpublishAsync(endpoint, cancellationToken);
                logger.LogInformation("Successfully unpublished endpoint {url}", endpoint.Url);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to unpublish endpoint {url} during shutdown", endpoint.Url);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
