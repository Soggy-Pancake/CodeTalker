using System;
using System.Text;
using CodeTalker.Networking;

internal class BinaryPacketWrapper {
	internal readonly byte[] Data;

	internal readonly string PacketSignature;

    internal BinaryPacketWrapper(string sig, byte[] rawData) {

        string binSig = CodeTalkerNetwork.CODE_TALKER_BINARY_SIGNATURE;
        int sigSize = CodeTalkerNetwork.BINARY_PACKET_SIG_SIZE;

        if (sig.Length > 8)
            sig = sig[..sigSize];

        PacketSignature = sig;

        int sigLen = binSig.Length + sigSize;
        binSig += sig;

        // Make a new array with the extra space for the signature and memcopy
        Data = new byte[sigLen + rawData.Length];
        Array.Copy(Encoding.ASCII.GetBytes(binSig), Data, binSig.Length);
        Array.Copy(rawData, 0, Data, sigLen, rawData.Length);
    }

    internal BinaryPacketWrapper(byte[] rawPacketData) {
        int sigSize = CodeTalkerNetwork.BINARY_PACKET_SIG_SIZE;
        PacketSignature = Encoding.ASCII.GetString(rawPacketData, 0, sigSize);
        Data = rawPacketData[sigSize..];
    }
}
