using Newtonsoft.Json;

namespace CodeTalker.Packets;

/// <summary>
/// A container class that stores all packet serialization information
/// </summary>
public static class PacketSerializer
{
  /// <summary>
  /// The serialization settings used by Code Talker. Do not modify
  /// under any circumstance or you will completely destroy the network
  /// </summary>
  internal static readonly JsonSerializerSettings JSONOptions = new()
  {
    TypeNameHandling = TypeNameHandling.None,
    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
    Formatting = Formatting.None,
    NullValueHandling = NullValueHandling.Ignore,
    DefaultValueHandling = DefaultValueHandling.Ignore
  };
}
