using UnityEngine;

namespace JetFighter.Network
{
    /// <summary>
    /// Broadcasts this device's own jet and applies the remote one.
    ///
    /// Each device is authoritative for its own jet, so nothing here syncs a
    /// jet to itself. That is the whole reason player sync is simpler than
    /// enemy sync -- there is no arbitration, only transmission.
    ///
    /// No prediction and no interpolation on the receive side beyond a
    /// straight lerp toward the last received position. The spec is explicit
    /// that prediction is not justified until playtesting shows it is needed,
    /// and it is real complexity: a rollback bug looks exactly like a physics
    /// bug and is far harder to isolate.
    /// </summary>
    public class NetworkedPlayerState : MonoBehaviour
    {
        [Header("Local jet (authoritative here)")]
        [SerializeField] private Transform localJet;

        [Header("Remote jet (driven by the network)")]
        [SerializeField] private Transform remoteJet;

        [Tooltip("Broadcasts per second. 10-20Hz is ample for a short P2P session.")]
        [Range(1f, 60f)]
        [SerializeField] private float sendRateHz = 15f;

        [Tooltip("How quickly the remote jet converges on its last received position.")]
        [Min(0.01f)]
        [SerializeField] private float smoothing = 12f;

        private INetworkTransport transport;
        private float sendTimer;
        private uint sendSequence;
        private uint lastAppliedSequence;
        private Vector3 remoteTargetPosition;
        private float remoteTargetBank;
        private bool hasRemoteState;

        /// <summary>Payloads broadcast. For asserting the send rate.</summary>
        public int SentCount { get; private set; }

        /// <summary>Payloads applied to the remote jet.</summary>
        public int AppliedCount { get; private set; }

        /// <summary>
        /// Packets discarded as older than one already applied. Surfaced
        /// because a rising count is the signature of reordering, which is
        /// otherwise indistinguishable from jitter.
        /// </summary>
        public int RejectedAsStale { get; private set; }

        /// <summary>Packets discarded as malformed. A real link produces these.</summary>
        public int RejectedAsMalformed { get; private set; }

        /// <summary>Raised when the remote peer fires, so the local scene can play the effect.</summary>
        public event System.Action OnRemoteFired;

        public Transform LocalJet { get => localJet; set => localJet = value; }

        public Transform RemoteJet { get => remoteJet; set => remoteJet = value; }

        public float SendRateHz
        {
            get => sendRateHz;
            set => sendRateHz = Mathf.Clamp(value, 1f, 60f);
        }

        /// <summary>
        /// Attaches a transport. Unsubscribes from the previous one first: a
        /// reconnect that left the old subscription would apply every packet
        /// twice, and the second application is always the stale one.
        /// </summary>
        public void Bind(INetworkTransport newTransport)
        {
            if (transport != null)
            {
                transport.OnStateReceived -= HandlePayload;
            }
            transport = newTransport;
            if (transport != null)
            {
                transport.OnStateReceived += HandlePayload;
            }
        }

        private void OnDisable()
        {
            Bind(null);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Broadcasts on the send interval and eases the remote jet toward its
        /// last known state. Takes deltaTime so the rate is assertable without
        /// waiting real seconds.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }
            TickSend(deltaTime);
            TickRemote(deltaTime);
        }

        private void TickSend(float deltaTime)
        {
            if (transport == null || localJet == null)
            {
                return;
            }
            sendTimer -= deltaTime;
            if (sendTimer > 0f)
            {
                return;
            }
            // Reset rather than accumulated: unlike the gun's cooldown, a
            // missed broadcast must not be made up for. Sending four packets
            // back to back after a hitch wastes bandwidth to deliver three
            // positions the remote will never render.
            sendTimer = 1f / sendRateHz;
            Broadcast();
        }

        /// <summary>Sends the local jet's state now.</summary>
        public void Broadcast()
        {
            if (transport == null || localJet == null)
            {
                return;
            }
            var payload = new PlayerStatePayload
            {
                position = localJet.position,
                bankAngle = localJet.localEulerAngles.z,
                sequence = ++sendSequence,
            };
            if (transport.SendState(NetworkMessage.EncodePlayerState(payload)))
            {
                SentCount++;
            }
        }

        /// <summary>Announces a local shot to the remote peer.</summary>
        public void BroadcastFired()
        {
            if (transport == null)
            {
                return;
            }
            transport.SendState(NetworkMessage.EncodeFired(++sendSequence));
        }

        private void TickRemote(float deltaTime)
        {
            if (remoteJet == null || !hasRemoteState)
            {
                return;
            }
            // Exponential convergence, for the same reason the flight model's
            // banking uses it: a rate that depends on tick length would make
            // the remote jet visibly smoother on one device than the other.
            float t = 1f - Mathf.Exp(-smoothing * deltaTime);
            remoteJet.position = Vector3.Lerp(remoteJet.position, remoteTargetPosition, t);
            remoteJet.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.LerpAngle(remoteJet.localEulerAngles.z, remoteTargetBank, t));
        }

        private void HandlePayload(byte[] payload)
        {
            switch (NetworkMessage.TypeOf(payload))
            {
                case MessageType.PlayerState:
                    ApplyPlayerState(payload);
                    break;
                case MessageType.PlayerFired:
                    if (NetworkMessage.TryDecodeFired(payload, out _))
                    {
                        OnRemoteFired?.Invoke();
                    }
                    break;
                default:
                    // Not ours. Enemy state is another component's, and an
                    // unknown tag is a peer on a newer build -- neither is an
                    // error worth failing on.
                    break;
            }
        }

        private void ApplyPlayerState(byte[] payload)
        {
            if (!NetworkMessage.TryDecodePlayerState(payload, out PlayerStatePayload state))
            {
                RejectedAsMalformed++;
                return;
            }
            // UDP-style transports reorder. Applying an older packet after a
            // newer one snaps the remote jet backwards, which reads as
            // rubber-banding and is usually blamed on latency.
            if (hasRemoteState && state.sequence <= lastAppliedSequence)
            {
                RejectedAsStale++;
                return;
            }

            lastAppliedSequence = state.sequence;
            remoteTargetPosition = state.position;
            remoteTargetBank = state.bankAngle;
            AppliedCount++;

            if (!hasRemoteState)
            {
                // The first packet snaps rather than eases: the remote jet
                // starts at the origin, and easing from there would fly it
                // across the level in front of the player.
                hasRemoteState = true;
                if (remoteJet != null)
                {
                    remoteJet.position = state.position;
                }
            }
        }
    }
}
