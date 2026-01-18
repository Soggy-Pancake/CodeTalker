using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using CodeTalker.Packets;
using Mirror;
using Newtonsoft.Json;
using Steamworks;
using ZstdSharp.Unsafe;
using static CodeTalker.Compressors;
using u64 = ulong;

namespace CodeTalker.Networking;

/// <summary>
/// The main network entry point for Code Talker
/// </summary>
public static class CodeTalkerNetwork {
    /// <summary>
    /// A delegate for receiving packet events from Code Talker
    /// </summary>
    /// <param name="header">The received packet's header</param>
    /// <param name="packet">The received packet's body</param>
    public delegate void PacketListener(PacketHeader header, PacketBase packet);

    /// <summary>
    /// A delegate for receiving binary packet events from Code Talker
    /// </summary>
    /// <param name="header">The received packet's header</param>
    /// <param name="packet">The received packet's body</param>
    public delegate void BinaryPacketListener(PacketHeader header, BinaryPacketBase packet);

    /// <summary>
    /// This should only be modified for BREAKING changes in packet structure
    /// </summary>
    private const ushort NETWORK_PACKET_VERSION = 4;
    internal static byte[] CODE_TALKER_SIGNATURE = Encoding.UTF8.GetBytes($"!CTN:PV{NETWORK_PACKET_VERSION}!");

    private static readonly Dictionary<UInt64, PacketListener> packetListeners = [];
    private static readonly Dictionary<UInt64, Func<string, PacketBase>> packetDeserializers = [];

    static u64 lastSkippedPacketSig = 0;

    struct BinaryListenerEntry {
        public BinaryPacketListener Listener;
        public Type PacketType;
    }
    private static readonly Dictionary<UInt64, BinaryListenerEntry> binaryListeners = [];

    internal static string GetTypeNameString(Type type) =>
      $"{type.Assembly.GetName().Name},{type.DeclaringType?.Name ?? "NONE"}:{type.Namespace ?? "NONE"}.{type.Name}";

    internal static bool dbg = false;
    internal static bool dev = false;

    internal static bool signatureCheck(Span<byte> data, Span<byte> sig) {
        if (data.Length < sig.Length)
            return false;

        return data.Slice(0, sig.Length).SequenceEqual(sig);
    }

    internal static u64 signatureHash(Span<byte> signature) {
        // FNV-1a 64-bit hash (https://en.wikipedia.org/wiki/Fowler%E2%80%93Noll%E2%80%93Vo_hash_function#FNV-1a_hash)
        u64 hash = 0xcbf29ce484222325; // FNV offset basis
        const u64 fnvPrime = 0x00000100000001b3;

        foreach (byte b in signature) {
            hash ^= b;
            hash *= fnvPrime;
        }

        return hash;
    }

    /// <summary>
    /// Registers a listenerEntry by packet type. Code Talker will call
    /// your listenerEntry when the specific System.Type is created from
    /// a serial network message and pass it to your listenerEntry
    /// </summary>
    /// <typeparam name="T">The exact runtime type of your packet</typeparam>
    /// <param name="listener">A PacketListener delegate to notify</param>
    /// <returns>
    /// Will return <em>TRUE</em> if the listenerEntry is added to the list and
    /// <em>FALSE</em> is a listenerEntry already exists for this type
    /// </returns>
    public static bool RegisterListener<T>(PacketListener listener) where T : PacketBase {
        var inType = typeof(T);
        string typeName = GetTypeNameString(inType);
        u64 typeHash = signatureHash(Encoding.UTF8.GetBytes(typeName));

        if (packetListeners.ContainsKey(typeHash)) {
            CodeTalkerPlugin.Log.LogError($"Failed to register listener for type {inType.FullName}! A listener for this type is already registered or a hash collision has occurred!");
            return false;
        }

        packetListeners.Add(typeHash, listener);
        packetDeserializers.Add(typeHash, (payload) => JsonConvert.DeserializeObject<T>(payload, PacketSerializer.JSONOptions)!);

        return true;
    }


