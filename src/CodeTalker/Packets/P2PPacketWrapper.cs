using System;
using System.IO.Compression;
using System.Text;
using CodeTalker;
using CodeTalker.Networking;

using static CodeTalker.Compressors;

internal enum P2PPacketType : Byte { 
    JSON,
    Binary
}

// This could replace both of the other wrappers as well at some point, but I havent done that
// becuase I dont wanna make breaking changes rn
internal class P2PPacketWrapper {
    internal readonly byte[] PacketBytes;

    internal readonly string PacketSignature;

    // This should allow both JSON and binary without having a different wrapper for each
    internal readonly P2PPacketType PacketType;

    internal readonly CompressionType compression = CompressionType.None;

    internal readonly uint TargetNetId; // Will be used to route packets to specific clients in future updates

    // For sending packets
    internal P2PPacketWrapper(string sig, byte[] rawData, P2PPacketType packetType, CompressionType compressionType, CompressionLevel compressionLevel, uint targetNetId = 0) {
        PacketSignature = sig;
        PacketType = packetType;
        TargetNetId = targetNetId;

        string ctSig = CodeTalkerNetwork.CODE_TALKER_P2P_SIGNATURE;
        byte[] signatureBytes = Encoding.UTF8.GetBytes(sig);

        int headerSize = ctSig.Length + 1 + signatureBytes.Length + 5;

        // Make header
        // Header format: CODE_TALKER_P2P_SIGNATURE PacketSignatureLength(1 Byte) PacketSignature(PacketSigLen bytes long)
        // [packetCompression(high nibble) packetType(low nibble)] (1 byte)
        // targetNetId(4 bytes)
        byte[] header = new byte[headerSize];
        int offset = 0;
        Array.Copy(Encoding.ASCII.GetBytes(ctSig), header, ctSig.Length);
        offset += ctSig.Length;
        header[offset++] = (byte)signatureBytes.Length;
        Array.Copy(signatureBytes, 0, header, offset, signatureBytes.Length);
        offset += signatureBytes.Length;

        header[offset++] = (byte)((((int)compressionType << 4) & 0xf0) + ((int)packetType & 0x0f));
        Array.Copy(BitConverter.GetBytes(targetNetId), 0, header, offset, 4);

        if (compressionType != CompressionType.None)
            rawData = Compress(rawData, compressionType, compressionLevel);

        // Make a new array with the extra space for the signature then memcopy
        PacketBytes = new byte[headerSize + rawData.Length];
        Array.Copy(header, PacketBytes, header.Length);
        Array.Copy(rawData, 0, PacketBytes, headerSize, rawData.Length);
    }

    // Reading packet in
    internal P2PPacketWrapper(byte[] rawPacketData) {
        //CodeTalkerPlugin.Log.LogDebug($"raw packet data hex: {BitConverter.ToString(rawPacketData).Replace("-", "")}");
        int sigSize = rawPacketData[0];
        PacketSignature = Encoding.UTF8.GetString(rawPacketData, 1, sigSize);
        int offset = sigSize + 1;

        PacketType = (P2PPacketType)(rawPacketData[offset] & 0x0f);
        compression = (CompressionType)((rawPacketData[offset] >> 4) & 0x0f);
        offset += 1;

        TargetNetId = BitConverter.ToUInt32(rawPacketData, offset);
        offset += 4;
        PacketBytes = rawPacketData[offset..];

        if (compression != CompressionType.None)
            PacketBytes = Decompress(PacketBytes, compression);
        //CodeTalkerPlugin.Log.LogDebug($"Packet hex (no header): {BitConverter.ToString(PacketBytes).Replace("-", "")}");
    }
}
