using System;
using System.Collections.Generic;
using UnityEngine;

namespace JetFighter.Network
{
    /// <summary>
    /// The native side of a GKMatch, behind one interface.
    ///
    /// Everything GameKit-shaped is behind INativeMatch, so this class is
    /// testable without the plugin, without a device and without a match. That
    /// matters more here than anywhere else in the project: this is the one
    /// component CI can never exercise end to end, so the alternative is a
    /// class that is only ever tested by two people holding phones.
    ///
    /// The spec flags the dependency itself as a risk -- Apple's
    /// com.apple.unityplugin.gamekit has no guaranteed maintenance -- and this
    /// shape is the mitigation. If the plugin is abandoned, what changes is
    /// one INativeMatch implementation, not the transport, and not a line of
    /// gameplay code.
    /// </summary>
    public class GameKitTransport : INetworkTransport
    {
        /// <summary>
        /// The narrow slice of GKMatch this transport needs.
        ///
        /// Deliberately smaller than GKMatch: every method here is one a fake
        /// has to implement, and every one that is not here is one that cannot
        /// leak GameKit's shape into gameplay's.
        /// </summary>
        public interface INativeMatch
        {
            bool IsConnected { get; }

            string LocalPlayerId { get; }

            IReadOnlyList<string> RemotePlayerIds { get; }

            event Action<byte[], string> OnDataReceived;

            event Action<string, bool> OnPlayerConnectionChanged;

            void Send(byte[] payload);

            void Disconnect();
        }

        private readonly INativeMatch match;
        private TransportState state = TransportState.Disconnected;

        public GameKitTransport(INativeMatch nativeMatch)
        {
            match = nativeMatch ?? throw new ArgumentNullException(nameof(nativeMatch));
            match.OnDataReceived += HandleData;
            match.OnPlayerConnectionChanged += HandleConnectionChanged;
        }

        public TransportState State => state;

        /// <summary>
        /// The local GameKit player id.
        ///
        /// Empty rather than null when the plugin has not resolved one yet:
        /// HostAuthority treats an empty local id as "not host", which is the
        /// safe answer before a match exists. Null would be the same answer by
        /// accident rather than by decision.
        /// </summary>
        public string LocalPeerId => match.LocalPlayerId ?? string.Empty;

        /// <summary>The peer id HostAuthority elects against, or empty while alone.</summary>
        public string RemotePeerId
        {
            get
            {
                IReadOnlyList<string> remotes = match.RemotePlayerIds;
                return remotes != null && remotes.Count > 0 ? remotes[0] : string.Empty;
            }
        }

        public event Action<byte[]> OnStateReceived;

        public event Action<TransportState> OnStateChanged;

        /// <summary>Payloads refused because the match was not connected.</summary>
        public int DroppedWhileDisconnected { get; private set; }

        public void Connect()
        {
            if (state == TransportState.Connected)
            {
                return;
            }
            SetState(match.IsConnected ? TransportState.Connected : TransportState.Connecting);
        }

        public bool SendState(byte[] payload)
        {
            if (payload == null || state != TransportState.Connected)
            {
                DroppedWhileDisconnected++;
                return false;
            }
            try
            {
                match.Send(payload);
                return true;
            }
            catch (Exception e)
            {
                // A native send can throw when the match tears down between
                // the state check and the call. Surfacing it as a failed send
                // keeps the failure in the same shape as every other one --
                // gameplay already handles a false return, and it does not
                // handle an exception from the middle of a frame.
                Debug.LogWarning($"[GameKitTransport] send failed: {e.Message}");
                SetState(TransportState.Failed);
                return false;
            }
        }

        public void Disconnect()
        {
            match.OnDataReceived -= HandleData;
            match.OnPlayerConnectionChanged -= HandleConnectionChanged;
            try
            {
                match.Disconnect();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameKitTransport] disconnect failed: {e.Message}");
            }
            SetState(TransportState.Disconnected);
        }

        private void HandleData(byte[] payload, string fromPlayerId)
        {
            // The sender id is deliberately dropped here. A 2-player match has
            // exactly one remote, and passing the id upward would let gameplay
            // grow logic keyed on GameKit's identifier format -- which is the
            // leak this whole abstraction exists to prevent.
            if (payload != null)
            {
                OnStateReceived?.Invoke(payload);
            }
        }

        private void HandleConnectionChanged(string playerId, bool connected)
        {
            SetState(connected ? TransportState.Connected
                : match.IsConnected ? TransportState.Connected : TransportState.Disconnected);
        }

        private void SetState(TransportState next)
        {
            if (state == next)
            {
                return;
            }
            state = next;
            OnStateChanged?.Invoke(next);
        }
    }
}
