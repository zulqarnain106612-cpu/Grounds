using System;
using System.Collections.Generic;
using UnityEngine;
using JetFighter.Enemy;

namespace JetFighter.Network
{
    /// <summary>
    /// One enemy's state on the wire.
    ///
    /// Health travels as an absolute value, never as a damage delta. A delta
    /// lost in transit leaves the guest permanently wrong by that much, and
    /// nothing later corrects it; an absolute value self-heals on the next
    /// packet. That single choice is most of what makes "zero divergence"
    /// achievable over a lossy link.
    /// </summary>
    [Serializable]
    public struct EnemyStatePayload
    {
        public int enemyId;
        public Vector3 position;
        public float currentHealth;
        public float maxHealth;
        public bool alive;
    }

    /// <summary>
    /// Host serialises enemy state; guest applies it and simulates nothing.
    ///
    /// The criterion is zero enemy-health divergence, and the only reliable
    /// way to get it is for exactly one peer to own the truth. The guest
    /// therefore does not run spawners, does not roll drops, and does not
    /// apply damage locally -- not "usually agrees", but "has no independent
    /// opinion to disagree with".
    ///
    /// Guest-side damage is not dropped, it is simply not authoritative: the
    /// guest's bullets still hit, the host still owns the resulting health,
    /// and the next state packet reconciles the guest's display. That is why
    /// the sync interval matters more than the damage path here.
    /// </summary>
    public class NetworkedEnemyState : MonoBehaviour
    {
        public const int PayloadSize = 1 + 4 + 12 + 4 + 4 + 1;

        [Tooltip("Enemy state broadcasts per second, host only.")]
        [Range(1f, 60f)]
        [SerializeField] private float sendRateHz = 15f;

        private readonly Dictionary<int, EnemyHealth> tracked = new Dictionary<int, EnemyHealth>();
        private INetworkTransport transport;
        private bool isHost;
        private float sendTimer;
        private int nextEnemyId = 1;

        /// <summary>State packets sent as host.</summary>
        public int SentCount { get; private set; }

        /// <summary>State packets applied as guest.</summary>
        public int AppliedCount { get; private set; }

        /// <summary>Packets for enemies this peer does not know about yet.</summary>
        public int UnknownEnemyPackets { get; private set; }

        public bool IsHost => isHost;

        public float SendRateHz
        {
            get => sendRateHz;
            set => sendRateHz = Mathf.Clamp(value, 1f, 60f);
        }

        /// <summary>Enemies this peer is tracking, by network id.</summary>
        public IReadOnlyDictionary<int, EnemyHealth> Tracked => tracked;

        public void Bind(INetworkTransport newTransport, bool asHost)
        {
            if (transport != null)
            {
                transport.OnStateReceived -= HandlePayload;
            }
            transport = newTransport;
            isHost = asHost;
            if (transport != null)
            {
                transport.OnStateReceived += HandlePayload;
            }
        }

        private void OnDisable()
        {
            Bind(null, isHost);
        }

        /// <summary>
        /// Registers an enemy under a shared id.
        ///
        /// The host assigns ids and the guest is told them, rather than both
        /// deriving one from a spawn order they would have to agree on. Two
        /// peers agreeing on an ordering is the same problem as agreeing on
        /// state, one level down.
        /// </summary>
        public int Register(EnemyHealth enemy, int enemyId = 0)
        {
            if (enemy == null)
            {
                return 0;
            }
            int id = enemyId > 0 ? enemyId : nextEnemyId++;
            tracked[id] = enemy;
            if (id >= nextEnemyId)
            {
                nextEnemyId = id + 1;
            }
            return id;
        }

        public void Forget(int enemyId)
        {
            tracked.Remove(enemyId);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Broadcasts every tracked enemy on the interval, host only. Takes
        /// deltaTime so a divergence soak runs in simulated time.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!isHost || transport == null || deltaTime <= 0f)
            {
                return;
            }
            sendTimer -= deltaTime;
            if (sendTimer > 0f)
            {
                return;
            }
            sendTimer = 1f / sendRateHz;
            BroadcastAll();
        }

