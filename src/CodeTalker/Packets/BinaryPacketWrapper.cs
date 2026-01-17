using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CodeTalker.Networking;
using static CodeTalker.Networking.CodeTalkerNetwork;

internal class BinaryPacketWrapper {
	internal readonly byte[] FullPacketBytes;

	internal readonly UInt64 PacketSignature;

    static SHA256 sha256 = SHA256.Create();

    internal BinaryPacketWrapper(string sig, Span<byte> rawData) {
        Span<byte> ctSig = CODE_TALKER_BINARY_SIGNATURE;
        Span<byte> signatureBytes = Encoding.UTF8.GetBytes(sig);
        PacketSignature = signatureHash(signatureBytes);

        int headerSize = ctSig.Length + sizeof(UInt64);
        FullPacketBytes = new byte[headerSize + rawData.Length];
        Span<byte> pkt = new Span<byte>(FullPacketBytes);

        // Make header
        // Header format: CODE_TALKER_BINARY_SIGNATURE PacketSignature(u64)
        ctSig.CopyTo(pkt);
        BinaryPrimitives.WriteUInt64LittleEndian(pkt.Slice(ctSig.Length), PacketSignature);
        rawData.CopyTo(pkt.Slice(headerSize));
    }

    internal BinaryPacketWrapper(Span<byte> rawPacketData) {
        PacketSignature = BinaryPrimitives.ReadUInt64LittleEndian(rawPacketData);
        FullPacketBytes = rawPacketData.Slice(sizeof(UInt64)).ToArray();
    }
}
