# Getting Started

CodeYapper is a networking library to allow mods to communicate with other clients running another instance of your mod.

## Adding CodeYapper to Your Projct

In your .csproj you can use either `<Reference>` or `<PackageReference>`. 

### Local Reference

This references a local install of CodeYapper. It is recommended that you set up a variable that points to your plugins folder to make referencing multiple plugins easier.

```
<ItemGroup>
	<PluginsDir>your/plugins/path</PluginsDir>
	<Reference>$(PluginsDir)/Soggy-Pancake-CodeYapper/CodeTalker.dll</Reference>
</ItemGroup>
```

### Using Nuget to use a Version from Thunderstore

This is typically used when using the mod template where Thunderstore has already been added to the Nuget sources in the repo.

```
<ItemGroup>
	<PackageReference Include="Soggy_Pancake-CodeYapper" Version="2.1.*" Private="False" />
</ItemGroup>
```

Both the minor and patch can be filled with `*` if you dont care about dealing with breaking changes from a new version immediately.

## Your first packet

Now that you have added CodeYapper to your project it's time to figure out what you need to send. The easiest packet type to make is a JSON packet.

First, you will want to create a class that will hold the data you want to send. There is an example in the examples folder if you want to look at a functional packet. This new class needs to inherit from `PacketBase` to be able to be sent. Next you need to add the `[JsonProperty]` attribute to every field of your class you want serialized. This allows serialization of your data to happen. If there are issues deserializing later and you can't figure out why, try adding a parameterless constructor (binary packets will require one if you plan on making any).

Before you attempt to send anything you first need to listen for your packet, otherwise it will just be ignored!

To create a packet listener create a method that accepts a `PacketHeader` and a `PacketBase`. You should end up with something like this:
```C#
void PacketListener(PacketHeader header, PacketBase p) { 
	if(p is YourPacket packet){ // This is really just in case, you could just cast if you wanted
		/* stuff... */
	}
}
```

> Note: Keep in mind that when broadcasting a packet to lobby chat you will recieve your own packets! **This does not apply to P2P or targeted packets!**

Then you need to register you listener using:
```C#
using CodeTalker.CodeTalkerNetwork;

// Usually in your mod's awake()
CodeTalkerNetwork.RegisterListener<YourPacket>(PacketListener);
```

Now you are ready to send packets!

## Sending Your First Packet

After you've registered your packet with CodeYapper you can now send a packet!

Sending a packet is done using:
```C#
using CodeTalker.CodeTalkerNetwork;
using CodeTaker.Compressors; // Only necessary for specifying compression type

...

// Several examples, pick any one of these based on your needs
CodeTalkerNetwork.SendNetworkPacket(new YourPacket()); // This broadcasts to everyone!
CodeTalkerNetwork.SendNetworkPacket(player, new YourPacket()); // This will be recieved by the specifc player you specified using p2p
CodeTalkerNetwork.SendNetworkPacket(player, new YourPacket(), CompressionType.Brotli); // Applies the Brotli compression algorithm to your packet when sending with p2p
```

**IMPORTANT: Broadcasted packets have a hard size limit of *4kb* (4096 bytes)!**
> Note: You can still use a constructor with parameters!

The packet should be recieved by both yourself and anyone else in the instance!

Q+A:

*What if I dont want to send it to everyone?*

Add the player object that you want to send the packet to as the first argument of the `SendNetworkPacket()` method! This will use the newly added P2P (Peer to Peer) communication. (SteamNetworkingMessages)

*My packet is too large!*

Try enabling compression when sending your packet and eliminate any duplication you can find.

*What compression algorithm is best?/Which should I use?*

Brotli does a fairly good job at compressing data efficently, however you should use the built in `CompressTest()` method that will print out the compression ratio and time taken in microseconds. You decide what performs best for your particular dataset and how much compute time you are willing to sacrifice to minimize packet size.

*Why use binary packets?*

Binary packets are useful when you want the most compact packet possible. Floating point numbers can be extremely wateful when converting to and from JSON. For example, the number '10.283647' needs 9 bytes to store a 32 bit float (4 bytes!) and this is excluding the extra labels and formatting of JSON that doesn't contribute to the actual data being sent. It does make it human readable, but sacrifices size for labeling and formatting. Depending on your packet layout this might be a significant portion of your bandwidth! Beware that binary packets are for advanced users and requires being comfortable with byte arrays and potentially  bitwise operations as well.
