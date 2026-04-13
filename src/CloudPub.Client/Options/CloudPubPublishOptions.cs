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

namespace CloudPub.Options;

/// <summary>
/// Describes a local service to expose through CloudPub: protocol, address or URL, optional auth, and traffic rules.
/// </summary>
public class CloudPubPublishOptions
{
    /// <summary>
    /// Gets or sets the application-level protocol for the published endpoint.
    /// </summary>
    public ProtocolType Protocol { get; set; } = ProtocolType.Https;

    /// <summary>
    /// Gets or sets the authentication mode; when <c>null</c>, a default is chosen (e.g. Basic for WebDAV).
    /// </summary>
    public AuthType? Auth { get; set; } = null;

    /// <summary>
    /// Gets or sets the local bind address: port number, <c>host:port</c>, host with path, or a full URL depending on protocol.
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// Gets or sets a human-readable name stored as the endpoint description.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets optional access control entries for the endpoint.
    /// </summary>
    public IReadOnlyCollection<Acl>? Acl { get; set; }

    /// <summary>
    /// Gets or sets optional HTTP headers to associate with the published service.
    /// </summary>
    public IReadOnlyCollection<Header>? Headers { get; set; }

    /// <summary>
    /// Gets or sets optional filter rules applied to traffic for this endpoint.
    /// </summary>
    public IReadOnlyCollection<FilterRule>? Rules { get; set; }
}
