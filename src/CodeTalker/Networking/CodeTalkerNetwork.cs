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
using u64 = System.UInt64;

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

    internal static byte[] CODE_TALKER_SIGNATURE = Encoding.UTF8.GetBytes($"!!CODE_TALKER_NETWORKING:PV{NETWORK_PACKET_VERSION}!!");
    internal static byte[] CODE_TALKER_BINARY_SIGNATURE = Encoding.UTF8.GetBytes($"!CTN:BIN{NETWORK_PACKET_VERSION}!");
    internal static byte[] CODE_TALKER_P2P_SIGNATURE = Encoding.UTF8.GetBytes($"!CTN:P2P{NETWORK_PACKET_VERSION}!");

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
        if (binaryListeners.ContainsKey(sigHash))
            return false;

        binaryListeners.Add(sigHash, new BinaryListenerEntry { Listener = listener, PacketType = type });
        return true;
    }

    /// <summary>
    /// Wraps and sends a message to all clients on the Code Talker network
    /// </summary>
    /// <param name="packet">The packet to send, must be derived from PacketBase</param>
    public static void SendNetworkPacket(PacketBase packet) {
        string rawPacket = JsonConvert.SerializeObject(packet, PacketSerializer.JSONOptions);
        PacketWrapper wrapper = new(GetTypeNameString(packet.GetType()), rawPacket);

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
        BinaryPacketWrapper wrapper = new(packet.PacketSignature, serializedPacket);

        if (wrapper.FullPacketBytes.Length > 4096) {
            CodeTalkerPlugin.Log.LogError($"Failed to send binary packet of signature {packet.PacketSignature}, packet size exceeds maximum of 4kb! Size: {wrapper.FullPacketBytes.Length}");
            return;
        }

        SteamMatchmaking.SendLobbyChatMsg(new(SteamLobby._current._currentLobbyID), wrapper.FullPacketBytes, wrapper.FullPacketBytes.Length);
    }

    /// <summary>
    /// Wraps and sends a binary packet to all clients on the Code Talker network
    /// </summary>
    /// <param name="packet"></param>
    [Obsolete("Use SendNetworkPacket(BinaryPacketBase) instead")]
    public static void SendBinaryNetworkPacket(BinaryPacketBase packet) {
        byte[] serializedPacket = packet.Serialize();
        BinaryPacketWrapper wrapper = new(packet.PacketSignature, serializedPacket);

        if (wrapper.FullPacketBytes.Length > 4096) {
            CodeTalkerPlugin.Log.LogError($"Failed to send binary packet of signature {packet.PacketSignature}, packet size exceeds maximum of 4kb! Size: {wrapper.FullPacketBytes.Length}");
            return;
        }

        SteamMatchmaking.SendLobbyChatMsg(new(SteamLobby._current._currentLobbyID), wrapper.FullPacketBytes, wrapper.FullPacketBytes.Length);
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
        P2PPacketWrapper wrapper = new(packet.PacketSignature, serializedPacket, P2PPacketType.Binary, compressionType, compressionLevel, player.netId);
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
        PacketWrapper jsonWrapper = new(GetTypeNameString(packet.GetType()), serializedPacket);
        //byte[] rawPacket = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(jsonWrapper, PacketSerializer.JSONOptions));
        P2PPacketWrapper wrapper = new(GetTypeNameString(packet.GetType()), Encoding.UTF8.GetBytes(serializedPacket), P2PPacketType.JSON, compressionType, compressionLevel, player.netId);
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
        P2PPacketWrapper wrapper = new(packet.PacketSignature, serializedPacket, P2PPacketType.Binary, compressionType, compressionLevel);
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
        P2PPacketWrapper wrapper = new(GetTypeNameString(packet.GetType()), Encoding.UTF8.GetBytes(serializedPacket), P2PPacketType.JSON, compressionType, compressionLevel);
        SendSteamNetworkingMessage(new CSteamID(steamID), wrapper);
    }

    /// <summary>
    /// Handles sending the packet over Steam Networking Messages
    /// </summary>
    /// <param name="id"></param>
    /// <param name="wrappedPacket"></param>
    internal static void SendSteamNetworkingMessage(CSteamID id, P2PPacketWrapper wrappedPacket) {
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
        if (dbg)
            CodeTalkerPlugin.Log.LogDebug("Called back!");

        const int bufferSize = 4096; //4kb buffer
        byte[] rawData = new byte[bufferSize];

        var ret = SteamMatchmaking.GetLobbyChatEntry(new(message.m_ulSteamIDLobby), (int)message.m_iChatID, out var senderID, rawData, bufferSize, out var messageType);
        Span<byte> b = new Span<byte>(rawData);

        var test = Encoding.UTF8.GetString(b.Slice(0, ret));

        if (signatureCheck(rawData, CODE_TALKER_SIGNATURE))
            HandleJSONMessage(senderID, b.Slice(CODE_TALKER_SIGNATURE.Length, ret - CODE_TALKER_SIGNATURE.Length));
        else if (signatureCheck(rawData, CODE_TALKER_BINARY_SIGNATURE))
            HandleBinaryMessage(senderID, b.Slice(CODE_TALKER_BINARY_SIGNATURE.Length, ret - CODE_TALKER_BINARY_SIGNATURE.Length));
        else if (signatureCheck(rawData, CODE_TALKER_P2P_SIGNATURE))
            HandleP2PMessage(senderID, b.Slice(CODE_TALKER_P2P_SIGNATURE.Length, ret - CODE_TALKER_P2P_SIGNATURE.Length));
    }

    internal static void OnSteamSessionRequest(SteamNetworkingMessagesSessionRequest_t request) {
        if (dbg)
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
    static object packet;
    static Type type;

    internal static void ExecuteHandler(Delegate listener, CSteamID senderID, object packetObj) {
        try {
            listener.DynamicInvoke(new PacketHeader(senderID.m_SteamID), packetObj);
        } catch (Exception ex) {
            var plugins = Chainloader.PluginInfos;
            var mod = plugins.Values.Where(mod => mod.Instance?.GetType().Assembly == type.Assembly).FirstOrDefault();

            //Happy lil ternary
            string modName = mod != null
              ? $"{mod.Metadata.Name} version {mod.Metadata.Version}"
              : type.Assembly.GetName().Name;

            //Big beefin' raw string literal with interpolation
            CodeTalkerPlugin.Log.LogError($"""
The following mod encountered an error while responding to a network packet, please do not report this as a CodeTalker error!
Mod: {modName}
StackTrace:
{ex}
""");
        }
    }

    static PacketWrapper? wrapper;
    internal static void HandleJSONMessage(CSteamID senderID, Span<byte> rawData) {
        //We do it this way to make sure we're not blamed for errors
        //that other networked mods may cause

        string data = string.Empty;
        try {
            data = Encoding.UTF8.GetString(rawData);
            if (JsonConvert.DeserializeObject<PacketWrapper>(data, PacketSerializer.JSONOptions) is PacketWrapper inWrapper)
                wrapper = inWrapper;
            else
                throw new InvalidOperationException("Failed to deserialize a valid packet wrapper");
        } catch (Exception ex) {
            string aData;

            if (data.Length < 24)
                aData = data;
            else
                aData = data[..24];

            CodeTalkerPlugin.Log.LogError($"""
Error while receiving a packet!
Exception: {ex.GetType().Name}
Packet:
{aData}
""");
            return;
        }

        u64 typeHash = signatureHash(Encoding.UTF8.GetBytes(wrapper.PacketType));

        if (!packetListeners.TryGetValue(typeHash, out var listener)) {
            if (dbg && typeHash != lastSkippedPacketSig) {
                CodeTalkerPlugin.Log.LogDebug($"Skipping packet of type: {wrapper.PacketType} because this client does not have it installed, this is safe!");
                lastSkippedPacketSig = typeHash;
            }
            return;
        }

        try {
            if (packetDeserializers[typeHash](wrapper.PacketPayload) is PacketBase inPacket) {
                type = inPacket.GetType();
                packet = inPacket;
            } else
                return;
        } catch (Exception ex) {
            CodeTalkerPlugin.Log.LogError($"""
Error while unwrapping a packet!
Exception: {ex.GetType().Name}
Expected Type: {wrapper.PacketType}
""");
            return;
        }

        if (dbg) {
            CodeTalkerPlugin.Log.LogDebug($"Heard {rawData.Length} from GetLobbyChat. Sender {senderID}");
            CodeTalkerPlugin.Log.LogDebug($"Full message: {data}");
            CodeTalkerPlugin.Log.LogDebug($"Sending an event for type {wrapper.PacketType}");
        }

        ExecuteHandler(listener, senderID, packet);
    }

    static BinaryPacketWrapper? binWrapper;
    internal static void HandleBinaryMessage(CSteamID senderID, Span<byte> rawData) {
        try {
            binWrapper = new BinaryPacketWrapper(rawData);
        } catch (Exception ex) {
            CodeTalkerPlugin.Log.LogError($"Failed to create binary packet wrapper for valid packet!\nStackTrace: {ex}");
            return;
        }

        if (!binaryListeners.TryGetValue(binWrapper.PacketSignature, out var listenerEntry)) {
            if (dbg && (binWrapper.PacketSignature != lastSkippedPacketSig)) {
                CodeTalkerPlugin.Log.LogDebug($"Skipping binary packet of signature: {binWrapper.PacketSignature} because this client does not have it installed, this is safe!");
                lastSkippedPacketSig = binWrapper.PacketSignature;
            }
            return;
        }

        if (dbg) {
            CodeTalkerPlugin.Log.LogDebug($"Recieved binary packet!");
        }

        BinaryPacketBase bPacket;
        try {
            type = listenerEntry.PacketType;
            object instance = Activator.CreateInstance(type);
            if (instance is BinaryPacketBase i) {
                bPacket = i;
                try {
                    bPacket.Deserialize(binWrapper.FullPacketBytes);
                } catch (Exception ex) {
                    CodeTalkerPlugin.Log.LogError($"Error while deserializing binary packet! THIS IS NOT A CODETALKER ISSUE! DO NOT REPORT THIS TO THE CODETALKER DEV!!\nStackTrace: {ex}");
                    CodeTalkerPlugin.Log.LogError($"Packet signature: {i.PacketSignature}");
                    CodeTalkerPlugin.Log.LogError($"Message hex: {BinaryToHexString(rawData)}");
                    return;
                }
            } else {
                throw new InvalidOperationException($"Failed to create instance of binary packet type: {type}");
            }
        } catch (Exception ex) {
            CodeTalkerPlugin.Log.LogError($"Error while creating binary packet instance! This should be reported to either codetalker or the plugin dev!\nStackTrace: {ex}");
            CodeTalkerPlugin.Log.LogError($"Message hex: {BinaryToHexString(rawData)}");
            return;
        }

        if (dbg) {
            CodeTalkerPlugin.Log.LogDebug($"Heard {rawData.Length} from GetLobbyChat. Sender {senderID}");
            CodeTalkerPlugin.Log.LogDebug($"Message hex: {BinaryToHexString(rawData)}");
            CodeTalkerPlugin.Log.LogDebug($"Sending an event for binary handler \"{bPacket.PacketSignature}\"");
        }

        ExecuteHandler(listenerEntry.Listener, senderID, bPacket);
    }

    static P2PPacketWrapper? p2pWrapper;
    internal static void HandleP2PMessage(CSteamID senderID, Span<byte> rawData) {
        try {
            p2pWrapper = new P2PPacketWrapper(rawData);
        } catch (Exception ex) {
            CodeTalkerPlugin.Log.LogError($"Failed to create P2P packet wrapper for valid packet!\nStackTrace: {ex}");
            return;
        }

        if (dbg)
            CodeTalkerPlugin.Log.LogDebug($"Got P2P packet! {p2pWrapper}");

        void printWrapperInfo(Span<byte> rawData, P2PPacketWrapper wrapper, LogLevel level = LogLevel.Debug) {
            CodeTalkerPlugin.Log.Log(level, $"Heard {rawData.Length} from steam network. Sender: {senderID} Wrapper: {wrapper}");
            CodeTalkerPlugin.Log.Log(level, $"Message hex: {BinaryToHexString(rawData)}");
            if(wrapper.compression != CompressionType.None)
                CodeTalkerPlugin.Log.Log(level, $"Packet hex (decompressed): {BinaryToHexString(wrapper.PacketBytes)}");
        }

        if (p2pWrapper.TargetNetId > 0) {
            // Targeted P2P packet
            if (Player._mainPlayer == null || Player._mainPlayer.netId != p2pWrapper.TargetNetId) {
                if (dbg)
                    CodeTalkerPlugin.Log.LogDebug($"P2P netId doesn't match this client! Targeted netId: {p2pWrapper.TargetNetId}");
                return;
            }
        }

        if (p2pWrapper.PacketType == P2PPacketType.JSON) {
            // JSON P2P packet
            if (dbg) {
                CodeTalkerPlugin.Log.LogDebug($"Recieved P2P JSON packet!");
            }

            if (!packetListeners.TryGetValue(p2pWrapper.PacketSignature, out var listener)) {
                if (dbg && p2pWrapper.PacketSignature != lastSkippedPacketSig) {
                    CodeTalkerPlugin.Log.LogDebug($"Skipping packet of type: {p2pWrapper.PacketSignature} because this client does not have it installed, this is safe!");
                    lastSkippedPacketSig = p2pWrapper.PacketSignature;
                }
                return;
            }

            try {
                if (packetDeserializers[p2pWrapper.PacketSignature](Encoding.UTF8.GetString(p2pWrapper.PacketBytes)) is PacketBase inPacket) {
                    type = inPacket.GetType();
                    packet = inPacket;
                } else
                    return;
            } catch (Exception ex) {
                CodeTalkerPlugin.Log.LogError($"""
Error while unwrapping a packet!
Exception: {ex.GetType().Name}
Expected Type: {p2pWrapper.PacketSignature}
""");
                return;
            }

            if (dbg) {
                printWrapperInfo(rawData, p2pWrapper);
                CodeTalkerPlugin.Log.LogDebug($"Sending an event for {((PacketBase)packet).PacketSourceGUID}");
            }

            ExecuteHandler(listener, senderID, packet);
        }

        if (p2pWrapper.PacketType == P2PPacketType.Binary) {

            if (!binaryListeners.TryGetValue(p2pWrapper.PacketSignature, out var listenerEntry)) {
                if (dbg && (p2pWrapper.PacketSignature != lastSkippedPacketSig)) {
                    CodeTalkerPlugin.Log.LogDebug($"Skipping binary packet of unknown signature hash: 0x{p2pWrapper.PacketSignature.ToString("x2")} because this client does not have it installed, this is safe!");
                    lastSkippedPacketSig = p2pWrapper.PacketSignature;
                    return;
                }
            }

            if (dbg)
                CodeTalkerPlugin.Log.LogDebug($"Recieved P2P binary packet!");

            try {
                type = listenerEntry.PacketType;
                object instance = Activator.CreateInstance(type);
                if (instance is BinaryPacketBase bPacket) {
                    packet = instance;
                    try {
                        bPacket.Deserialize(p2pWrapper.PacketBytes);
                    } catch (Exception ex) {
                        CodeTalkerPlugin.Log.LogError($"Error while deserializing binary packet! THIS IS NOT A CODETALKER ISSUE! DO NOT REPORT THIS TO THE CODETALKER DEV!!\nStackTrace: {ex}");
                        printWrapperInfo(rawData, p2pWrapper, LogLevel.Error);
                        return;
                    }
                } else {
                    throw new InvalidOperationException("Failed to create instance of binary packet type!");
                }
            } catch (Exception ex) {
                CodeTalkerPlugin.Log.LogError($"Error while creating binary packet instance! This should be reported to either codetalker or the plugin dev!\nStackTrace: {ex}");
                printWrapperInfo(rawData, p2pWrapper, LogLevel.Error);
                return;
            }

            if (dbg) {
                printWrapperInfo(rawData, p2pWrapper);
                CodeTalkerPlugin.Log.LogDebug($"Sending an event for binary signature \"{((BinaryPacketBase)packet).PacketSignature}\"");
            }

            ExecuteHandler(listenerEntry.Listener, senderID, packet);
        }
    }
}
#endregion Message Handling