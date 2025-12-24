using System.Collections.Generic;
using Newtonsoft.Json;

namespace CodeTalker.Packets;

internal class PacketWrapper
{
  [JsonProperty]
  public readonly string PacketType;

  [JsonProperty]
  public readonly string PacketPayload;

  [JsonConstructor]
  public PacketWrapper(string PacketType, string PacketPayload)
  {
    this.PacketType = PacketType;
    this.PacketPayload = PacketPayload;
  }
}
