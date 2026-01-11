using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using CodeTalker.Packets;
using Mirror;
using Newtonsoft.Json;
using Steamworks;
using static CodeTalker.Compressors;

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
    private const ushort NETWORK_PACKET_VERSION = 3;

    internal static string CODE_TALKER_SIGNATURE = $"!!CODE_TALKER_NETWORKING:PV{NETWORK_PACKET_VERSION}!!";
    internal static string CODE_TALKER_BINARY_SIGNATURE = $"!CTN:BIN{NETWORK_PACKET_VERSION}!";
    internal static string CODE_TALKER_P2P_SIGNATURE = $"!CTN:P2P{NETWORK_PACKET_VERSION}!";
    private static readonly Dictionary<string, PacketListener> packetListeners = [];
    private static readonly Dictionary<string, Func<string, PacketBase>> packetDeserializers = [];

    static string lastSkippedPacketType = "";

    struct BinaryListenerEntry {
        public BinaryPacketListener Listener;
        public Type PacketType;
    }
    private static readonly Dictionary<string, BinaryListenerEntry> binaryListeners = [];

    internal static string GetTypeNameString(Type type) =>
      $"{type.Assembly.GetName().Name},{type.DeclaringType?.Name ?? "NONE"}:{type.Namespace ?? "NONE"}.{type.Name}";

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

        if (packetListeners.ContainsKey(typeName)) {
            return false;
        }

        packetListeners.Add(typeName, listener);
        packetDeserializers.Add(typeName, (payload) => JsonConvert.DeserializeObject<T>(payload, PacketSerializer.JSONOptions)!);

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
        string signature = instance.PacketSignature;

        if (binaryListeners.ContainsKey(signature))
            return false;

        int headerLen = Encoding.UTF8.GetBytes(signature).Length;

        if (signature.Length == 0 || signature.Length > 255) {
            CodeTalkerPlugin.Log.LogError($"Failed to register binary Listener for type {type.FullName}, PacketSignature can't be {(signature.Length > 255 ? "longer than 255 bytes" : "empty")}!");
            return false;
        }

        binaryListeners.Add(signature, new BinaryListenerEntry { Listener = listener, PacketType = type });
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
        bool dbg = CodeTalkerPlugin.EnablePacketDebugging.Value;

        if (dbg)
            CodeTalkerPlugin.Log.LogDebug("Called back!");

        const int bufferSize = 4096; //4kb buffer
        byte[] rawData = new byte[bufferSize];

        var ret = SteamMatchmaking.GetLobbyChatEntry(new(message.m_ulSteamIDLobby), (int)message.m_iChatID, out var senderID, rawData, bufferSize, out var messageType);

        HandleNetworkMessage(senderID, rawData[..ret]);
    }

    internal static void OnSteamSessionRequest(SteamNetworkingMessagesSessionRequest_t request) {
        CodeTalkerPlugin.Log.LogDebug($"Accepting messages session request from {request.m_identityRemote.GetSteamID()}");
        SteamNetworkingMessages.AcceptSessionWithUser(ref request.m_identityRemote);
    }

    // Quick function to sanitize control characters from binary strings for logging
    internal static string BinaryToUtf8String(byte[] data) {
        return new string(Encoding.UTF8.GetString(data).Select(c => char.IsControl(c) && c != '\r' && c != '\n' ? '�' : c).ToArray());
    }

    internal static string BinaryToHexString(byte[] data) {
        return BitConverter.ToString(data).Replace("-", "");
    }

    internal static void HandleNetworkMessage(CSteamID senderID, byte[] rawData) {
        bool dbg = CodeTalkerPlugin.EnablePacketDebugging.Value;

        PacketWrapper wrapper;
        PacketBase packet;
        Type inType;

        //We do it this way to make sure we're not blamed for errors
        //that other networked mods may cause

        string data = Encoding.UTF8.GetString(rawData);
        if (!data.StartsWith(CODE_TALKER_SIGNATURE) &&
                !data.StartsWith(CODE_TALKER_BINARY_SIGNATURE) &&
                !data.StartsWith(CODE_TALKER_P2P_SIGNATURE))
            return;

        if (data.StartsWith(CODE_TALKER_SIGNATURE)) {
            data = data.Replace(CODE_TALKER_SIGNATURE, string.Empty);

            try {
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


            if (!packetListeners.TryGetValue(wrapper.PacketType, out var listener)) {
                if (dbg && wrapper.PacketType != lastSkippedPacketType) {
                    CodeTalkerPlugin.Log.LogDebug($"Skipping packet of type: {wrapper.PacketType} because this client does not have it installed, this is safe!");
                    lastSkippedPacketType = wrapper.PacketType;
                }
                return;
            }

            try {
                if (packetDeserializers[wrapper.PacketType](wrapper.PacketPayload) is PacketBase inPacket) {
                    inType = inPacket.GetType();
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

            try {
                listener.Invoke(new(senderID.m_SteamID), packet);
            } catch (Exception ex) {
                var plugins = Chainloader.PluginInfos;
                var mod = plugins.Values.Where(mod => mod.Instance?.GetType().Assembly == inType.Assembly).FirstOrDefault();

                //Happy lil ternary
                string modName = mod != null
                  ? $"{mod.Metadata.Name} version {mod.Metadata.Version}"
                  : inType.Assembly.GetName().Name;

                //Big beefin' raw string literal with interpolation
                CodeTalkerPlugin.Log.LogError($"""
The following mod encountered an error while responding to a network packet, please do not report this as a CodeTalker error!
Mod: {modName}
StackTrace:
{ex}
""");
            }
        }

        if (data.StartsWith(CODE_TALKER_BINARY_SIGNATURE)) {
            data = data.Replace(CODE_TALKER_BINARY_SIGNATURE, string.Empty);

            BinaryPacketWrapper binWrapper;
            try {
                binWrapper = new BinaryPacketWrapper(rawData[(CODE_TALKER_BINARY_SIGNATURE.Length)..]);
            } catch (Exception ex) {
                CodeTalkerPlugin.Log.LogError($"Failed to create binary packet wrapper for valid packet!\nStackTrace: {ex}");
                return;
            }

            if (!binaryListeners.TryGetValue(binWrapper.PacketSignature, out var listenerEntry)) {
                if (dbg && (binWrapper.PacketSignature != lastSkippedPacketType)) {
                    CodeTalkerPlugin.Log.LogDebug($"Skipping binary packet of signature: {binWrapper.PacketSignature} because this client does not have it installed, this is safe!");
                    lastSkippedPacketType = binWrapper.PacketSignature;
                }
                return;
            }

            if (dbg) {
                CodeTalkerPlugin.Log.LogDebug($"Recieved binary packet!");
            }

            BinaryPacketBase bPacket;
            try {
                inType = listenerEntry.PacketType;
                object instance = Activator.CreateInstance(inType);
                if (instance is BinaryPacketBase) {
                    bPacket = (BinaryPacketBase)instance;
                    try {
                        bPacket.Deserialize(binWrapper.FullPacketBytes);
                    } catch (Exception ex) {
                        CodeTalkerPlugin.Log.LogError($"Error while deserializing binary packet! THIS IS NOT A CODETALKER ISSUE! DO NOT REPORT THIS TO THE CODETALKER DEV!!\nStackTrace: {ex}");
                        CodeTalkerPlugin.Log.LogError($"Full message: {BinaryToUtf8String(rawData)}");
                        CodeTalkerPlugin.Log.LogError($"Full message hex: {BinaryToHexString(rawData)}");
                        return;
                    }
                } else {
                    throw new InvalidOperationException("Failed to create instance of binary packet type!");
                }
            } catch (Exception ex) {
                CodeTalkerPlugin.Log.LogError($"Error while creating binary packet instance! This should be reported to either codetalker or the plugin dev!\nStackTrace: {ex}");
                CodeTalkerPlugin.Log.LogError($"Full message: {BinaryToUtf8String(rawData)}");
                CodeTalkerPlugin.Log.LogError($"Full message hex: {BinaryToHexString(rawData)}");
                return;
            }

            if (dbg) {
                CodeTalkerPlugin.Log.LogDebug($"Heard {rawData.Length} from GetLobbyChat. Sender {senderID}");
                CodeTalkerPlugin.Log.LogDebug($"Full message: {BinaryToUtf8String(rawData)}");
                CodeTalkerPlugin.Log.LogDebug($"Full message hex: {BinaryToHexString(rawData)}");
                CodeTalkerPlugin.Log.LogDebug($"Sending an event for binary signature \"{binWrapper.PacketSignature}\"");
            }

            try {
                listenerEntry.Listener.Invoke(new(senderID.m_SteamID), bPacket);
            } catch (Exception ex) {
                var plugins = Chainloader.PluginInfos;
                inType = listenerEntry.GetType();
                var mod = plugins.Values.Where(mod => mod.Instance?.GetType().Assembly == inType.Assembly).FirstOrDefault();

                //Happy lil ternary
                string modName = mod != null
                  ? $"{mod.Metadata.Name} version {mod.Metadata.Version}"
                  : inType.Assembly.GetName().Name;

                //Big beefin' raw string literal with interpolation
                CodeTalkerPlugin.Log.LogError($"""
The following mod encountered an error while responding to a network packet, please do not report this as a CodeTalker error!
Mod: {modName}
StackTrace:
{ex}
""");
            }
        }

        if (data.StartsWith(CODE_TALKER_P2P_SIGNATURE)) {
            data = data.Replace(CODE_TALKER_P2P_SIGNATURE, string.Empty);
            // TODO: Reduce code duplication at some point, this could be used for all packets at some point

            void printWrapperInfo(P2PPacketWrapper wrapper, LogLevel level = LogLevel.Debug) {
                CodeTalkerPlugin.Log.Log(level, $"Heard {rawData.Length} from steam network. Sender: {senderID} Type: {wrapper.PacketType} Compression: {wrapper.compression}");
                CodeTalkerPlugin.Log.Log(level, $"Full message: {BinaryToUtf8String(rawData)}");
                CodeTalkerPlugin.Log.Log(level, $"Full message hex: {BinaryToHexString(rawData)}");
                CodeTalkerPlugin.Log.Log(level, $"Packet hex (decompressed): {BinaryToHexString(wrapper.PacketBytes)}");
            }

            P2PPacketWrapper p2pWrapper;
            try {
                p2pWrapper = new P2PPacketWrapper(rawData[(CODE_TALKER_P2P_SIGNATURE.Length)..]);
            } catch (Exception ex) {
                CodeTalkerPlugin.Log.LogError($"Failed to create P2P packet wrapper for valid packet!\nStackTrace: {ex}");
                return;
            }

            if (p2pWrapper.TargetNetId > 0) {
                // Targeted P2P packet
                if (Player._mainPlayer == null || Player._mainPlayer.netId != p2pWrapper.TargetNetId) {
                    if (dbg)
                        CodeTalkerPlugin.Log.LogDebug($"P2P netId doesn't match this client! Targeted netId: {p2pWrapper.TargetNetId}");
                    return;
                }
            }

            if (dbg)
                CodeTalkerPlugin.Log.LogDebug($"Got P2P packet! Type: {p2pWrapper.PacketType} TargetNetID: {p2pWrapper.TargetNetId}");

            if (p2pWrapper.PacketType == P2PPacketType.JSON) {
                // JSON P2P packet
                if (dbg) {
                    CodeTalkerPlugin.Log.LogDebug($"Recieved P2P JSON packet!");
                }

                if (!packetListeners.TryGetValue(p2pWrapper.PacketSignature, out var listener)) {
                    if (dbg && p2pWrapper.PacketSignature != lastSkippedPacketType) {
                        CodeTalkerPlugin.Log.LogDebug($"Skipping packet of type: {p2pWrapper.PacketSignature} because this client does not have it installed, this is safe!");
                        lastSkippedPacketType = p2pWrapper.PacketSignature;
                    }
                    return;
                }

                try {
                    if (packetDeserializers[p2pWrapper.PacketSignature](Encoding.UTF8.GetString(p2pWrapper.PacketBytes)) is PacketBase inPacket) {
                        inType = inPacket.GetType();
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
                    printWrapperInfo(p2pWrapper);
                    CodeTalkerPlugin.Log.LogDebug($"Sending an event for type {p2pWrapper.PacketSignature}");
                }

                try {
                    listener.Invoke(new(senderID.m_SteamID), packet);
                } catch (Exception ex) {
                    var plugins = Chainloader.PluginInfos;
                    var mod = plugins.Values.Where(mod => mod.Instance?.GetType().Assembly == inType.Assembly).FirstOrDefault();

                    //Happy lil ternary
                    string modName = mod != null
                      ? $"{mod.Metadata.Name} version {mod.Metadata.Version}"
                      : inType.Assembly.GetName().Name;

                    //Big beefin' raw string literal with interpolation
                    CodeTalkerPlugin.Log.LogError($"""
The following mod encountered an error while responding to a network packet, please do not report this as a CodeTalker error!
Mod: {modName}
StackTrace:
{ex}
""");
                }
            }

            if (p2pWrapper.PacketType == P2PPacketType.Binary) {
                data = data.Replace(CODE_TALKER_BINARY_SIGNATURE, string.Empty);

                // dont need binary wrapper we already parsed everything

                if (!binaryListeners.TryGetValue(p2pWrapper.PacketSignature, out var listenerEntry)) {
                    if (dbg && (p2pWrapper.PacketSignature != lastSkippedPacketType)) {
                        CodeTalkerPlugin.Log.LogDebug($"Skipping binary packet of signature: {p2pWrapper.PacketSignature} because this client does not have it installed, this is safe!");
                        lastSkippedPacketType = p2pWrapper.PacketSignature;
                    }
                    return;
                }

                if (dbg) {
                    CodeTalkerPlugin.Log.LogDebug($"Recieved P2P binary packet!");
                }

                BinaryPacketBase bPacket;
                try {
                    inType = listenerEntry.PacketType;
                    object instance = Activator.CreateInstance(inType);
                    if (instance is BinaryPacketBase) {
                        bPacket = (BinaryPacketBase)instance;
                        try {
                            bPacket.Deserialize(p2pWrapper.PacketBytes);
                        } catch (Exception ex) {
                            CodeTalkerPlugin.Log.LogError($"Error while deserializing binary packet! THIS IS NOT A CODETALKER ISSUE! DO NOT REPORT THIS TO THE CODETALKER DEV!!\nStackTrace: {ex}");
                            printWrapperInfo(p2pWrapper, LogLevel.Error);
                            return;
                        }
                    } else {
                        throw new InvalidOperationException("Failed to create instance of binary packet type!");
                    }
                } catch (Exception ex) {
                    CodeTalkerPlugin.Log.LogError($"Error while creating binary packet instance! This should be reported to either codetalker or the plugin dev!\nStackTrace: {ex}");
                    printWrapperInfo(p2pWrapper, LogLevel.Error);
                    return;
                }

                if (dbg) {
                    printWrapperInfo(p2pWrapper);
                    CodeTalkerPlugin.Log.LogDebug($"Sending an event for binary signature \"{p2pWrapper.PacketSignature}\"");
                }

                try {
                    listenerEntry.Listener.Invoke(new(senderID.m_SteamID), bPacket);
                } catch (Exception ex) {
                    var plugins = Chainloader.PluginInfos;
                    inType = listenerEntry.GetType();
                    var mod = plugins.Values.Where(mod => mod.Instance?.GetType().Assembly == inType.Assembly).FirstOrDefault();

                    //Happy lil ternary
                    string modName = mod != null
                      ? $"{mod.Metadata.Name} version {mod.Metadata.Version}"
                      : inType.Assembly.GetName().Name;

                    //Big beefin' raw string literal with interpolation
                    CodeTalkerPlugin.Log.LogError($"""
The following mod encountered an error while responding to a network packet, please do not report this as a CodeTalker error!
Mod: {modName}
StackTrace:
{ex}
""");
                }
            }
        }
    }
}
