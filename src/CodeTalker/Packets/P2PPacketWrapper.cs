using System;
using System.IO.Compression;
using System.Text;
using CodeTalker;
using CodeTalker.Networking;
using static CodeTalker.Networking.CodeTalkerNetwork;
using static CodeTalker.Compressors;
using System.Buffers.Binary;
using System.Linq;

internal enum P2PPacketType : Byte {
    JSON,
    Binary
}

// This could replace both of the other wrappers as well at some point, but I havent done that
// becuase I dont wanna make breaking changes rn
internal class P2PPacketWrapper {
    internal readonly byte[] PacketBytes;

    internal readonly UInt64 PacketSignature;

    // This should allow both JSON and binary without having a different wrapper for each
    internal readonly P2PPacketType PacketType;

    internal readonly CompressionType compression = CompressionType.None;

    internal readonly uint TargetNetId; // Will be used to route packets to specific clients in future updates

    // For sending packets
    internal P2PPacketWrapper(string sig, Span<byte> rawData, P2PPacketType packetType, CompressionType compressionType, CompressionLevel compressionLevel, uint targetNetId = 0) {
        Span<byte> ctSig = CODE_TALKER_P2P_SIGNATURE;
        Span<byte> signatureBytes = Encoding.UTF8.GetBytes(sig);
        PacketSignature = signatureHash(signatureBytes);

        if (compressionType != CompressionType.None)
            rawData = Compress(rawData.ToArray(), compressionType, compressionLevel);

        int headerSize = ctSig.Length + sizeof(UInt64) + 5;
        byte[] packetOut = new byte[headerSize + rawData.Length];
        Span<byte> pkt = new Span<byte>(packetOut);

        // Make header
        // Header format: CODE_TALKER_P2P_SIGNATURE PacketSignatureHash(u64)
        // [packetCompression(high nibble) packetType(low nibble)] (1 byte)
        // targetNetId(4 bytes)
        int offset = 0;
        ctSig.CopyTo(pkt.Slice(offset));
        offset += ctSig.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(pkt.Slice(offset), PacketSignature);
        offset += sizeof(UInt64);
        pkt[offset++] = (byte)((((int)compressionType << 4) & 0xf0) + ((int)packetType & 0x0f));
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.Slice(offset), targetNetId);
        offset += sizeof(uint);

        rawData.CopyTo(pkt.Slice(offset));
    }

    // Reading packet in
    internal P2PPacketWrapper(Span<byte> rawPacketData) {
        //CodeTalkerPlugin.Log.LogDebug($"raw packet data hex: {BitConverter.ToString(rawPacketData).Replace("-", "")}");
        PacketSignature = BinaryPrimitives.ReadUInt64LittleEndian(rawPacketData);
        PacketType = (P2PPacketType)(rawPacketData[8] & 0x0f);
        compression = (CompressionType)((rawPacketData[8] >> 4) & 0x0f);

        TargetNetId = BinaryPrimitives.ReadUInt32LittleEndian(rawPacketData.Slice(9, 4));
        PacketBytes = rawPacketData.Slice(13).ToArray();

        if (compression != CompressionType.None)
            PacketBytes = Decompress(PacketBytes, compression);
        //CodeTalkerPlugin.Log.LogDebug($"Packet hex (no header): {BitConverter.ToString(PacketBytes).Replace("-", "")}");
    }

    public override string ToString() {
        return $"[Type: {PacketType} Compression: {compression} Target: {TargetNetId}]";
    }
}
