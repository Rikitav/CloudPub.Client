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

namespace CloudPub.Components;

/// <summary>
/// Defines per-protocol relay factory mappings used by the CloudPub client.
/// </summary>
public interface ICloudPubRules
{
    /// <summary>
    /// Associates a protocol with a relay factory used to create data-channel relays.
    /// </summary>
    /// <param name="protocolType">Protocol to bind.</param>
    /// <param name="relayFactory">Factory that creates a relay instance for that protocol.</param>
    void AddCustomProtocolRelay(ProtocolType protocolType, Func<IDataChannelRelay> relayFactory);

    /// <summary>
    /// Resolves the relay factory for a protocol, if it was registered.
    /// </summary>
    /// <param name="protocolType">Protocol to resolve.</param>
    /// <returns>The registered relay factory, or <c>null</c> if none was configured.</returns>
    Func<IDataChannelRelay>? GetCustomProtocolRelay(ProtocolType protocolType);
}
