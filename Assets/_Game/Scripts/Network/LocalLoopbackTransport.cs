using System;
using System.Collections.Generic;

namespace JetFighter.Network
{
    /// <summary>
    /// A same-process transport, so Phase 4's gameplay can be built and tested
    /// before GameKit is wired in at all.
    ///
    /// This is why the abstraction cell comes first: state sync and host
    /// authority are the hard parts, and debugging them across two physical
    /// devices -- where the only instrument is watching two screens -- costs
    /// an order of magnitude more than debugging them in a unit test.
    ///
    /// It models the parts of a real link that break gameplay assumptions:
    /// delivery is not instant (it is queued until Pump), it can be lossy, and
    /// it can be interrupted. A loopback that delivered synchronously inside
    /// SendState would let code work that reads its own state back the same
    /// frame -- which no real transport permits, and which would then fail
    /// only on device.
    /// </summary>
    public class LocalLoopbackTransport : INetworkTransport
    {
        private readonly Queue<Pending> inbox = new Queue<Pending>();

        /// <summary>A payload and the number of pumps left before it lands.</summary>
        private struct Pending
        {
            public byte[] Payload;
            public int PumpsRemaining;
        }
        private LocalLoopbackTransport peer;
        private TransportState state = TransportState.Disconnected;

        public LocalLoopbackTransport(string peerId)
        {
            LocalPeerId = peerId;
        }

        public string LocalPeerId { get; }

        public TransportState State => state;

        public event Action<byte[]> OnStateReceived;

        public event Action<TransportState> OnStateChanged;

        /// <summary>Payloads sent while disconnected. Surfaced so a test can assert none were.</summary>
        public int DroppedWhileDisconnected { get; private set; }

        /// <summary>Payloads discarded by <see cref="LossRate"/>.</summary>
        public int DroppedByLoss { get; private set; }

        /// <summary>
        /// Fraction of packets to discard, 0..1. Deterministic rather than
        /// random: a flaky test is worse than no test, and "every third packet"
        /// exercises the same code path as "33% loss" without the flake.
        /// </summary>
        public float LossRate { get; set; }

        private int sendCounter;

        /// <summary>
        /// Pumps a payload waits before delivery. Zero is the old behaviour.
        ///
        /// Latency is what separates a soak from a round-trip test: every
        /// ordering bug in the sync layer needs two messages in flight at once
        /// to show up, and with instant delivery there is never more than one.
        /// </summary>
        public int LatencyPumps { get; set; }

        /// <summary>
        /// Extra pumps added to every other message, modelling jitter.
        ///
        /// Deterministic, like the loss model: this is what actually reorders
        /// packets, and a reordering bug that appears one run in five is one
        /// nobody will believe.
        /// </summary>
        public int JitterPumps { get; set; }

        /// <summary>Messages waiting to be delivered by <see cref="Pump"/>.</summary>
        public int PendingCount => inbox.Count;

        /// <summary>
        /// Wires two transports together. Both are connected afterwards, so a
        /// test does not have to remember to call Connect on each. Pass
        /// connect: false when the caller needs to subscribe to
        /// OnStateChanged before the link comes up -- a transport handed back
        /// already Connected never raises the transition its owner is
        /// waiting for.
        /// </summary>
        public static (LocalLoopbackTransport host, LocalLoopbackTransport guest) CreatePair(
            string hostId = "host", string guestId = "guest", bool connect = true)
        {
            var host = new LocalLoopbackTransport(hostId);
            var guest = new LocalLoopbackTransport(guestId);
            host.peer = guest;
            guest.peer = host;
            if (connect)
            {
                host.Connect();
                guest.Connect();
            }
            return (host, guest);
        }

        public void Connect()
        {
            if (state == TransportState.Connected)
            {
                return;
            }
            SetState(peer != null ? TransportState.Connected : TransportState.Failed);
        }

        public bool SendState(byte[] payload)
        {
            if (payload == null || state != TransportState.Connected || peer == null)
            {
                DroppedWhileDisconnected++;
                return false;
            }

            sendCounter++;
            if (DropsToLoss(sendCounter))
            {
                DroppedByLoss++;
                // Reported as sent: a real lossy link does not tell the sender.
                // Code that treats a false return as "retry" would spin.
                return true;
            }

            // The far end being down is not the sender's business either: a
            // peer that stopped listening looks exactly like a link that
            // stopped delivering. Enqueuing anyway would let a disconnected
            // guest keep applying host state -- the one thing an interruption
            // test exists to catch, and the failure that reads as a working
            // game until the players compare screens.
            if (peer.state != TransportState.Connected)
            {
                return true;
            }

            // Copied, not referenced. A caller reusing its buffer between sends
            // -- which is what anyone does at 20Hz to avoid allocating -- would
            // otherwise mutate a message already in flight.
            var copy = new byte[payload.Length];
            Buffer.BlockCopy(payload, 0, copy, 0, payload.Length);
            int delay = Math.Max(0, LatencyPumps) + (sendCounter % 2 == 0 ? Math.Max(0, JitterPumps) : 0);
            peer.inbox.Enqueue(new Pending { Payload = copy, PumpsRemaining = delay });
            return true;
        }

        /// <summary>
        /// Delivers queued messages. Explicit rather than automatic so a test
        /// controls exactly when the remote sees state, which is what makes
        /// "the guest never simulates independently" assertable.
        /// </summary>
        public int Pump()
        {
            int waiting = inbox.Count;
            int delivered = 0;
            // Each message is examined once per pump. Anything still in
            // flight goes back on the queue, so ordering is preserved except
            // where jitter deliberately breaks it -- which is the point.
            for (int i = 0; i < waiting; i++)
            {
                Pending pending = inbox.Dequeue();
                if (pending.PumpsRemaining > 0)
                {
                    pending.PumpsRemaining--;
                    inbox.Enqueue(pending);
                    continue;
                }
                OnStateReceived?.Invoke(pending.Payload);
                delivered++;
            }
            return delivered;
        }

        /// <summary>
        /// Simulates a brief interruption: the link drops, in-flight messages
        /// are lost, and it can be reconnected. The soak cell's "an
        /// interruption crashes neither client" starts here.
        /// </summary>
        public void Interrupt()
        {
            inbox.Clear();
            SetState(TransportState.Disconnected);
        }

        public void Disconnect()
        {
            inbox.Clear();
            if (state != TransportState.Disconnected)
            {
                SetState(TransportState.Disconnected);
            }
        }

        /// <summary>
        /// Whether this packet is discarded by <see cref="LossRate"/>.
        ///
        /// The phase rotates with the cycle instead of dropping a fixed slot.
        /// A plain "every Nth packet" aliases with the caller's send pattern:
        /// two 20Hz senders sharing one link alternate, so at 50% loss one of
        /// them lands on the dropped slot every single time and never gets a
        /// packet through -- 100% loss for that stream while the counter says
        /// 50%. Rotating gives each interleaved stream the configured rate and
        /// never drops the same one twice running, and it is still exactly
        /// reproducible: a flaky soak is worse than no soak.
        /// </summary>
        private bool DropsToLoss(int counter)
        {
            if (LossRate <= 0f)
            {
                return false;
            }
            int period = Math.Max(2, (int)Math.Round(1f / LossRate));
            return counter % period == counter / period % period;
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
