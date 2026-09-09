using System;
using UnityEngine;

namespace JetFighter.Network
{
    /// <summary>
    /// Starts matchmaking and hands the rest of the game a connected
    /// transport.
    ///
    /// This is the one place that decides which INetworkTransport the session
    /// uses, which is what makes the swap a one-line change rather than a
    /// search across the project. Cell 4's acceptance test is exactly that:
    /// swapping the implementation must touch nothing outside Network/.
    ///
    /// The factory is injectable so a test -- and a developer working on a
    /// train -- can run the whole game on loopback without a GameKit plugin
    /// present.
    /// </summary>
    public class NetworkMatchmaker : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>Same-process transport. No plugin, no devices.</summary>
            Loopback = 0,

            /// <summary>GKMatch through the native plugin.</summary>
            GameKit = 1,
        }

        [SerializeField] private Mode mode = Mode.Loopback;

        /// <summary>
        /// Builds the GameKit transport. Injected rather than constructed
        /// here so the plugin dependency stays optional at compile time: the
        /// spec flags it as an unmaintained-package risk, and a hard reference
        /// would make that risk a build failure rather than a swap.
        /// </summary>
        public Func<INetworkTransport> GameKitFactory { get; set; }

        /// <summary>The connected transport, or null before Begin succeeds.</summary>
        public INetworkTransport Transport { get; private set; }

        /// <summary>Raised once a transport reaches Connected.</summary>
        public event Action<INetworkTransport> OnMatchReady;

        /// <summary>Raised when matchmaking fails, with a reason for the UI.</summary>
        public event Action<string> OnMatchFailed;

        public Mode CurrentMode
        {
            get => mode;
            set => mode = value;
        }

        /// <summary>
        /// Starts matchmaking. Returns the transport, or null on failure --
        /// callers get the same answer whether the plugin is missing, the
        /// match was declined, or the network is down, because none of those
        /// change what gameplay does next.
        /// </summary>
        public INetworkTransport Begin()
        {
            Dispose();

            INetworkTransport created = Create();
            if (created == null)
            {
                OnMatchFailed?.Invoke($"no transport available for mode {mode}");
                return null;
            }

            created.OnStateChanged += HandleStateChanged;
            Transport = created;
            created.Connect();

            if (created.State == TransportState.Failed)
            {
                OnMatchFailed?.Invoke("transport failed to connect");
                return null;
            }
            return created;
        }

        /// <summary>Tears the session down. Safe to call when nothing is running.</summary>
        public void Dispose()
        {
            if (Transport == null)
            {
                return;
            }
            Transport.OnStateChanged -= HandleStateChanged;
            Transport.Disconnect();
            Transport = null;
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private INetworkTransport Create()
        {
            switch (mode)
            {
                case Mode.GameKit:
                    // Null when no factory was supplied, which is what a build
                    // without the plugin looks like. Reported as a failed
                    // match rather than a crash: a player with no Game Center
                    // still gets a working single-player game.
                    return GameKitFactory?.Invoke();
                default:
                    // Both ends, not one. A lone LocalLoopbackTransport has no
                    // peer, and Connect() reports Failed without one, so a solo
                    // instance here made loopback mode return null from Begin
                    // every time. Loopback stands up its own far end: the host
                    // is what gameplay talks to, the guest is the local echo.
                    return LocalLoopbackTransport.CreatePair(
                        SystemInfo.deviceUniqueIdentifier,
                        SystemInfo.deviceUniqueIdentifier + ":loopback").host;
            }
        }

        private void HandleStateChanged(TransportState next)
        {
            if (next == TransportState.Connected)
            {
                OnMatchReady?.Invoke(Transport);
            }
            else if (next == TransportState.Failed)
            {
                OnMatchFailed?.Invoke("transport failed");
            }
        }
    }
}
