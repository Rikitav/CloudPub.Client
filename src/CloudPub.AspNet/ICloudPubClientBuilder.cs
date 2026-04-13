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
using Microsoft.Extensions.DependencyInjection;

namespace CloudPub;

/// <summary>
/// Fluent builder used to configure CloudPub endpoint publications and traffic proxy mode.
/// </summary>
public interface ICloudPubClientBuilder
{
    /// <summary>
    /// Gets the underlying service collection used to register CloudPub services.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Adds a preconfigured publish endpoint definition.
    /// </summary>
    /// <param name="publishOptions">Endpoint options to publish through CloudPub.</param>
    /// <returns>The same builder instance for chaining.</returns>
    ICloudPubClientBuilder AddPublishEndpoint(CloudPubPublishOptions publishOptions);

    /// <summary>
    /// Adds an endpoint that targets <c>localhost:{port}</c>.
    /// </summary>
    /// <param name="port">Local service port.</param>
    /// <param name="name">Optional endpoint label displayed by CloudPub.</param>
    /// <param name="protocolType">Protocol for the published endpoint.</param>
    /// <returns>The same builder instance for chaining.</returns>
    ICloudPubClientBuilder AddEndpoint(ushort port, string? name = null, ProtocolType protocolType = ProtocolType.Http);

    /// <summary>
    /// Adds an endpoint using a custom local address string.
    /// </summary>
    /// <param name="address">Address in a format supported by CloudPub publish options.</param>
    /// <param name="name">Optional endpoint label displayed by CloudPub.</param>
    /// <param name="protocolType">Protocol for the published endpoint.</param>
    /// <returns>The same builder instance for chaining.</returns>
    ICloudPubClientBuilder AddEndpoint(string address, string? name = null, ProtocolType protocolType = ProtocolType.Http);

    /// <summary>
    /// Adds an endpoint with implicit address resolution for protocol-specific defaults.
    /// </summary>
    /// <param name="name">Optional endpoint label displayed by CloudPub.</param>
    /// <param name="protocolType">Protocol for the published endpoint.</param>
    /// <returns>The same builder instance for chaining.</returns>
    ICloudPubClientBuilder AddEndpoint(string? name = null, ProtocolType protocolType = ProtocolType.Http);

    /// <summary>
    /// Enables localhost proxy mode that forwards CloudPub traffic to local host addresses.
    /// </summary>
    /// <returns>The same builder instance for chaining.</returns>
    ICloudPubClientBuilder WithLocalhostProxy();

    /// <summary>
    /// Enables pipeline proxy mode that forwards CloudPub HTTP traffic directly into the ASP.NET pipeline.
    /// </summary>
    /// <returns>The same builder instance for chaining.</returns>
    ICloudPubClientBuilder WithPipelineProxy();
}
