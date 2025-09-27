using System.Text;
using BepInEx;
using BepInEx.Logging;
using CodeTalker.Networking;
using CodeTalker.PacketExamples;
using CodeTalker.Packets;

namespace CodeTalker;

/*
  This example goes over how to properly register a binary packet
  listener with CodeTalker. See also: ExampleSimpleBinaryPacket.cs
*/

[BepInPlugin("dev.mamallama.CodeTalkerExampleBinaryListener", "Code Talker Binary Example", "0.0.1")]
public class CodeTalkerExamplePlugin : BaseUnityPlugin {
    internal static ManualLogSource Log = null!;

    private void Awake() {

        Log = Logger;

        // Log our awake here so we can see it in LogOutput.txt file
        Log.LogInfo($"Code talker example is loaded");

        CodeTalkerNetwork.RegisterBinaryListener<SimpleBinaryPacket>(ReceiveExamplePacket);
        Log.LogMessage("Created a packet listener");
    }

    internal static void ReceiveExamplePacket(PacketHeader header, BinaryPacketBase packet) {
        if (packet is SimpleBinaryPacket example) {
            Log.LogInfo($"Packet\n  From: {header.SenderID} (isHost: {header.SenderIsLobbyOwner})\n  PayLoad: {example.Payload}");
        }
    }

    internal static void SendPacketTest() {
        CodeTalkerNetwork.SendNetworkPacket(new SimpleBinaryPacket());
    }

}