    /// <summary>
    /// Registers a listenerEntry by binary packet signature. Code Talker will call
    /// your listenerEntry when a binary packet with the matching signature
    /// is recieved and will pass the raw byte array to your listenerEntry
    /// </summary>
    /// <typeparam name="T">The exact runtime type of your packet</typeparam>
    /// <param name="listener"></param>
    /// <returns>
    /// Will return <em>TRUE</em> if the listenerEntry is added to the list and
    /// <em>FALSE</em> is a listenerEntry already exists for this type
    /// </returns>
    public static bool RegisterBinaryListener<T>(BinaryPacketListener listener) where T : BinaryPacketBase, new() {

        var type = typeof(T);
        BinaryPacketBase instance = new T();
        if (instance.PacketSignature == string.Empty) {
            CodeTalkerPlugin.Log.LogError($"Failed to register binary Listener for type {type.FullName}, PacketSignature can't be empty!");
            return false;
        }

        u64 sigHash = signatureHash(Encoding.UTF8.GetBytes(instance.PacketSignature));
        if (binaryListeners.ContainsKey(sigHash)) {
            CodeTalkerPlugin.Log.LogError($"Failed to register listener for type {instance.PacketSignature}! A listener for this type is already registered or a hash collision has occurred!");
            return false;
        }

        binaryListeners.Add(sigHash, new BinaryListenerEntry { Listener = listener, PacketType = type });
        return true;
    }

    #region Legacy Packet Senders
    // This is only here for backwards compatibility with older mods so ABI doesn't break

    /// <summary>
    /// Wraps and sends a message to all clients on the Code Talker network
    /// </summary>
    /// <param name="packet">The packet to send, must be derived from PacketBase</param>
    public static void SendNetworkPacket(PacketBase packet) {
        string rawPacket = JsonConvert.SerializeObject(packet, PacketSerializer.JSONOptions);
        PacketWrapper wrapper = new(GetTypeNameString(packet.GetType()), Encoding.UTF8.GetBytes(rawPacket), PacketType.JSON);

        var rawWrapper = $"{CODE_TALKER_SIGNATURE}{JsonConvert.SerializeObject(wrapper, PacketSerializer.JSONOptions)}";
        var bytes = Encoding.UTF8.GetBytes(rawWrapper);

        if (bytes.Length > 4096) {
            CodeTalkerPlugin.Log.LogError($"Failed to send packet of type {GetTypeNameString(packet.GetType())}, packet size exceeds maximum of 4kb! Size: {bytes.Length}");
            return;
        }

        SteamMatchmaking.SendLobbyChatMsg(new(SteamLobby._current._currentLobbyID), bytes, bytes.Length);
    }

    /// <summary>
    /// Wraps and sends a binary packet to all clients on the Code Talker network
    /// </summary>
    /// <param name="packet"></param>
    public static void SendNetworkPacket(BinaryPacketBase packet) {
        byte[] serializedPacket = packet.Serialize();
        PacketWrapper wrapper = new(packet.PacketSignature, serializedPacket, PacketType.Binary);

        if (wrapper.PacketBytes.Length > 4096) {
            CodeTalkerPlugin.Log.LogError($"Failed to send binary packet of signature {packet.PacketSignature}, packet size exceeds maximum of 4kb! Size: {wrapper.PacketBytes.Length}");
            return;
        }

        SteamMatchmaking.SendLobbyChatMsg(new(SteamLobby._current._currentLobbyID), wrapper.PacketBytes, wrapper.PacketBytes.Length);
    }
    #endregion Legacy Packet Senders

    /// <summary>
    /// Wraps and sends a message to all clients on the Code Talker network
    /// </summary>
    /// <param name="packet">The packet to send, must be derived from PacketBase</param>
    /// <param name="compressionType">Automatically apply compression</param>
    /// <param name="compressionLevel">Level of compresison that will be applied</param>
    public static void SendNetworkPacket(PacketBase packet, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Fastest) {
        string rawPacket = JsonConvert.SerializeObject(packet, PacketSerializer.JSONOptions);
        PacketWrapper wrapper = new(GetTypeNameString(packet.GetType()), Encoding.UTF8.GetBytes(rawPacket), PacketType.JSON, compressionType, compressionLevel);

        if (wrapper.PacketBytes.Length > 4096) {
            CodeTalkerPlugin.Log.LogError($"Failed to send packet of type {GetTypeNameString(packet.GetType())}, packet size exceeds maximum of 4kb! Size: {wrapper.PacketBytes.Length}");
            return;
        }

        SteamMatchmaking.SendLobbyChatMsg(new(SteamLobby._current._currentLobbyID), wrapper.PacketBytes, wrapper.PacketBytes.Length);
    }

