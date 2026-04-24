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
/// Thread-safe registry of <see cref="IDataChannelRelay"/> instances keyed by server-assigned channel id,
/// selecting TCP or UDP relays based on the endpoint protocol.
/// </summary>
public class RelaysManager(ICloudPubRules rules) : IRelaysManager
{
    private readonly ICloudPubRules Rules = rules;
    private readonly ConcurrentDictionary<uint, RelayState> RelayStates = [];

    /// <summary>
    /// Opens a local relay for the given channel id according to <paramref name="endpoint"/> (TCP by default, UDP when requested).
    /// </summary>
    /// <param name="channelId">Server-assigned data channel identifier.</param>
    /// <param name="endpoint">Describes the local address and protocol to connect to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<RelayState?> CreateDataChannel(uint channelId, ServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        if (RelayStates.TryGetValue(channelId, out RelayState? state))
            await state.ActionsSync.WaitAsync(cancellationToken);

        try
        {
            if (endpoint?.Client is null)
                return null;

            if (RelayStates.TryGetValue(channelId, out RelayState? existingRelay))
            {
                Debug.WriteLine("CloudPub relay already exists for channelId={channelId}, reusing.");
                return existingRelay;
            }

            Func<IDataChannelRelay>? relayFactory = Rules.GetCustomProtocolRelay(endpoint.Client.LocalProto);
            if (relayFactory != null)
            {
                IDataChannelRelay relay = new BoundDataChannelRelay(channelId, relayFactory.Invoke());
                state = RelayStates.GetOrAdd(channelId, _ => new RelayState(channelId, relay));

                Debug.WriteLine($"CloudPub relay created (custom), channelId={channelId}, storedNew={ReferenceEquals(state.Relay, relay)}");
                return state;
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

            state = RelayStates.GetOrAdd(channelId, _ => new RelayState(channelId, createdRelay));
            if (!ReferenceEquals(state.Relay, createdRelay))
                await createdRelay.DisposeAsync().ConfigureAwait(false);

            Debug.WriteLine($"CloudPub relay created, channelId={channelId}, protocol={endpoint.Client.LocalProto}, addr={localAddr}:{localPort}");
            return state;
        }
        finally
        {
            state?.ActionsSync.Release();
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
        if (RelayStates.TryGetValue(channelId, out RelayState? state))
            await state.ActionsSync.WaitAsync(cancellationToken);

        try
        {
            if (state == null)
            {
                Debug.WriteLine("CloudPub relay not found for writing, channelId={channelId}");
                return 0;
            }

            IDataChannelRelay relay = state.Relay;
            await relay.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            
            Debug.WriteLine($"CloudPub relay consumed chunk, channelId={channelId}, bytes={data.Length}, total={relay.TotalConsumed}");
            return relay.TotalConsumed;
        }
        finally
        {
            state?.ActionsSync.Release();
        }
    }

    /// <summary>
    /// Disposes and removes the relay for <paramref name="channelId"/>, if present.
    /// </summary>
    /// <param name="channelId">Data channel identifier to tear down.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task DeleteDataChannel(uint channelId, CancellationToken cancellationToken = default)
    {
        if (RelayStates.TryGetValue(channelId, out RelayState? state))
            await state.ActionsSync.WaitAsync(cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RelayStates.TryRemove(channelId, out state))
            {
                Debug.WriteLine($"CloudPub relay not found for deletion, channelId={channelId}");
                return;
            }

            await state.DisposeAsync().ConfigureAwait(false);
            Debug.WriteLine($"CloudPub relay disposed, channelId={channelId}");
        }
        finally
        {
            state.ActionsSync.Release();
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
