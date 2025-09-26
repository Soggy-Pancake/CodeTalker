using System;
using System.Collections.Generic;
using System.Text;
using CodeTalker.Packets;
using UnityEngine;

namespace CodeTalker.Examples;

public class SimpleBinaryPacket : BinaryPacketBase {

    public byte[] rawBytes;

    // You recieve the raw payload bytes here. You can parse them however you want.
    public override void Deserialize(byte[] data) {
        Debug.Log($"Received binary packet of length {data.Length}");
        foreach (var b in data)
            Debug.Log(b.ToString());

        rawBytes = data;
    }

    // Do ur deserialization here. The only limit is the max packet size of 4kb
    public override byte[] Serialize() {
        Debug.Log("Sending binary packet!");

        return new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    }

    // Anything is allowed that's utf8, just make sure its not super long (255 bytes max)
    // To calculate your max paxket size, do: 4096 - 11 - Encoding.UTF8.GetBytes(PacketSignature).Length = max payload size
    public override string PacketSignature => "👍";

}