    /// <summary>
    /// Wraps and sends a binary packet to all clients on the Code Talker network
    /// </summary>
    /// <param name="packet"></param>
    /// <param name="compressionType">Automatically apply compression</param>
    /// <param name="compressionLevel">Level of compresison that will be applied</param>
    public static void SendNetworkPacket(BinaryPacketBase packet, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Fastest) {
        byte[] serializedPacket = packet.Serialize();
        PacketWrapper wrapper = new(packet.PacketSignature, serializedPacket, PacketType.Binary, compressionType, compressionLevel);

        if (wrapper.PacketBytes.Length > 4096) {
            CodeTalkerPlugin.Log.LogError($"Failed to send binary packet of signature {packet.PacketSignature}, packet size exceeds maximum of 4kb! Size: {wrapper.PacketBytes.Length}");
            return;
        }

        SteamMatchmaking.SendLobbyChatMsg(new(SteamLobby._current._currentLobbyID), wrapper.PacketBytes, wrapper.PacketBytes.Length);
    }

    /// <summary>
    /// Wraps and sends a binary packet to all clients on the Code Talker network
    /// </summary>
    /// <param name="packet"></param>
    [Obsolete("Use SendNetworkPacket(BinaryPacketBase) instead")]
    public static void SendBinaryNetworkPacket(BinaryPacketBase packet) {
        byte[] serializedPacket = packet.Serialize();
        PacketWrapper wrapper = new(packet.PacketSignature, serializedPacket, PacketType.Binary);

        if (wrapper.PacketBytes.Length > 4096) {
            CodeTalkerPlugin.Log.LogError($"Failed to send binary packet of signature {packet.PacketSignature}, packet size exceeds maximum of 4kb! Size: {wrapper.PacketBytes.Length}");
            return;
        }

        SteamMatchmaking.SendLobbyChatMsg(new(SteamLobby._current._currentLobbyID), wrapper.PacketBytes, wrapper.PacketBytes.Length);
    }

    #region SteamNetworkingMessages
    /// <summary>
    /// Sends a binary packet to a specific player on the Code Talker network (P2P)
    /// </summary>
    /// <param name="player"></param>
    /// <param name="packet"></param>
    /// <param name="compressionType">Automatically apply compression</param>
    /// <param name="compressionLevel">Level of compresison that will be applied</param>
    public static void SendNetworkPacket(Player player, BinaryPacketBase packet, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Fastest) {
        byte[] serializedPacket = packet.Serialize();
        PacketWrapper wrapper = new(packet.PacketSignature, serializedPacket, PacketType.Binary, compressionType, compressionLevel, player.netId);
        SendSteamNetworkingMessage(new CSteamID(ulong.Parse(player.Network_steamID)), wrapper);
    }

    /// <summary>
    /// Sends a JSON packet to a specific player on the Code Talker network (P2P)
    /// </summary>
    /// <param name="player"></param>
    /// <param name="packet"></param>
    /// <param name="compressionType">Automatically apply compression</param>
    /// <param name="compressionLevel">Level of compresison that will be applied</param>
    public static void SendNetworkPacket(Player player, PacketBase packet, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Fastest) {
        string serializedPacket = JsonConvert.SerializeObject(packet, PacketSerializer.JSONOptions);
        PacketWrapper wrapper = new(GetTypeNameString(packet.GetType()), Encoding.UTF8.GetBytes(serializedPacket), PacketType.JSON, compressionType, compressionLevel, player.netId);
        SendSteamNetworkingMessage(new CSteamID(ulong.Parse(player.Network_steamID)), wrapper);
    }

    /// <summary>
    /// Sends a binary packet to a specific player on the Code Talker network (P2P)
    /// </summary>
    /// <param name="steamID"></param>
    /// <param name="packet"></param>
    /// <param name="compressionType">Automatically apply compression</param>
    /// <param name="compressionLevel">Level of compresison that will be applied</param>
    public static void SendNetworkPacket(ulong steamID, BinaryPacketBase packet, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Fastest) {
        byte[] serializedPacket = packet.Serialize();
        PacketWrapper wrapper = new(packet.PacketSignature, serializedPacket, PacketType.Binary, compressionType, compressionLevel);
        SendSteamNetworkingMessage(new CSteamID(steamID), wrapper);
    }

    /// <summary>
    /// Sends a JSON packet to a specific player on the Code Talker network (P2P)
    /// </summary>
    /// <param name="steamID"></param>
    /// <param name="packet"></param>
    /// <param name="compressionType">Automatically apply compression</param>
    /// <param name="compressionLevel">Level of compresison that will be applied</param>
    public static void SendNetworkPacket(ulong steamID, PacketBase packet, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Fastest) {
        string serializedPacket = JsonConvert.SerializeObject(packet, PacketSerializer.JSONOptions);
        PacketWrapper wrapper = new(GetTypeNameString(packet.GetType()), Encoding.UTF8.GetBytes(serializedPacket), PacketType.JSON, compressionType, compressionLevel);
        SendSteamNetworkingMessage(new CSteamID(steamID), wrapper);
    }

