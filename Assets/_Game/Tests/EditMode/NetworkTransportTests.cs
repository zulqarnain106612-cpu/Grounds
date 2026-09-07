using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using JetFighter.Network;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion: the loopback transport round-trips a message with zero
    /// gameplay-code awareness of the implementation.
    ///
    /// Everything here talks to INetworkTransport rather than the concrete
    /// class wherever the behaviour is part of the contract, because the cell
    /// after next swaps the implementation and requires zero changes outside
    /// Network/. A test written against the concrete type would pass then and
    /// prove nothing.
    /// </summary>
    public class NetworkTransportTests
    {
        private static byte[] Payload(string text) => Encoding.UTF8.GetBytes(text);

        private static string Text(byte[] payload) => Encoding.UTF8.GetString(payload);

        [Test]
        public void AMessageRoundTripsThroughTheInterface()
        {
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            INetworkTransport sender = host;

            string received = null;
            guest.OnStateReceived += payload => received = Text(payload);

            Assert.IsTrue(sender.SendState(Payload("hello")));
            guest.Pump();

            Assert.AreEqual("hello", received);
        }

        [Test]
        public void BothDirectionsWork()
        {
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            string atHost = null;
            host.OnStateReceived += p => atHost = Text(p);

            ((INetworkTransport)guest).SendState(Payload("from guest"));
            host.Pump();

            Assert.AreEqual("from guest", atHost);
        }

        [Test]
        public void DeliveryIsNotSynchronousWithSending()
        {
            // A loopback that delivered inside SendState would let code work
            // that reads its own state back the same frame -- which no real
            // transport permits, so it would fail only on device.
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            bool arrived = false;
            guest.OnStateReceived += _ => arrived = true;

            host.SendState(Payload("x"));
            Assert.IsFalse(arrived, "the message arrived before the remote pumped");
            Assert.AreEqual(1, guest.PendingCount);

            guest.Pump();
            Assert.IsTrue(arrived);
        }

        [Test]
        public void MessagesArriveInOrder()
        {
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            var order = new List<string>();
            guest.OnStateReceived += p => order.Add(Text(p));

            for (int i = 0; i < 5; i++)
            {
                host.SendState(Payload(i.ToString()));
            }
            guest.Pump();

            CollectionAssert.AreEqual(new[] { "0", "1", "2", "3", "4" }, order);
        }

        [Test]
        public void APayloadIsCopiedNotReferenced()
        {
            // Reusing one buffer between sends is what anyone does at 20Hz to
            // avoid allocating. Referencing it would mutate a message already
            // in flight.
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            string received = null;
            guest.OnStateReceived += p => received = Text(p);

            byte[] buffer = Payload("first");
            host.SendState(buffer);
            buffer[0] = (byte)'X';
            guest.Pump();

            Assert.AreEqual("first", received);
        }

        [Test]
        public void SendingWhileDisconnectedFailsRatherThanQueueing()
        {
            // At 20Hz a queue that survives a disconnect delivers a burst of
            // stale state on reconnect, which looks exactly like a desync.
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            host.Disconnect();

            Assert.IsFalse(((INetworkTransport)host).SendState(Payload("x")));
            Assert.AreEqual(0, guest.PendingCount);
            Assert.AreEqual(1, host.DroppedWhileDisconnected);
        }

        [Test]
        public void AnInterruptionDropsInFlightStateRatherThanDeliveringItLate()
        {
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            host.SendState(Payload("stale"));
            guest.Interrupt();

            bool arrived = false;
            guest.OnStateReceived += _ => arrived = true;
            guest.Pump();

            Assert.IsFalse(arrived, "state from before the interruption was delivered after it");
        }

        [Test]
        public void StateChangesAreAnnounced()
        {
            // Gameplay has to distinguish "not connected yet" from "was
            // connected and dropped" -- the second closes the control gate.
            var transport = new LocalLoopbackTransport("solo");
            var seen = new List<TransportState>();
            transport.OnStateChanged += seen.Add;

            transport.Connect();
            transport.Disconnect();

            CollectionAssert.AreEqual(
                new[] { TransportState.Failed, TransportState.Disconnected }, seen);
        }

        [Test]
        public void ConnectingWithNoPeerFailsRatherThanPretending()
        {
            var lonely = new LocalLoopbackTransport("solo");
            lonely.Connect();
            Assert.AreEqual(TransportState.Failed, lonely.State);
            Assert.IsFalse(lonely.SendState(Payload("x")));
        }

        [Test]
        public void ConnectAndDisconnectAreIdempotent()
        {
            (LocalLoopbackTransport host, _) = LocalLoopbackTransport.CreatePair();
            var changes = new List<TransportState>();
            host.OnStateChanged += changes.Add;

            host.Connect();
            host.Connect();
            Assert.IsEmpty(changes, "a redundant Connect announced a state change");

            host.Disconnect();
            host.Disconnect();
            Assert.AreEqual(1, changes.Count);
        }

        [Test]
        public void ANullPayloadIsRefusedRatherThanSent()
        {
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            Assert.IsFalse(host.SendState(null));
            Assert.AreEqual(0, guest.PendingCount);
        }

        [Test]
        public void LossIsDeterministicRatherThanRandom()
        {
            // A flaky test is worse than no test. "Every third packet"
            // exercises the same code path as "33% loss" without the flake.
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            host.LossRate = 0.5f;

            for (int i = 0; i < 10; i++)
            {
                host.SendState(Payload(i.ToString()));
            }
            Assert.AreEqual(5, host.DroppedByLoss);
            Assert.AreEqual(5, guest.PendingCount);
        }

        [Test]
        public void ALostPacketIsStillReportedAsSent()
        {
            // A real lossy link does not tell the sender. Code treating a
            // false return as "retry" would spin.
            (LocalLoopbackTransport host, _) = LocalLoopbackTransport.CreatePair();
            host.LossRate = 1f;
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(host.SendState(Payload(i.ToString())));
            }
            Assert.Greater(host.DroppedByLoss, 0);
        }

        [Test]
        public void PeerIdsAreStableAndDistinct()
        {
            // HostAuthority elects from these in the next-but-one cell, and a
            // non-deterministic id would make the election non-deterministic.
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            Assert.AreEqual("host", host.LocalPeerId);
            Assert.AreEqual("guest", guest.LocalPeerId);
            Assert.AreEqual("host", host.LocalPeerId);
        }

        [Test]
        public void ReconnectingAfterAnInterruptionWorks()
        {
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            guest.Interrupt();
            Assert.AreEqual(TransportState.Disconnected, guest.State);

            guest.Connect();
            Assert.AreEqual(TransportState.Connected, guest.State);

            string received = null;
            host.OnStateReceived += p => received = Text(p);
            guest.SendState(Payload("back"));
            host.Pump();
            Assert.AreEqual("back", received);
        }
    }
}
