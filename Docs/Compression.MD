# Compression

CodeYapper added support for compression with the 2.1.0 update.

The current built in compression algorithms are:
- Brotli (default)
- LZ4
- ZStd
- GZip


Compression can be applied yourself by using (binary packets only, unless you have a byte array in a json packet for some reason):
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
