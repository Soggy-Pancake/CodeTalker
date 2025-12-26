using System.IO;
using System.IO.Compression;
using ZstdSharp;
using K4os.Compression.LZ4;
using System;
using BepInEx.Logging;
using System.Diagnostics;
using CodeTalker.Packets;
using Newtonsoft.Json;

namespace CodeTalker;

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
                MemoryStream input = new MemoryStream(data);
                MemoryStream outStream = new MemoryStream();
                var brotliCompressor = new BrotliStream(outStream, level);
                input.CopyTo(brotliCompressor);
                brotliCompressor.Close();
                brotliCompressor.DisposeAsync();
                return outStream.ToArray();
            case CompressionType.ZStd:
                var compressor = new Compressor(level == CompressionLevel.Fastest ? 3 : 10); // 3 is default, 10 is a number I randomly picked
                return compressor.Wrap(data).ToArray();
            case CompressionType.GZip:
                input = new MemoryStream(data);
                outStream = new MemoryStream();
                var GZcompressor = new GZipStream(outStream, level);
                input.CopyTo(GZcompressor);
                GZcompressor.Close();
                return outStream.ToArray();
            case CompressionType.LZ4:
                return LZ4Pickler.Pickle(data, level == CompressionLevel.Fastest ? LZ4Level.L00_FAST : LZ4Level.L11_OPT);
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
                var brotliDecompressor = new BrotliStream(new MemoryStream(data), CompressionMode.Decompress);
                MemoryStream outStream = new MemoryStream();
                brotliDecompressor.CopyTo(outStream);
                return outStream.ToArray();
            case CompressionType.ZStd:
                var decompressor = new Decompressor();
                return decompressor.Unwrap(data).ToArray();
            case CompressionType.GZip:
                outStream = new MemoryStream();
                var GZdecompressor = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                GZdecompressor.CopyTo(outStream);
                return outStream.ToArray();
            case CompressionType.LZ4:
                return LZ4Pickler.Unpickle(data);
            default:
                CodeTalkerPlugin.Log.LogError($"[CodeTalker] Unknown decompression type: {type}");
                return data;
        }
    }

    /// <summary>
    /// Test compression ratios for different algorithms, this is only for testing purposes for mod developers
    /// </summary>
    /// <param name="rawBytes"></param>
    public static void CompressTest(byte[] rawBytes) {
        Stopwatch sw = new Stopwatch();
        CompressionLevel compressionLevel = CompressionLevel.Fastest;
        long nsPerTick = (1000000000) / Stopwatch.Frequency;
        // Dry run first to prevent library loading and initilization from influencing results
        foreach (CompressionType type in Enum.GetValues(typeof(CompressionType))) {
            if (type == CompressionType.None)
                continue;
            Compress(rawBytes, type, CompressionLevel.Fastest);
            Compress(rawBytes, type, CompressionLevel.Optimal); // Do optimal to prevent any potential caching or checks if its the same as last time
        }

        // Fastest then Optimal
        for (int i = 0; i < 2; i++) {
            CodeTalkerPlugin.Log.LogInfo($"{compressionLevel}:");
            foreach (CompressionType type in Enum.GetValues(typeof(CompressionType))) {
                if (type == CompressionType.None)
                    continue;
                sw.Start();
                byte[] compressed = Compress(rawBytes, type, compressionLevel);
                sw.Stop();

                CodeTalkerPlugin.Log.LogInfo($"{type}: {compressed.Length} bytes - ratio: {rawBytes.Length / (float)compressed.Length} - time to compress: {(sw.ElapsedTicks * nsPerTick) / 1000}us");
                sw.Reset();
            }
            compressionLevel = CompressionLevel.Optimal;
        }
    }

    /// <summary>
    /// Test compression ratios for different algorithms, this is only for testing purposes for mod developers
    /// </summary>
    /// <param name="packet"></param>
    public static void CompressTest(PacketBase packet) {
        string rawPacket = JsonConvert.SerializeObject(packet, PacketSerializer.JSONOptions);
        byte[] rawBytes = System.Text.Encoding.UTF8.GetBytes(rawPacket);
        CompressTest(rawBytes);
    }
}
