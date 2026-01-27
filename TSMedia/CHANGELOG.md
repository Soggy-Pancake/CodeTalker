# Changelog

## Version 2.2.2

- Forgot to not the ulong.tryparse >~<

## Version 2.2.1

> Hotfix for 2.2.0
- Fixed bug where binary packets that didnt have a registered listener could still attempt to be processed, resulting in a null reference.
- Check if player steam id is null or invalid before sending (mario's observe was sending too early)

## Version 2.2.0

- Removed specific json and binary packet wrappers for newer 'P2P' wrapper
	- **THIS UPDATE IS NOT BACKWARDS COMPATIBLE WITH OTHER VERSIONS OF CODEYAPPER**
	- P2P Wrapper is now just `PacketWrapper`
		- **All packets now support auto compression!** You no longer have to use p2p to be able to compress your packets automatically!
		- The old methods with no optionals have not been removed for ABI compatibility as some unmaintained but functional mods would still break!
		- Codeyapper now only uses 1 signature again.
			- Network version has also been increased
- Packet signatures are sent as a hash instead of the whole string! Usable packet size for broadcast packets is now fixed.
- Internal code has been refactored to use `Span` instead of arrays. *There is no visible change to mods.*
	- Significantly less code duplication and simplified control flow now too.
- Added `EnableDevMode` config option to log network traffic. `EnablePacketDebugging` now only logs packets that trigger their handler.
	- No more "recieved SNM" and such logs unless you enable dev mode

## Version 2.1.0

- Added support for P2P connections using SteamNetworkingMessages.
	- Requires specifying a player or steamId to send the packet to, otherwise it will still use the lobby chat.
	- JSON packets are't double serialized when using P2P and are slightly smaller.
		- It used to store everything in json, only really holding the packet type and actual packet json. Now it just stores the packet json and the packet type is part of the binary p2p header.
- Added compression helper methods. Packets aren't compressed by default! Options are:
	- Brotli (Default)
	- GZip
	- LZ4
	- ZStd
- Packets can be autocompressed by CodeYapper by passing a compression type when sending.
	- Only available for P2P packets for now (since it would break backwards compatibility).

## Version 2.0.1

- Marked `SendBinaryNetworkPacket` as obsolete in favor of `SendNetworkPacket` with an overload.
- Attempting to send a packet that would result in a buffer larger than 4096 bytes will log an error and fail to send.

## Version 2.0.0

- New author ig
- Update package info
	- new icon to differentiate from original
		- just a frame from rosedoMeow emote (artist is @roseDoodle, this is just a silly mod icon but figured ig at least gib credit/sauce)
	- Still uses the same mod GUID so it will replace existing installs during loading
- **BREAKING** changes to json packets
	- Type handling is now done by linking the packet type to the packet handler
		- Should fix a potential exploit (by Marioalexsan)
	- Existing code won't need to update, just the library
	- Updated packet version to 3
	- MetaInf removed from json packets
- Binary packet support!
	- New binary classes for sending and receiving binary data
		- You MUST deserialize and serialize binary packets yourself
			- This is for ADVANCED users only
		- Allows for *much* denser packets for the same info
	- New example files have been included
- Debugging improvements
	- Doesn't print packet if the packet is not registered
	- Doesn't print that the packet was ignored if the type matches the last ignored packet

## Version 1.2.0

- Adds a packet wrapper to graceful handle when the client does not have the same mods installed as the sender.
- Adds a MetaInf table to the packet wrapper for use later without breaking the packet version each time
- **BREAKING** Changes packet version to 2, clients on older versions will not be able to communicate with version 2 or higher clients.

## Version 1.1.4

- Adds a config file option for Packet Debugging. This is meant for developers to see all traffic and debug packet issues. It is `FALSE` by default. Please only enable if you need it, because it is likely slow

## Version 1.1.3

- Exception logging will ignore type load errors for the sake of debugging. This is a minor change aimed at developers

## Version 1.1.2

- Adds much smarter exception handling to help users understand where networking errors occur and what mod is to blame
- Lowers network buffer to 4kb from 10kb for better memory usage
