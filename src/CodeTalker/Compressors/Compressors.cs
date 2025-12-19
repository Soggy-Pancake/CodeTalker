using System.IO;
using System.IO.Compression;
using ZstdSharp;
using K4os.Compression.LZ4;

namespace CodeTalker.Compressors;

/// <summary>
/// Provides helper methods for compressing and decompressing data
/// </summary>
public static class Compressors {

    /// <summary>
    /// Type of compression to use
    /// </summary>
    public enum CompressionType {
        /// <summary>
        /// ZStandard compression
        /// </summary>
        ZStd,
        /// <summary>
        /// GZip compression
        /// </summary>
        GZip,
        /// <summary>
        /// LZ4 compression (default)
        /// </summary>
        LZ4
    }

    /// <summary>
    /// Compress data with specified compression type
    /// </summary>
    /// <param name="data"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    public static byte[] Compress(byte[] data, CompressionType type = CompressionType.LZ4) {
        switch (type) {
            case CompressionType.ZStd:
                var compressor = new Compressor();
                return compressor.Wrap(data).ToArray();
            case CompressionType.GZip:
                var GZcompressor = new GZipStream(new MemoryStream(data), CompressionLevel.Fastest);
                MemoryStream outStream = new MemoryStream();
                GZcompressor.CopyTo(outStream);
                return outStream.ToArray();
            case CompressionType.LZ4:
                return LZ4Pickler.Pickle(data);
            default:
                CodeTalkerPlugin.Log.LogError($"[CodeTalker] Unknown compression type: {type}");
                return data;
        }
    }

    /// <summary>
    /// Decompress data with specified compression type
    /// </summary>
    /// <param name="data"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    public static byte[] Decompress(byte[] data, CompressionType type = CompressionType.LZ4) {
        switch (type) {
            case CompressionType.ZStd:
                var decompressor = new Decompressor();
                return decompressor.Unwrap(data).ToArray();
            case CompressionType.GZip:
                var GZdecompressor = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                MemoryStream outStream = new MemoryStream();
                GZdecompressor.CopyTo(outStream);
                return outStream.ToArray();
            case CompressionType.LZ4:
                return LZ4Pickler.Unpickle(data);
            default:
                CodeTalkerPlugin.Log.LogError($"[CodeTalker] Unknown decompression type: {type}");
                return data;
        }
    }
}
