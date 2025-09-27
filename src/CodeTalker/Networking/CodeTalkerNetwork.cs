using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using CodeTalker.Packets;
using Newtonsoft.Json;
using Steamworks;

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
    private const ushort NETWORK_PACKET_VERSION = 2;

    internal static string CODE_TALKER_SIGNATURE = $"!!CODE_TALKER_NETWORKING:PV{NETWORK_PACKET_VERSION}!!";
    internal static string CODE_TALKER_BINARY_SIGNATURE = $"!CTN:BIN{NETWORK_PACKET_VERSION}!";
    private static readonly Dictionary<string, PacketListener> packetListeners = [];
    private static readonly Dictionary<string, Func<string, PacketBase>> packetDeserializers = [];

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
        var prop = type.GetProperty("PacketSignature", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? (MemberInfo)type.GetField("PacketSignature", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        string signature;

        if (prop is PropertyInfo pi)
            signature = (string)pi.GetValue(new T());
        else {
            CodeTalkerPlugin.Log.LogError($"Failed to register binary listenerEntry for type {type.FullName}, PacketSignature property not found!");
            return false;
        }

        if (binaryListeners.ContainsKey(signature))
            return false;

        int headerLen = Encoding.UTF8.GetBytes(signature).Length;

        if (signature.Length == 0 || signature.Length > 255) {
            CodeTalkerPlugin.Log.LogError($"Failed to register binary Listener for type {type.FullName}, PacketSignature can't be {(signature.Length > 255 ? "longer than 255 bytes" : "empty")}!");
            return false;
        }

        binaryListeners.Add(signature, new BinaryListenerEntry{ Listener = listener, PacketType = type});
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
        SteamMatchmaking.SendLobbyChatMsg(new(SteamLobby._current._currentLobbyID), bytes, bytes.Length);
    }

    /// <summary>
    /// Wraps and sends a binary packet to all clients on the Code Talker network
    /// </summary>
    /// <param name="packet"></param>
    public static void SendBinaryNetworkPacket(BinaryPacketBase packet) {
        byte[] serializedPacket = packet.Serialize();
        BinaryPacketWrapper wrapper = new(packet.PacketSignature, serializedPacket);

        SteamMatchmaking.SendLobbyChatMsg(new(SteamLobby._current._currentLobbyID), wrapper.FullPacketBytes, wrapper.FullPacketBytes.Length);
    }

    internal static void OnNetworkMessage(LobbyChatMsg_t message) {
        bool dbg = CodeTalkerPlugin.EnablePacketDebugging.Value;

        if (dbg)
            CodeTalkerPlugin.Log.LogDebug("Called back!");

        int bufferSize = 4096; //4kb buffer
        byte[] rawData = new byte[bufferSize];

        var ret = SteamMatchmaking.GetLobbyChatEntry(new(message.m_ulSteamIDLobby), (int)message.m_iChatID, out var senderID, rawData, bufferSize, out var messageType);
        string data = Encoding.UTF8.GetString(rawData[..ret]);

        if (!data.StartsWith(CODE_TALKER_SIGNATURE) && !data.StartsWith(CODE_TALKER_BINARY_SIGNATURE))
            return;

        PacketWrapper wrapper;
        PacketBase packet;
        Type inType;

        //We do it this way to make sure we're not blamed for errors
        //that other networked mods may cause

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
Abridged Packet:
{aData}
""");
                return;
            }


            if (!packetListeners.TryGetValue(wrapper.PacketType, out var listener)) {
                if (dbg)
                    CodeTalkerPlugin.Log.LogDebug($"Skipping packet of type: {wrapper.PacketType} because this client does not have it installed, this is safe!");

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
                CodeTalkerPlugin.Log.LogDebug($"Heard {ret} from GetLobbyChat. Sender {senderID}, type {messageType}");
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
            rawData = rawData[..ret];

            data = data.Replace(CODE_TALKER_BINARY_SIGNATURE, string.Empty);

            BinaryPacketWrapper binWrapper;
            try {
                binWrapper = new BinaryPacketWrapper(rawData[(CODE_TALKER_BINARY_SIGNATURE.Length)..]);
            } catch (Exception ex) {
                CodeTalkerPlugin.Log.LogError($"Failed to create binary packet wrapper for valid packet!\nStackTrace: {ex}");
                return;
            }

            if (!binaryListeners.TryGetValue(binWrapper.PacketSignature, out var listenerEntry)) {
                if (dbg)
                    CodeTalkerPlugin.Log.LogDebug($"Skipping binary packet of signature: {binWrapper.PacketSignature} because this client does not have it installed, this is safe!");
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
                    bPacket.Deserialize(binWrapper.FullPacketBytes);
                } else {
                    throw new InvalidOperationException("Failed to create instance of binary packet type!");
                }
            } catch (Exception ex) {
                CodeTalkerPlugin.Log.LogError($"Error while deserializing binary packet! THIS IS NOT A CODETALKER ISSUE! DO NOT REPORT THIS TO THE CODETALKER DEV!!\nStackTrace: {ex}");
                CodeTalkerPlugin.Log.LogError($"Full message: {Encoding.UTF8.GetString(rawData)}");
                CodeTalkerPlugin.Log.LogError($"Full message hex: {BitConverter.ToString(rawData).Replace("-", "")}");
                return;
            }

            if (dbg) {
                CodeTalkerPlugin.Log.LogDebug($"Heard {ret} from GetLobbyChat. Sender {senderID}, type {messageType}");
                CodeTalkerPlugin.Log.LogDebug($"Full message: {Encoding.UTF8.GetString(rawData)}");
                CodeTalkerPlugin.Log.LogDebug($"Full message hex: {BitConverter.ToString(rawData).Replace("-", "")}");
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
    }
}
