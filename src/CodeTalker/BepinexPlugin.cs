using System;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CodeTalker.Networking;
using Steamworks;

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
    static bool steamIntialized = false;

    private void Awake() {
        Log = Logger;
        EnablePacketDebugging = Config.Bind("Debugging", "EnablePacketDebugging", false, "If CodeTalker should dump packet information (this will be on the debug channel, make sure that is enabled in BepInEx.cfg)");

        Log.LogInfo($"Plugin {LCMPluginInfo.PLUGIN_NAME} version {LCMPluginInfo.PLUGIN_VERSION} is loaded!");

        onNetworkMessage = Callback<LobbyChatMsg_t>.Create(CodeTalkerNetwork.OnNetworkMessage);
        onSNMRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(CodeTalkerNetwork.OnSteamSessionRequest);
        Callback<SteamRelayNetworkStatus_t>.Create(OnSteamNetworkInitialized);
        Log.LogMessage("Created steam networking callbacks");

        CodeTalkerNetwork.dbg = EnablePacketDebugging.Value;
    }

    void OnSteamNetworkInitialized(SteamRelayNetworkStatus_t status) {
        // Prevents some errors on startup hopefully and actually attempts to setup the network stuff before using it
        if (steamIntialized)
            return;

        steamIntialized = status.m_eAvail == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current;
        if(EnablePacketDebugging.Value)
            Logger.LogDebug($"Steam Relay Network status update: {status.m_eAvail}");
        if(steamIntialized)
            SteamNetworkingUtils.InitRelayNetworkAccess();
    }

    private void Update() {
        // Polling is required to receive messages for SteamNetworkingMessages
        try {
            IntPtr[] messagePtrBuffer = new IntPtr[10]; // buffer of 10 messages ig
            int messageCount = SteamNetworkingMessages.ReceiveMessagesOnChannel(0, messagePtrBuffer, messagePtrBuffer.Length);
            if (messageCount > 0) {
                try {
                    for (int i = 0; i < messageCount; i++) {
                        SteamNetworkingMessage_t msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(messagePtrBuffer[i]);
                        byte[] buffer = new byte[msg.m_cbSize];
                        if(EnablePacketDebugging.Value)
                            Log.LogDebug($"Recieved SNM packet of size {msg.m_cbSize}");
                        try { // just to be safe and make absolutely sure it gets freed
                            Marshal.Copy(msg.m_pData, buffer, 0, (int)msg.m_cbSize);
                            CodeTalkerNetwork.HandleNetworkMessage(msg.m_identityPeer.GetSteamID(), buffer);
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
