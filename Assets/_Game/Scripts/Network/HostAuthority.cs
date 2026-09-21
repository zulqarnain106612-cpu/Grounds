using System;

namespace JetFighter.Network
{
    /// <summary>
    /// Decides which peer owns enemy state.
    ///
    /// Deterministic from the two peer ids rather than negotiated. A 2-player
    /// match does not need an election protocol, and an election is one more
    /// thing that can end with both peers believing they won -- which is a
    /// desync with no symptom until the enemy healths diverge.
    ///
    /// The rule is "lower peer id hosts". Any total order works; what matters
    /// is that both peers compute the same answer from the same two ids
    /// without exchanging a message, so the answer exists before the first
    /// packet does.
    /// </summary>
    public static class HostAuthority
    {
        /// <summary>
        /// Whether this peer is the host.
        ///
        /// Ordinal comparison, not culture-aware: the same two ids must
        /// resolve identically on every device, and culture-aware ordering of
        /// the same strings genuinely differs between locales.
        /// </summary>
        public static bool IsHost(string localPeerId, string remotePeerId)
        {
            if (string.IsNullOrEmpty(localPeerId))
            {
                return false;
            }
            if (string.IsNullOrEmpty(remotePeerId))
            {
                // No peer yet means a solo session, which is host by
                // definition -- otherwise enemies never spawn while waiting.
                return true;
            }
            int order = string.CompareOrdinal(localPeerId, remotePeerId);
            if (order == 0)
            {
                // Identical ids should be impossible, but if two devices ever
                // report the same one, both claiming host is the worst
                // outcome and both declining is merely a dead match.
                return false;
            }
            return order < 0;
        }

        /// <summary>Whether this peer is the guest. The exact complement, minus the tie.</summary>
        public static bool IsGuest(string localPeerId, string remotePeerId)
        {
            return !IsHost(localPeerId, remotePeerId)
                && !string.IsNullOrEmpty(localPeerId)
                && !string.IsNullOrEmpty(remotePeerId)
                && string.CompareOrdinal(localPeerId, remotePeerId) != 0;
        }

        /// <summary>
        /// Resolves the role for a transport once a peer is known.
        ///
        /// Takes the remote id explicitly rather than reading it off the
        /// transport: a transport that has not finished connecting has no
        /// remote id, and the role must not silently default to host while
        /// that is true.
        /// </summary>
        public static bool IsHost(INetworkTransport transport, string remotePeerId)
        {
            return transport != null && IsHost(transport.LocalPeerId, remotePeerId);
        }
    }
}
