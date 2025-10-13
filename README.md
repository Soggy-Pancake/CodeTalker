# CodeYapper

**CodeYapper** is a lightweight C# networking library designed to simplify sending and receiving custom packets in Atlyss. It allows you to create your own packet types and register callbacks that are automatically invoked when matching packets are received.
- Fork of [CodeTalker](https://github.com/RobynLlama/CodeTalker) originaly made by Robyn.

**Special Thanks**: ButteredLilly for initial inspiration for the delivery method, thanks!

## Warning

Whenever a packet version update is applied you will be unable to communicate with clients not running the same version as you. If you are not seeing other user's packets then ensure you are running the latest version of CodeTalker!

## Features

- Define custom packet classes by inheriting from `PacketBase` or `BinaryPacketBase`.
- Easily register listeners for specific packet types.
- Automatically deserialize incoming messages and dispatch them to the appropriate listener.
- Packets are serialized into Steam lobby chat messages, avoiding the need for new RPCs that could disrupt multiplayer or clutter the in-game chat.

## How it works

### Json Packets
- Create your packet classes by inheriting from `PacketBase` or `BinaryPacketBase`.
- Register listeners for your packet types using `CodeTalkerNetwork.RegisterListener<T>()` or `CodeTalkerNetwork.RegisterBinaryListener<T>()`.
- Send packets using `CodeTalkerNetwork.SendNetworkPacket()`.
- Incoming messages are deserialized and routed automatically to the registered listeners.
- `PacketHeader` information is included with each payload, providing useful data such as the sender's identity and whether the sender is the host.

### Binary Packets
- Create your packet classes by inheriting from `BinaryPacketBase`.
- Register listeners for your packet types using `CodeTalkerNetwork.RegisterBinaryListener<T>()`.
- Send packets using `CodeTalkerNetwork.SendBinaryNetworkPacket()`.
- Incoming messages are serialized and deserialized using a custom function defined by the packet, then routed automatically to the registered listeners.
- `PacketHeader` information is included with each payload, providing useful data such as the sender's identity and whether the sender is the host.

---

For a full example, see the [example folder](https://github.com/Soggy-Pancake/CodeTalker/tree/main/src/CodeTalker/Examples) in the repo.
