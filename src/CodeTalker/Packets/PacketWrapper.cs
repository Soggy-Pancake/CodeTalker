using System;
using System.IO.Compression;
using System.Text;
using CodeTalker;
using CodeTalker.Networking;
using static CodeTalker.Networking.CodeTalkerNetwork;
using static CodeTalker.Compressors;
using System.Buffers.Binary;
using System.Linq;
using Newtonsoft.Json;

internal enum PacketType : Byte {
    JSON,
    Binary
}

internal class PacketWrapper {
    internal byte[] PacketBytes {
        get {
            if (!isCompressed)
                return _PacketBytes;
            
            if (compression == CompressionType.Brotli) {
                Type? type = null;
                if (PacketType == PacketType.JSON && packetDeserializers.TryGetValue(PacketSignature, out var a))
                    type = a.Method.ReturnType;

                if (PacketType == PacketType.Binary && binaryListeners.TryGetValue(PacketSignature, out var b))
                    type = b.PacketType;

                CodeTalkerPlugin.Log.LogError($"[Affected packet: {(type != null ? GetTypeNameString(type) : "Unknown Type")}] Brotli compression is currently DISABLED due to .NET brotli decoder CVE! (CVE-2026-111497)");
                isCompressed = false;
                return [];
            }
            
            _PacketBytes = Decompress(_PacketBytes, compression);
            isCompressed = false;
            return _PacketBytes;
        }
    }

    byte[] _PacketBytes;

    internal readonly UInt64 PacketSignature;

    // This should allow both JSON and binary without having a different wrapper for each
    internal readonly PacketType PacketType;

    internal readonly CompressionType compression = CompressionType.None;
    private           bool            isCompressed;

    internal readonly uint TargetNetId; // Will be used to route packets to specific clients in future updates

    // For sending packets
    internal PacketWrapper(string sig, Span<byte> rawData, PacketType packetType, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Fastest, uint targetNetId = 0) {
        Span<byte> ctSig = CODE_TALKER_SIGNATURE;
        Span<byte> signatureBytes = Encoding.UTF8.GetBytes(sig);
        this.PacketType = packetType; // just for debugging
        PacketSignature = signatureHash(signatureBytes);

        if (compressionType != CompressionType.None)
            rawData = Compress(rawData.ToArray(), compressionType, compressionLevel);

        int headerSize = ctSig.Length + sizeof(UInt64) + 5;
        _PacketBytes = new byte[headerSize + rawData.Length];
        Span<byte> pkt = new Span<byte>(_PacketBytes);

        // Make header
        // Header format: CODE_TALKER_SIGNATURE PacketSignatureHash(u64)
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
    internal PacketWrapper(Span<byte> rawPacketData) {
        //CodeTalkerPlugin.Log.LogDebug($"raw packet data hex: {BitConverter.ToString(rawPacketData).Replace("-", "")}");
        PacketSignature = BinaryPrimitives.ReadUInt64LittleEndian(rawPacketData);
        PacketType = (PacketType)(rawPacketData[8] & 0x0f);
        compression = (CompressionType)((rawPacketData[8] >> 4) & 0x0f);
        isCompressed = compression != CompressionType.None;

        TargetNetId = BinaryPrimitives.ReadUInt32LittleEndian(rawPacketData.Slice(9, 4));
        _PacketBytes = rawPacketData.Slice(13).ToArray();
    }

    public override string ToString() {
        return $"[PacketSig: {PacketSignature} Target: {TargetNetId} Type: {PacketType} Size: {_PacketBytes.Length} Compression: {compression}]";
    }
}
