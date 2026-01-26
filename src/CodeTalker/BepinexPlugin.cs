using System;
using System.IO.Compression;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CodeTalker.Networking;
using Steamworks;
using static CodeTalker.Compressors;
using static CodeTalker.Networking.CodeTalkerNetwork;

namespace CodeTalker;

/// <summary>
/// The main Code Talker entry point for BepInEx to load
/// </summary>
[BepInPlugin(LCMPluginInfo.PLUGIN_GUID, LCMPluginInfo.PLUGIN_NAME, LCMPluginInfo.PLUGIN_VERSION)]
public class CodeTalkerPlugin : BaseUnityPlugin {
    internal static ManualLogSource Log = null!;
    internal static Callback<LobbyChatMsg_t>? onNetworkMessage;
    internal static Callback<SteamNetworkingMessagesSessionRequest_t>? onSNMRequest;
    internal static ConfigEntry<bool> EnablePacketDebugging = null!;
    internal static ConfigEntry<bool> devMode = null!;
    static bool steamIntialized = false;

    private void Awake() {
        Log = Logger;
        EnablePacketDebugging = Config.Bind("Debugging", "EnablePacketDebugging", false, "If CodeTalker should dump packet information (this will be on the debug channel, make sure that is enabled in BepInEx.cfg)");
        devMode = Config.Bind("General", "EnableDevMode", false, "If CodeTalker should log extremely verbosely. This should only be used when actively debugging codetalker!");

        Log.LogInfo($"Plugin {LCMPluginInfo.PLUGIN_NAME} version {LCMPluginInfo.PLUGIN_VERSION} is loaded!");

        onNetworkMessage = Callback<LobbyChatMsg_t>.Create(CodeTalkerNetwork.OnNetworkMessage);
        onSNMRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(CodeTalkerNetwork.OnSteamSessionRequest);
        Callback<SteamRelayNetworkStatus_t>.Create(OnSteamNetworkInitialized);
        Log.LogMessage("Created steam networking callbacks");

        CodeTalkerNetwork.dbg = EnablePacketDebugging.Value;
        CodeTalkerNetwork.dev = devMode.Value;

        { // Preload compression libraries to prevent lag when joining servers
            byte[] data = new byte[64];
            new Random().NextBytes(data);
            foreach (CompressionType algo in Enum.GetValues(typeof(CompressionType))) {
                if (algo == CompressionType.None)
                    continue;

                var compressed = Compress(data, algo, CompressionLevel.Fastest);
                _ = Decompress(compressed, algo, CompressionLevel.Fastest);
            }
        }
    }

    void OnSteamNetworkInitialized(SteamRelayNetworkStatus_t status) {
        // Prevents some errors on startup hopefully and actually attempts to setup the network stuff before using it
        if (steamIntialized)
            return;

        steamIntialized = status.m_eAvail == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current;
        if (devMode.Value)
            Logger.LogDebug($"Steam Relay Network status update: {status.m_eAvail}");
        if (steamIntialized)
            SteamNetworkingUtils.InitRelayNetworkAccess();
    }

    static IntPtr[] messagePtrBuffer = new IntPtr[10]; // buffer of 10 messages ig
    private void Update() {
        // Polling is required to receive messages for SteamNetworkingMessages
        try {
            int messageCount = SteamNetworkingMessages.ReceiveMessagesOnChannel(0, messagePtrBuffer, messagePtrBuffer.Length);
            if (messageCount > 0) {
                try {
                    for (int i = 0; i < messageCount; i++) {
                        SteamNetworkingMessage_t msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(messagePtrBuffer[i]);
                        byte[] buffer = new byte[msg.m_cbSize];
                        if (devMode.Value)
                            Log.LogDebug($"Recieved SNM packet of size {msg.m_cbSize}");
                        try { // just to be safe and make absolutely sure it gets freed
                            Marshal.Copy(msg.m_pData, buffer, 0, (int)msg.m_cbSize);

                            Span<byte> b = new Span<byte>(buffer);
                            if(signatureCheck(buffer, CODE_TALKER_SIGNATURE)){
                                if (devMode.Value)
                                    Log.LogDebug($"Got P2P message! {BinaryToHexString(buffer)}");
                                HandleNetworkMessage(msg.m_identityPeer.GetSteamID(), b.Slice(CODE_TALKER_SIGNATURE.Length));
                            }
                        } catch { }
                        SteamNetworkingMessage_t.Release(messagePtrBuffer[i]);
                    }
                } catch (Exception e) {
                    Logger.LogError("Error handling SteamNetworkingMessages message! " + e);
                }
            }
        } catch { }
    }
}
