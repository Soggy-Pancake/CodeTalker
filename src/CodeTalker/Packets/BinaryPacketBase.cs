using System;

namespace CodeTalker.Packets;

/// <summary>
/// The base packet type all binary packets must be derived from
/// </summary>
public abstract class BinaryPacketBase {

    /// <summary>
    /// Used to identify the packet type. Must not encode to more than 255 bytes. 
    /// </summary>
    public abstract string PacketSignature { get; }

    /// <summary>
    /// Your custom function to serialize your packet into bytes
    /// </summary>
    /// <returns></returns>
    public abstract byte[] Serialize();

    /// <summary>
    /// Your custom function to deserialize your packet from bytes
    /// </summary>
    /// <param name="data"></param>
    public abstract void Deserialize(byte[] data);
}
