# Binary Packets (Advanced)

Binary packets allow full control of your packet size and structure. This assumes you already know how to create and send packets using CodeYapper.

To create a binary packet, inherit from `BinaryPacketBase` instead of `PacketBase`. There will be 2 new methods you will be required to add. These will be the deserializarion and the serialization methods. These are called before your packet instance is forwarded to your packet listener.

You should now end up with something like this:
```C#
class BinaryPacket : BinaryPacketBase {

	int something;

	public BinaryPacket() { }

	public override void Deserialize(byte[] data) {
		/* Deserialization stuffs... */
	}

	public override byte[] Serialize(){
		/* Make ur byte array and shove stuff in, you can use MemoryStreams or whatever too */
	}
}
```


Remove any json attributes as we wont be needing those anymore (unless you are serializing your packet yourself in json for some reason ig). Then, add a public parameterless constructor (it can be empty). This is to ensure CodeYapper can create an instance of your packet (this is required and it will not compile if it is missing).

After testing that your packet serializes and deserialiszes correctly you are done! Enjoy your *hopefully* smaller packet size with better utilization of your network usage.

