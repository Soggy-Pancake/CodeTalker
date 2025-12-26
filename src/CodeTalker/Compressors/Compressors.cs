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
    public enum CompressionType { // WARNING - CHANGING ORDER WILL BREAK COMPATIBILITY, ONLY APPEND
        /// <summary>
        /// No compression
        /// </summary>
        None,
        /// <summary>
        /// Brotli compression (default)
        /// </summary>
        Brotli,
        /// <summary>
        /// LZ4 compression
        /// </summary>
        LZ4,
        /// <summary>
        /// ZStandard compression
        /// </summary>
        ZStd,
        /// <summary>
        /// GZip compression
        /// </summary>
        GZip,
        
    }

    /// <summary>
    /// Compress data with specified compression type
    /// </summary>
    /// <param name="data"></param>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <returns></returns>
    public static byte[] Compress(byte[] data, CompressionType type = CompressionType.Brotli, CompressionLevel level = CompressionLevel.Fastest) {
        switch (type) {
            case CompressionType.Brotli:
                var brotliCompressor = new BrotliStream(new MemoryStream(data), level);
                MemoryStream brotliOutStream = new MemoryStream();
                brotliCompressor.CopyTo(brotliOutStream);
                return brotliOutStream.ToArray();
            case CompressionType.ZStd:
                var compressor = new Compressor();
                return compressor.Wrap(data).ToArray();
            case CompressionType.GZip:
                var GZcompressor = new GZipStream(new MemoryStream(data), level);
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
    /// <param name="level"></param>
    /// <returns></returns>
    public static byte[] Decompress(byte[] data, CompressionType type = CompressionType.Brotli, CompressionLevel level = CompressionLevel.Fastest) {
        switch (type) {
            case CompressionType.Brotli:
                var brotliDecompressor = new BrotliStream(new MemoryStream(data), level);
                MemoryStream brotliOutStream = new MemoryStream();
                brotliDecompressor.CopyTo(brotliOutStream);
                return brotliOutStream.ToArray();
            case CompressionType.ZStd:
                var decompressor = new Decompressor();
                return decompressor.Unwrap(data).ToArray();
            case CompressionType.GZip:
                var GZdecompressor = new GZipStream(new MemoryStream(data), level);
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
