using System;

namespace JetFighter.Network
{
    /// <summary>
    /// Connection states a transport can be in.
    ///
    /// Exposed because gameplay has to distinguish "not connected yet" from
    /// "was connected and dropped" -- the second needs the intro gate closed
    /// and a message, the first is just the lobby.
    /// </summary>
    public enum TransportState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Failed = 3,
    }

    /// <summary>
    /// The one seam between gameplay and the network.
    ///
    /// Everything gameplay-side talks to this and never to GameKit, which is
    /// the swap seam the roadmap's "won't corner us" requirement asks for --
    /// and the cell after next tests the abstraction by swapping the
    /// implementation and requiring zero changes outside Network/.
    ///
    /// Deliberately byte-oriented rather than generic over a message type.
    /// GKMatch sends bytes; a transport that took typed messages would have to
    /// own serialisation, which is the part most likely to change independently
    /// of the transport (and the part Phase 4's sync rate work touches).
    ///
    /// No async/await and no coroutines in the surface. Delivery is announced
    /// through an event because that is what every real transport does, and
    /// wrapping it in a task per message allocates once per packet at 20Hz for
    /// no benefit.
    /// </summary>
    public interface INetworkTransport
    {
        /// <summary>Current state. Never throws; a failed transport reports Failed.</summary>
        TransportState State { get; }

        /// <summary>Stable id for this peer, used by HostAuthority to elect deterministically.</summary>
        string LocalPeerId { get; }

        /// <summary>Raised for every payload received from a peer.</summary>
        event Action<byte[]> OnStateReceived;

        /// <summary>Raised whenever State changes, so gameplay can react to a drop.</summary>
        event Action<TransportState> OnStateChanged;

        /// <summary>Begins connecting. Idempotent -- calling it while connected is a no-op.</summary>
        void Connect();

        /// <summary>
        /// Sends a payload. Returns false when the transport is not connected
        /// rather than throwing or queueing: at 20Hz a queue that survives a
        /// disconnect delivers a burst of stale state on reconnect, which
        /// looks exactly like a desync.
        /// </summary>
        bool SendState(byte[] payload);

        /// <summary>Disconnects. Idempotent, and safe to call after a failure.</summary>
        void Disconnect();
    }
}
