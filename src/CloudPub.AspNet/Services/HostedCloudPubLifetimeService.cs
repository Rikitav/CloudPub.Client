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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Endpoint = CloudPub.Protocol.Endpoint;

namespace CloudPub.Services;

internal sealed class HostedCloudPubLifetimeService(
    ILogger<HostedCloudPubLifetimeService> logger,
    ICloudPubClient client,
    CloudPubHostingState state) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("CloudPub lifetime service starting with {count} endpoint(s)", state.PublishOptions.Count);
        Debug.WriteLine($"CloudPub lifetime start, endpoints={state.PublishOptions.Count}, proxyMode={state.ProxyMode}");

        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("CloudPub client connected");

        foreach (CloudPubPublishOptions publishOptions in state.PublishOptions)
        {
            try
            {
                Endpoint registeredEndpoint = await client.PublishAsync(publishOptions, cancellationToken).ConfigureAwait(false);
                state.PublishedEndpoints.Add(registeredEndpoint);

                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Now listening on '{url}'", registeredEndpoint.Url);
                Debug.WriteLine($"CloudPub endpoint published: guid={registeredEndpoint.Guid}, url={registeredEndpoint.Url}");
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                    logger.LogError(ex, "Failed to publish endpoint {url} during startup", publishOptions.Address);

                throw;
            }
        }
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("CloudPub lifetime service stopping; unpublishing {count} endpoint(s)", state.PublishedEndpoints.Count);

        foreach (Endpoint endpoint in state.PublishedEndpoints.ToArray())
        {
            try
            {
                await client.UnpublishAsync(endpoint, cancellationToken).ConfigureAwait(false);
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Successfully unpublished endpoint {url}", endpoint.Url);
                Debug.WriteLine($"CloudPub endpoint unpublished: guid={endpoint.Guid}, url={endpoint.Url}");
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                    logger.LogError(ex, "Failed to unpublish endpoint {url} during shutdown", endpoint.Url);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
