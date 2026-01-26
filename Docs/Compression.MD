# Compression

CodeYapper added support for compression with the 2.1.0 update.

The current built in compression algorithms are:
- Brotli (default)
- LZ4
- ZStd
- GZip

All packets can have compression handled by CodeYapper as of `2.2.0`. In `2.1.0` only p2p packets could have compression automatically applied.
To tell codeyapper to automatically apply compression add the `CompressionType` to the arguments when you send a packet!
```C#
CodeTalkerNetwork.SendNetworkPacket(packet, CompressionType, CompressionLevel);
```
> Note: Both `CompressionType` and `CompressionLevel` are still optional! **Only specifying `CompressionLevel` will not result in compression!**

Compression can be applied yourself by using `Compressors.Compress` and `Compressors.Decompress` (binary only):
```C#
using CodeTalker.Compressors;

Compresssors.Compress(byte[] data, CompressionType type = CompressionType.Brotli, CompressionLevel = CompressionLevel.Fastest)
Compressors.Decompress(byte[] data, CompressionType type = CompressionType.Brotli);
```

Just remember to decompress using the same algorithm!

There is a provided compression testing algorithm that allows you to see the resulting compression ratios of each algorithm that CodeYapper supports, as well as the time in microseconds that it took to compress.
```C#
Compressors.CompressTest(byte[] yourData)
```
It will first test `CompressionLevel.Fastest`, then test `CompressionLevel.Optimal`. Results are printed to the console!

> Note: CompressTest has an overload that will allow testing of compression ratios for json packets too!
