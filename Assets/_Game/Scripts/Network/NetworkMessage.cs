using System;
using UnityEngine;

namespace JetFighter.Network
{
    /// <summary>
    /// Kinds of payload on the wire.
    ///
    /// A one-byte tag in front of every message. Without it the receiver has
    /// to infer the type from the length, which works exactly until two
    /// message types happen to be the same size -- and then produces garbage
    /// state rather than an error.
    /// </summary>
    public enum MessageType : byte
    {
        Unknown = 0,
        PlayerState = 1,
        PlayerFired = 2,
        EnemyState = 3,
    }

    /// <summary>
    /// One remote jet's state at a moment.
    ///
    /// Deliberately small and absolute: position, rotation, a fire flag and a
    /// sequence number. No velocity, no input, no prediction -- the spec is
    /// explicit that client-side prediction and interpolation are not
    /// justified until playtesting shows they are needed, and they are real
    /// complexity to add speculatively.
    /// </summary>
    [Serializable]
    public struct PlayerStatePayload
    {
        public Vector3 position;
        public float bankAngle;
        public uint sequence;
    }

    /// <summary>
    /// Framing for everything on the wire.
    ///
    /// Hand-rolled rather than JsonUtility: at 20Hz for a whole session this
    /// runs thousands of times, and JSON of a Vector3 is roughly ten times the
    /// bytes and allocates a string per packet. GKMatch is also byte-oriented,
    /// so a text format would be encoded and decoded for nothing.
    /// </summary>
    public static class NetworkMessage
    {
        /// <summary>Tag byte plus 3 floats, 1 float, 1 uint.</summary>
        public const int PlayerStateSize = 1 + 12 + 4 + 4;

        public static MessageType TypeOf(byte[] payload)
        {
            if (payload == null || payload.Length < 1)
            {
                return MessageType.Unknown;
            }
            byte tag = payload[0];
            return Enum.IsDefined(typeof(MessageType), tag) ? (MessageType)tag : MessageType.Unknown;
        }

        public static byte[] EncodePlayerState(PlayerStatePayload state)
        {
            var buffer = new byte[PlayerStateSize];
            buffer[0] = (byte)MessageType.PlayerState;
            int offset = 1;
            WriteFloat(buffer, ref offset, state.position.x);
            WriteFloat(buffer, ref offset, state.position.y);
            WriteFloat(buffer, ref offset, state.position.z);
            WriteFloat(buffer, ref offset, state.bankAngle);
            WriteUInt(buffer, ref offset, state.sequence);
            return buffer;
        }

        /// <summary>
        /// Decodes a player-state payload. Returns false on anything that is
        /// not one, rather than throwing or half-filling the struct.
        ///
        /// A malformed packet is a normal event on a real link -- a truncated
        /// send, a build-version mismatch, a peer sending a message this build
        /// does not know. Throwing here would take the run down over one bad
        /// packet out of thousands.
        /// </summary>
        public static bool TryDecodePlayerState(byte[] payload, out PlayerStatePayload state)
        {
            state = default;
            if (TypeOf(payload) != MessageType.PlayerState || payload.Length < PlayerStateSize)
            {
                return false;
            }
            int offset = 1;
            float x = ReadFloat(payload, ref offset);
            float y = ReadFloat(payload, ref offset);
            float z = ReadFloat(payload, ref offset);
            float bank = ReadFloat(payload, ref offset);
            uint sequence = ReadUInt(payload, ref offset);

            // NaN reaches here from a peer that divided by zero, and it is
            // contagious: assigned to a Transform it poisons every subsequent
            // physics query on that object. Rejecting the packet loses one
            // frame of remote position; accepting it corrupts the scene.
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !IsFinite(bank))
            {
                return false;
            }

            state = new PlayerStatePayload
            {
                position = new Vector3(x, y, z),
                bankAngle = bank,
                sequence = sequence,
            };
            return true;
        }

        public static byte[] EncodeFired(uint sequence)
        {
            var buffer = new byte[1 + 4];
            buffer[0] = (byte)MessageType.PlayerFired;
            int offset = 1;
            WriteUInt(buffer, ref offset, sequence);
            return buffer;
        }

        public static bool TryDecodeFired(byte[] payload, out uint sequence)
        {
            sequence = 0;
            if (TypeOf(payload) != MessageType.PlayerFired || payload.Length < 5)
            {
                return false;
            }
            int offset = 1;
            sequence = ReadUInt(payload, ref offset);
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void WriteFloat(byte[] buffer, ref int offset, float value)
        {
            BitConverter.GetBytes(value).CopyTo(buffer, offset);
            offset += 4;
        }

        private static float ReadFloat(byte[] buffer, ref int offset)
        {
            float value = BitConverter.ToSingle(buffer, offset);
            offset += 4;
            return value;
        }

        private static void WriteUInt(byte[] buffer, ref int offset, uint value)
        {
            BitConverter.GetBytes(value).CopyTo(buffer, offset);
            offset += 4;
        }

        private static uint ReadUInt(byte[] buffer, ref int offset)
        {
            uint value = BitConverter.ToUInt32(buffer, offset);
            offset += 4;
            return value;
        }
    }
}