    /// <summary>
    /// Handles sending the packet over Steam Networking Messages
    /// </summary>
    /// <param name="id"></param>
    /// <param name="wrappedPacket"></param>
    internal static void SendSteamNetworkingMessage(CSteamID id, PacketWrapper wrappedPacket) {
        SteamNetworkingIdentity target = new();
        target.SetSteamID(id);
        GCHandle packetBytesHandle = GCHandle.Alloc(wrappedPacket.PacketBytes, GCHandleType.Pinned);
        try {
            IntPtr dataPtr = packetBytesHandle.AddrOfPinnedObject();
            SteamNetworkingMessages.SendMessageToUser(ref target, dataPtr, (uint)wrappedPacket.PacketBytes.Length, 0, 0);
        } catch { }
        packetBytesHandle.Free();
    }
    #endregion SteamNetworkingMessages


    internal static void OnNetworkMessage(LobbyChatMsg_t message) {
        if (dev)
            CodeTalkerPlugin.Log.LogDebug("Called back!");

        const int bufferSize = 4096; //4kb buffer
        byte[] rawData = new byte[bufferSize];

        var ret = SteamMatchmaking.GetLobbyChatEntry(new(message.m_ulSteamIDLobby), (int)message.m_iChatID, out var senderID, rawData, bufferSize, out var messageType);
        Span<byte> b = new Span<byte>(rawData);

        if (signatureCheck(rawData, CODE_TALKER_SIGNATURE))
            HandleNetworkMessage(senderID, b.Slice(CODE_TALKER_SIGNATURE.Length, ret - CODE_TALKER_SIGNATURE.Length));
    }

    internal static void OnSteamSessionRequest(SteamNetworkingMessagesSessionRequest_t request) {
        if (dev)
            CodeTalkerPlugin.Log.LogDebug($"Accepting messages session request from {request.m_identityRemote.GetSteamID()}");
        SteamNetworkingMessages.AcceptSessionWithUser(ref request.m_identityRemote);
    }

    // Quick function to sanitize control characters from binary strings for logging
    internal static string BinaryToUtf8String(Span<byte> data) {
        return new string(Encoding.UTF8.GetString(data).Select(c => char.IsControl(c) && c != '\r' && c != '\n' ? '�' : c).ToArray());
    }

    internal static string BinaryToHexString(Span<byte> data) {
        return BitConverter.ToString(data.ToArray()).Replace("-", "");
    }

#region Message Handling
    static object? packet;
    static Type? type;
    static PacketWrapper? wrapper;

    static void printWrapperInfo(CSteamID senderID, PacketWrapper wrapper, LogLevel level = LogLevel.Debug) {
        CodeTalkerPlugin.Log.Log(level, $"Sender: {senderID} Wrapper: {wrapper}");
        CodeTalkerPlugin.Log.Log(level, $"Message hex: {BinaryToHexString(wrapper.PacketBytes)}");
    }

    internal static void ExecuteHandler(Delegate listener, CSteamID senderID, object packetObj) {
        try {
            if (packetObj is PacketBase pkt) {
                if (dbg) {
                    printWrapperInfo(senderID, wrapper!);
                    CodeTalkerPlugin.Log.LogDebug($"Sending an event for \"{GetTypeNameString(pkt.GetType())}\"");
                }
                ((PacketListener)listener).Invoke(new PacketHeader(senderID.m_SteamID), pkt);
            } else if (packetObj is BinaryPacketBase bPkt) {
                if (dbg) {
                    printWrapperInfo(senderID, wrapper!);
                    CodeTalkerPlugin.Log.LogDebug($"Sending an event for \"{bPkt.PacketSignature}\"");
                }
                ((BinaryPacketListener)listener).Invoke(new PacketHeader(senderID.m_SteamID), bPkt);
            }
        } catch (Exception ex) {
            var plugins = Chainloader.PluginInfos;
            var mod = plugins.Values.Where(mod => mod.Instance?.GetType().Assembly == type?.Assembly).FirstOrDefault();

            //Happy lil ternary
            string modName = mod != null
              ? $"{mod.Metadata.Name} version {mod.Metadata.Version}"
              : type?.Assembly.GetName().Name ?? "";

            //Big beefin' raw string literal with interpolation
            CodeTalkerPlugin.Log.LogError($"""
The following mod encountered an error while responding to a network packet, please do not report this as a CodeTalker error!
Mod: {modName}
StackTrace:
{ex}
""");
        }
    }

