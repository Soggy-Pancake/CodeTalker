# Changelog

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
