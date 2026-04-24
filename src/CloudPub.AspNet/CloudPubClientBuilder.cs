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

using CloudPub.Options;
using CloudPub.Protocol;
using CloudPub.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CloudPub;

internal sealed class CloudPubClientBuilder(IServiceCollection services) : ICloudPubClientBuilder
{
    public IServiceCollection Services { get; } = services;

    public ICloudPubClientBuilder AddPublishEndpoint(CloudPubPublishOptions publishOptions)
    {
        ArgumentNullException.ThrowIfNull(publishOptions);
        CloudPubHostingState state = ResolveHostingState();
        state.PublishOptions.Add(publishOptions);
        return this;
    }

    public ICloudPubClientBuilder AddEndpoint(ushort port, string? name = null, ProtocolType protocolType = ProtocolType.Http)
        => AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Address = port.ToString(),
            Auth = AuthType.None,
            Name = name ?? string.Empty
        });

    public ICloudPubClientBuilder AddEndpoint(string address, string? name = null, ProtocolType protocolType = ProtocolType.Http)
        => AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Address = address ?? throw new ArgumentNullException(nameof(address)),
            Auth = AuthType.None,
            Name = name ?? string.Empty
        });

    public ICloudPubClientBuilder AddEndpoint(string? name = null, ProtocolType protocolType = ProtocolType.Http)
        => AddPublishEndpoint(new CloudPubPublishOptions()
        {
            Protocol = protocolType,
            Auth = AuthType.None,
            Name = name ?? string.Empty
        });

    public ICloudPubClientBuilder WithLocalhostProxy()
    {
        CloudPubHostingState state = ResolveHostingState();
        ValidateProxyMode(state, CloudPubProxyMode.Localhost);
        state.ProxyMode = CloudPubProxyMode.Localhost;

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HostedCloudPubLocalhostProxyService>());
        return this;
    }

    public ICloudPubClientBuilder WithPipelineProxy()
    {
        CloudPubHostingState state = ResolveHostingState();
        ValidateProxyMode(state, CloudPubProxyMode.Pipeline);
        state.ProxyMode = CloudPubProxyMode.Pipeline;

        Services.AddSingleton<HttpRelayDispatcher>();
        Services.AddSingleton<PipelineAccessor>();
        Services.AddTransient<IStartupFilter, PipelineCaptureFilter>();
        Services.Replace(ServiceDescriptor.Singleton(RulesFactory));
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HostedCloudPubPipelineProxyService>());
        return this;
    }

    private static void ValidateProxyMode(CloudPubHostingState state, CloudPubProxyMode requestedMode)
    {
        if (state.ProxyMode != CloudPubProxyMode.None && state.ProxyMode != requestedMode)
            throw new InvalidOperationException($"Proxy mode '{state.ProxyMode}' is already configured and incompatible with '{requestedMode}'.");
    }

    private CloudPubHostingState ResolveHostingState()
    {
        ServiceDescriptor? descriptor = Services.FirstOrDefault(x => x.ServiceType == typeof(CloudPubHostingState));
        if (descriptor?.ImplementationInstance is CloudPubHostingState state)
            return state;

        throw new InvalidOperationException("CloudPub hosting state is not registered.");
    }

    private static Components.ICloudPubRules RulesFactory(IServiceProvider serviceProvider)
    {
        CloudPubRules rules = new CloudPubRules();
        rules.AddCustomProtocolRelay(ProtocolType.Http, () => ActivatorUtilities.CreateInstance<ChannelRelays.TcpToHttpRelayChannel>(serviceProvider));
        rules.AddCustomProtocolRelay(ProtocolType.Https, () => ActivatorUtilities.CreateInstance<ChannelRelays.TcpToHttpRelayChannel>(serviceProvider));
        return rules;
    }
}