    internal static void HandleNetworkMessage(CSteamID senderID, Span<byte> rawData) {
        try {
            wrapper = new PacketWrapper(rawData);
        } catch (Exception ex) {
            CodeTalkerPlugin.Log.LogError($"Failed to create packet wrapper for valid packet!\nStackTrace: {ex}");
            return;
        }

        if (wrapper.TargetNetId > 0) {
            // Targeted packet
            if (Player._mainPlayer == null || Player._mainPlayer.netId != wrapper.TargetNetId) {
                if (dev)
                    CodeTalkerPlugin.Log.LogDebug($"Target netId doesn't match this client! Targeted netId: {wrapper.TargetNetId}");
                return;
            }
        }

        switch (wrapper.PacketType) {
            case PacketType.JSON:
                if (dev)
                    CodeTalkerPlugin.Log.LogDebug($"Recieved JSON packet!");

                if (!packetListeners.TryGetValue(wrapper.PacketSignature, out var listener)) {
                    if (dbg && wrapper.PacketSignature != lastSkippedPacketSig) {
                        CodeTalkerPlugin.Log.LogDebug($"Skipping packet of type: {wrapper.PacketSignature} because this client does not have it installed, this is safe!");
                        lastSkippedPacketSig = wrapper.PacketSignature;
                    }
                    return;
                }

                try {
                    if (packetDeserializers[wrapper.PacketSignature](Encoding.UTF8.GetString(wrapper.PacketBytes)) is PacketBase inPacket) {
                        type = inPacket.GetType();
                        packet = inPacket;
                    } else
                        return;
                } catch (Exception ex) {
                    CodeTalkerPlugin.Log.LogError($"""
Error while unwrapping a packet!
Exception: {ex.GetType().Name}
Expected Type: {wrapper.PacketSignature}
""");
                    return;
                }

                if (dev)
                    CodeTalkerPlugin.Log.LogDebug($"Raw message: {BinaryToHexString(rawData)}");

                ExecuteHandler(listener, senderID, packet);
                break;

            case PacketType.Binary:
                if (dev)
                    CodeTalkerPlugin.Log.LogDebug($"Recieved binary packet!");

                if (!binaryListeners.TryGetValue(wrapper.PacketSignature, out var listenerEntry)) {
                    if (dev && (wrapper.PacketSignature != lastSkippedPacketSig)) {
                        CodeTalkerPlugin.Log.LogDebug($"Skipping binary packet of unknown signature hash: 0x{wrapper.PacketSignature.ToString("x2")} because this client does not have it installed, this is safe!");
                        lastSkippedPacketSig = wrapper.PacketSignature;
                        return;
                    }
                }

                try {
                    type = listenerEntry.PacketType;
                    object instance = Activator.CreateInstance(type);
                    if (instance is BinaryPacketBase bPacket) {
                        packet = instance;
                        try {
                            bPacket.Deserialize(wrapper.PacketBytes);
                        } catch (Exception ex) {
                            CodeTalkerPlugin.Log.LogError($"Error while deserializing binary packet! THIS IS NOT A CODETALKER ISSUE! DO NOT REPORT THIS TO THE CODETALKER DEV!!\nStackTrace: {ex}");
                            printWrapperInfo(senderID, wrapper, LogLevel.Error);
                            return;
                        }
                    } else {
                        throw new InvalidOperationException("Failed to create instance of binary packet type!");
                    }
                } catch (Exception ex) {
                    CodeTalkerPlugin.Log.LogError($"Error while creating binary packet instance! This should be reported to either codetalker or the plugin dev!\nStackTrace: {ex}");
                    printWrapperInfo(senderID, wrapper, LogLevel.Error);
                    return;
                }

                if (dev)
                    CodeTalkerPlugin.Log.LogDebug($"Raw message: {BinaryToHexString(rawData)}");

                ExecuteHandler(listenerEntry.Listener, senderID, packet);
                break;

            default:
                CodeTalkerPlugin.Log.LogError($"UNKNOWN PACKET TYPE RECIEVED! THERE IS LIKELY A BUG IN CODEYAPPER. REPORT THIS TO @Soggy_Pancake! {wrapper}\nRecieved data: {BinaryToHexString(rawData)}");
                break;
        }
    }
#endregion Message Handling
}
