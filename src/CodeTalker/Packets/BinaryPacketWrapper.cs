using System;
using System.Text;
using CodeTalker.Networking;

internal class BinaryPacketWrapper {
	internal readonly byte[] FullPacketBytes;

	internal readonly string PacketSignature;

    internal BinaryPacketWrapper(string sig, byte[] rawData) {

        PacketSignature = sig;
        string binSig = CodeTalkerNetwork.CODE_TALKER_BINARY_SIGNATURE;

        int headerSize = binSig.Length + 1 + signatureBytes.Length;

        // Make header
        // Header format: CODE_TALKER_BINARY_SIGNATURE PacketSignatureLength(1 Byte) PacketSignature(PacketSigLen bytes long)
        byte[] header = new byte[headerSize];
        Array.Copy(Encoding.ASCII.GetBytes(binSig), header, binSig.Length);
        header[binSig.Length] = (byte)signatureBytes.Length;
        Array.Copy(signatureBytes, 0, header, binSig.Length + 1, signatureBytes.Length);

        // Make a new array with the extra space for the signature and memcopy
        FullPacketBytes = new byte[headerSize + rawData.Length];
        Array.Copy(header, FullPacketBytes, header.Length);
        Array.Copy(rawData, 0, FullPacketBytes, headerSize, rawData.Length);
    }

    internal BinaryPacketWrapper(byte[] rawPacketData) {
        int sigSize = rawPacketData[0];
        PacketSignature = Encoding.UTF8.GetString(rawPacketData, 1, sigSize);
        FullPacketBytes = rawPacketData[(sigSize + 1)..];
    }
}
