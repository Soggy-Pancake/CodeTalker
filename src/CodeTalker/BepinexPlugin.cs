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
  private void Awake()
  {

    Log = Logger;
    EnablePacketDebugging = Config.Bind("Debugging", "EnablePacketDebugging", false, "If CodeTalker should dump packet information (this will be on the debug channel, make sure that is enabled in BepInEx.cfg)");

        Log.LogInfo($"Plugin {LCMPluginInfo.PLUGIN_NAME} version {LCMPluginInfo.PLUGIN_VERSION} is loaded!");

        onNetworkMessage = Callback<LobbyChatMsg_t>.Create(CodeTalkerNetwork.OnNetworkMessage);
        onSNMRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(CodeTalkerNetwork.OnSteamSessionRequest);
        Callback<SteamRelayNetworkStatus_t>.Create(OnSteamNetworkInitialized);
        Log.LogMessage("Created steam networking callbacks");
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
}
