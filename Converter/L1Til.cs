// 轉發到 Lin.Helper.Core.Tile.L1Til
// 保留此檔案以維持向後相容性

using System.Collections.Generic;
using CoreL1Til = Lin.Helper.Core.Tile.L1Til;

namespace L1MapViewer.Converter
{
    /// <summary>
    /// L1 TIL 檔案處理 (轉發到 Lin.Helper.Core.Tile.L1Til)
    /// </summary>
    public static class L1Til
    {
        // Enums - 轉發
        public enum CompressionType
        {
            None = CoreL1Til.CompressionType.None,
            Zlib = CoreL1Til.CompressionType.Zlib,
            Brotli = CoreL1Til.CompressionType.Brotli
        }

        public enum TileVersion
        {
            Classic = CoreL1Til.TileVersion.Classic,
            Remaster = CoreL1Til.TileVersion.Remaster,
            Hybrid = CoreL1Til.TileVersion.Hybrid,
            Unknown = CoreL1Til.TileVersion.Unknown
        }

        // TileBlocks wrapper
        public class TileBlocks
        {
            private readonly CoreL1Til.TileBlocks _inner;

            public TileBlocks(CoreL1Til.TileBlocks inner) => _inner = inner;

            public byte[] Get(int index) => _inner.Get(index);
            public int Count => _inner.Count;
            public int UniqueCount => _inner.UniqueCount;
            public IEnumerable<KeyValuePair<int, byte[]>> GetUniqueBlocks() => _inner.GetUniqueBlocks();
            public void SetBlockData(int offset, byte[] newData) => _inner.SetBlockData(offset, newData);
            public int[] GetOffsets() => _inner.GetOffsets();
            public List<byte[]> ToList() => _inner.ToList();
        }

        // BlockAnalysis wrapper
        public class BlockAnalysis
        {
            public byte Type { get; set; }
            public int Size { get; set; }
            public bool IsSimpleDiamond { get; set; }
            public int EstimatedTileSize { get; set; }
            public string Format { get; set; }
            public byte XOffset { get; set; }
            public byte YOffset { get; set; }
            public byte XxLen { get; set; }
            public byte YLen { get; set; }
            public int MaxX => XOffset + XxLen;
            public int MaxY => YOffset + YLen;

            internal static BlockAnalysis FromCore(CoreL1Til.BlockAnalysis core) => new BlockAnalysis
            {
                Type = core.Type,
                Size = core.Size,
                IsSimpleDiamond = core.IsSimpleDiamond,
                EstimatedTileSize = core.EstimatedTileSize,
                Format = core.Format,
                XOffset = core.XOffset,
                YOffset = core.YOffset,
                XxLen = core.XxLen,
                YLen = core.YLen
            };
        }

        // Methods - 轉發
        public static CompressionType DetectCompression(byte[] data)
            => (CompressionType)CoreL1Til.DetectCompression(data);

        public static byte[] Decompress(byte[] data)
            => CoreL1Til.Decompress(data);

        public static byte[] Decompress(byte[] data, CompressionType compressionType)
            => CoreL1Til.Decompress(data, (CoreL1Til.CompressionType)compressionType);

        public static bool IsRemaster(byte[] tilData)
            => CoreL1Til.IsRemaster(tilData);

        public static TileVersion GetVersion(byte[] tilData)
            => (TileVersion)CoreL1Til.GetVersion(tilData);

        public static int GetTileSize(TileVersion version)
            => CoreL1Til.GetTileSize((CoreL1Til.TileVersion)version);

        public static int GetTileSize(byte[] tilData)
            => CoreL1Til.GetTileSize(tilData);

        public static List<byte[]> Parse(byte[] srcData)
            => CoreL1Til.Parse(srcData);

        public static TileBlocks ParseToTileBlocks(byte[] srcData)
        {
            var core = CoreL1Til.ParseToTileBlocks(srcData);
            return core != null ? new TileBlocks(core) : null;
        }

        public static byte[] BuildTil(List<byte[]> blocks)
            => CoreL1Til.BuildTil(blocks);

        public static byte[] BuildTilFromTileBlocks(TileBlocks tileBlocks)
            => CoreL1Til.BuildTilFromTileBlocks(
                new CoreL1Til.TileBlocks(tileBlocks.GetOffsets(),
                    new Dictionary<int, byte[]>(tileBlocks.GetUniqueBlocks())));

        public static byte[] DownscaleTil(byte[] tilData)
            => CoreL1Til.DownscaleTil(tilData);

        public static byte[] DownscaleBlock(byte[] blockData)
            => CoreL1Til.DownscaleBlock(blockData);

        public static BlockAnalysis AnalyzeBlock(byte[] blockData)
            => BlockAnalysis.FromCore(CoreL1Til.AnalyzeBlock(blockData));

        public static (int classic, int remaster, int hybrid, int unknown) AnalyzeTilBlocks(byte[] tilData)
            => CoreL1Til.AnalyzeTilBlocks(tilData);
    }
}
