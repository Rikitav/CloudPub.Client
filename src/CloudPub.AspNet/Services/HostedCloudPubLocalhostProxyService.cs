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

using CloudPub.Protocol;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudPub.Services;

/// <summary>
/// Hosted service that projects configured localhost proxy endpoints into the host's address list.
/// </summary>
/// <param name="logger">Logger for address registration lifecycle.</param>
/// <param name="server">The ASP.NET Core server whose address list is updated with published URLs.</param>
/// <param name="state">CloudPub hosting state with configured publish options.</param>
internal sealed class HostedCloudPubLocalhostProxyService(
    ILogger<HostedCloudPubLocalhostProxyService> logger,
    IServer server,
    CloudPubHostingState state) : IHostedLifecycleService
{
    private readonly List<string> _registeredAddresses = [];

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        IServerAddressesFeature? addressesFeature = server.Features.Get<IServerAddressesFeature>();
        if (addressesFeature == null)
            throw new InvalidOperationException("Server does not support IServerAddressesFeature.");

        foreach (var publishOptions in state.PublishOptions)
        {
            try
            {
                ClientEndpoint clientEndpoint = publishOptions.CreateCleintEndpoint();
                string address = clientEndpoint.ToLocalEndpointUrl();
                addressesFeature.Addresses.Add(address);
                _registeredAddresses.Add(address);

                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Added localhost proxy address '{url}'", address);
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                    logger.LogError(ex, "Failed to add localhost proxy address for {url}", publishOptions.Address);

                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        IServerAddressesFeature? addressesFeature = server.Features.Get<IServerAddressesFeature>();
        if (addressesFeature == null)
            return Task.CompletedTask;

        foreach (string address in _registeredAddresses)
        {
            try
            {
                addressesFeature.Addresses.Remove(address);
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Removed localhost proxy address '{url}'", address);
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                    logger.LogError(ex, "Failed to remove localhost proxy address '{url}'", address);
            }
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
