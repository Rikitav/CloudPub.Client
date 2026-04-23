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

using CloudPub.ChannelRelays;
using CloudPub.Components;
using CloudPub.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics;
using ProtocolType = CloudPub.Protocol.ProtocolType;

namespace CloudPub;

/// <summary>
/// Thread-safe registry of <see cref="CloudPub.Components.IDataChannelRelay"/> instances keyed by server-assigned channel id,
/// selecting TCP or UDP relays based on the endpoint protocol.
/// </summary>
public class RelaysManager(ICloudPubRules rules) : IRelaysManager
{
    private readonly ICloudPubRules Rules = rules;
    private readonly ConcurrentDictionary<uint, IDataChannelRelay> ChannelIdToRelayMap = [];
    private readonly SemaphoreSlim relaySync = new SemaphoreSlim(1, 1); 

    /// <summary>
    /// Opens a local relay for the given channel id according to <paramref name="endpoint"/> (TCP by default, UDP when requested).
    /// </summary>
    /// <param name="channelId">Server-assigned data channel identifier.</param>
    /// <param name="endpoint">Describes the local address and protocol to connect to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<IDataChannelRelay?> CreateDataChannel(uint channelId, ServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        await relaySync.WaitAsync(cancellationToken);
        try
        {
            if (endpoint?.Client is null)
                return null;

            if (ChannelIdToRelayMap.TryGetValue(channelId, out IDataChannelRelay? existingRelay))
                return existingRelay;

            Func<IDataChannelRelay>? relayFactory = Rules.WhatRelayUseForProtocol(endpoint.Client.LocalProto);
            if (relayFactory != null)
            {
                IDataChannelRelay relay = new BoundDataChannelRelay(channelId, relayFactory.Invoke());
                IDataChannelRelay storedRelay = ChannelIdToRelayMap.GetOrAdd(channelId, relay);
                Debug.WriteLine($"CloudPub relay created (custom), channelId={channelId}, storedNew={ReferenceEquals(storedRelay, relay)}");
                return storedRelay;
            }

            IDataChannelRelay createdRelay;
            string localAddr = endpoint.Client.LocalAddr;
            uint localPort = endpoint.Client.LocalPort;

            switch (endpoint.Client.LocalProto)
            {
                case ProtocolType.Udp:
                    {
                        createdRelay = await UdpDataChannelRelay
                            .CreateAsync(channelId, localAddr, localPort, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }

                default:
                    {
                        createdRelay = await TcpDataChannelRelay
                            .CreateAsync(channelId, localAddr, localPort, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }
            }

            IDataChannelRelay relayInMap = ChannelIdToRelayMap.GetOrAdd(channelId, createdRelay);
            if (!ReferenceEquals(relayInMap, createdRelay))
                await createdRelay.DisposeAsync().ConfigureAwait(false);

            Debug.WriteLine($"CloudPub relay created, channelId={channelId}, protocol={endpoint.Client.LocalProto}, addr={localAddr}:{localPort}");
            return relayInMap;
        }
        finally
        {
            relaySync.Release();
        }
    }

    /// <summary>
    /// Writes payload bytes to the relay for <paramref name="channelId"/>, if a relay exists.
    /// </summary>
    /// <param name="channelId">Target data channel identifier.</param>
    /// <param name="data">Bytes to forward to the local socket.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<uint> WriteDataChannel(uint channelId, byte[] data, CancellationToken cancellationToken = default)
    {
        await relaySync.WaitAsync(cancellationToken);
        try
        {
            if (!ChannelIdToRelayMap.TryGetValue(channelId, out IDataChannelRelay? relay))
                return 0;

            await relay.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            Debug.WriteLine($"CloudPub relay consumed chunk, channelId={channelId}, bytes={data.Length}, total={relay.TotalConsumed}");
            return relay.TotalConsumed;
        }
        finally
        {
            relaySync.Release();
        }
    }

    /// <summary>
    /// Disposes and removes the relay for <paramref name="channelId"/>, if present.
    /// </summary>
    /// <param name="channelId">Data channel identifier to tear down.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task DeleteDataChannel(uint channelId, CancellationToken cancellationToken = default)
    {
        await relaySync.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ChannelIdToRelayMap.TryRemove(channelId, out IDataChannelRelay? relay))
                return;

            await relay.DisposeAsync().ConfigureAwait(false);
            Debug.WriteLine($"CloudPub relay disposed, channelId={channelId}");
        }
        finally
        {
            relaySync.Release();
        }
    }

    private sealed class BoundDataChannelRelay(uint channelId, IDataChannelRelay innerRelay) : IDataChannelRelay
    {
        public uint ChannelId { get; } = channelId;
        public uint TotalConsumed => innerRelay.TotalConsumed;

        public Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
            => innerRelay.WriteAsync(data, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default)
            => innerRelay.ReadAsync(cancellationToken);

        public ValueTask DisposeAsync()
            => innerRelay.DisposeAsync();
    }
}
