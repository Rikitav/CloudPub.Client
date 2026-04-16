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

using System.Net;

namespace CloudPub.Options;

/// <summary>
/// Configuration for connecting to CloudPub: server URL, session credentials, timeouts, and agent identity fields.
/// </summary>
public sealed class CloudPubClientOptions
{
    /// <summary>
    /// Gets or sets the HTTP(S) base URL of the CloudPub control plane; WebSocket URL is derived from this value.
    /// </summary>
    public Uri ServerUri { get; set; } = new Uri("https://cloudpub.ru");

    /// <summary>
    /// Gets or sets the maximum time to wait for handshake steps after connecting.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the proxy using to connect to server.
    /// </summary>
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// Gets or sets time interval of web socket heartbeats.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Gets or sets a value indicating whether to send <c>EndpointStartAll</c> after the receive loop starts.
    /// </summary>
    public bool ResumeEndpointsOnConnect { get; set; } = true;

    /// <summary>
    /// Gets or sets the session token returned by the server after a successful handshake (may be empty before connect).
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the account email used in the agent hello when authenticating.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the account password used in the agent hello when authenticating.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets a stable agent identifier; a new GUID is used when left empty.
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Gets or sets an optional hardware identifier string reported to the server.
    /// </summary>
    public string? Hwid { get; set; }

    /// <summary>
    /// Gets or sets the client version string sent in <c>AgentInfo</c>; a default is used when empty.
    /// </summary>
    public string? ClientVersion { get; set; }
}