        /// <summary>Sends the current state of every tracked enemy.</summary>
        public void BroadcastAll()
        {
            if (!isHost || transport == null)
            {
                return;
            }
            foreach (KeyValuePair<int, EnemyHealth> pair in tracked)
            {
                EnemyHealth enemy = pair.Value;
                if (enemy == null)
                {
                    continue;
                }
                var payload = new EnemyStatePayload
                {
                    enemyId = pair.Key,
                    position = enemy.transform.position,
                    currentHealth = enemy.CurrentHealth,
                    maxHealth = enemy.MaxHealth,
                    alive = !enemy.IsDead && enemy.gameObject.activeSelf,
                };
                if (transport.SendState(Encode(payload)))
                {
                    SentCount++;
                }
            }
        }

        private void HandlePayload(byte[] payload)
        {
            if (NetworkMessage.TypeOf(payload) != MessageType.EnemyState)
            {
                return;
            }
            // The host ignores enemy state entirely. Applying its own echo, or
            // a guest that started sending, is how two peers end up
            // overwriting each other -- the exact failure this design exists
            // to make impossible.
            if (isHost || !TryDecode(payload, out EnemyStatePayload state))
            {
                return;
            }
            Apply(state);
        }

        /// <summary>Applies authoritative state to a tracked enemy.</summary>
        public void Apply(EnemyStatePayload state)
        {
            if (!tracked.TryGetValue(state.enemyId, out EnemyHealth enemy) || enemy == null)
            {
                // A packet for an enemy this peer has not spawned yet. Counted
                // rather than ignored silently: a persistently rising count
                // means spawn replication is behind, which is a real bug that
                // would otherwise show up only as enemies appearing late.
                UnknownEnemyPackets++;
                return;
            }

            enemy.transform.position = state.position;
            // Absolute, not a delta. A lost delta leaves the guest permanently
            // wrong; an absolute value self-heals on the next packet.
            enemy.SetNetworkedHealth(state.currentHealth, state.maxHealth);
            if (enemy.gameObject.activeSelf != state.alive)
            {
                enemy.gameObject.SetActive(state.alive);
            }
            AppliedCount++;
        }

        public static byte[] Encode(EnemyStatePayload state)
        {
            var buffer = new byte[PayloadSize];
            buffer[0] = (byte)MessageType.EnemyState;
            int offset = 1;
            BitConverter.GetBytes(state.enemyId).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(state.position.x).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(state.position.y).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(state.position.z).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(state.currentHealth).CopyTo(buffer, offset); offset += 4;
            BitConverter.GetBytes(state.maxHealth).CopyTo(buffer, offset); offset += 4;
            buffer[offset] = (byte)(state.alive ? 1 : 0);
            return buffer;
        }

        public static bool TryDecode(byte[] payload, out EnemyStatePayload state)
        {
            state = default;
            if (NetworkMessage.TypeOf(payload) != MessageType.EnemyState || payload.Length < PayloadSize)
            {
                return false;
            }
            int offset = 1;
            int id = BitConverter.ToInt32(payload, offset); offset += 4;
            float x = BitConverter.ToSingle(payload, offset); offset += 4;
            float y = BitConverter.ToSingle(payload, offset); offset += 4;
            float z = BitConverter.ToSingle(payload, offset); offset += 4;
            float current = BitConverter.ToSingle(payload, offset); offset += 4;
            float max = BitConverter.ToSingle(payload, offset); offset += 4;
            bool alive = payload[offset] != 0;

            foreach (float value in new[] { x, y, z, current, max })
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }
            }
            if (id <= 0)
            {
                return false;
            }

            state = new EnemyStatePayload
            {
                enemyId = id,
                position = new Vector3(x, y, z),
                currentHealth = current,
                maxHealth = max,
                alive = alive,
            };
            return true;
        }
    }
}
