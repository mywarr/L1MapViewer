using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using L1MapViewer.Helper;
using L1MapViewer.Models;

namespace L1MapViewer.CLI
{
    /// <summary>
    /// fs32 格式解析器 (ZIP 結構)
    /// </summary>
    public static class Fs32Parser
    {
        /// <summary>
        /// 從檔案讀取 fs32
        /// </summary>
        public static Fs32Data ParseFile(string filePath)
        {
            using (var zipArchive = ZipFile.OpenRead(filePath))
            {
                return Parse(zipArchive);
            }
        }

        /// <summary>
        /// 解析 fs32 ZIP 結構
        /// </summary>
        public static Fs32Data Parse(ZipArchive zipArchive)
        {
            var fs32 = new Fs32Data();

            // 1. 讀取 manifest.json
            var manifestEntry = zipArchive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                throw new InvalidDataException("Invalid fs32: missing manifest.json");
            }

            Fs32Manifest manifest;
            using (var stream = manifestEntry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                manifest = JsonSerializer.Deserialize<Fs32Manifest>(json);
            }

            fs32.Version = (ushort)manifest.Version;
            fs32.LayerFlags = (ushort)manifest.LayerFlags;
            fs32.Mode = (Fs32Mode)manifest.Mode;
            fs32.SourceMapId = manifest.SourceMapId ?? string.Empty;
            fs32.SelectionOriginX = manifest.SelectionOriginX;
            fs32.SelectionOriginY = manifest.SelectionOriginY;
            fs32.SelectionWidth = manifest.SelectionWidth;
            fs32.SelectionHeight = manifest.SelectionHeight;

            // 2. 讀取區塊
            foreach (string blockName in manifest.Blocks)
            {
                var blockEntry = zipArchive.GetEntry($"blocks/{blockName}.s32");
                if (blockEntry == null)
                    continue;

                // 從檔名解析 BlockX 和 BlockY (格式: 7fff8000)
                int blockX = 0, blockY = 0;
                if (blockName.Length == 8)
                {
                    blockX = Convert.ToInt32(blockName.Substring(0, 4), 16);
                    blockY = Convert.ToInt32(blockName.Substring(4, 4), 16);
                }

                byte[] s32Data;
                using (var stream = blockEntry.Open())
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    s32Data = ms.ToArray();
                }

                fs32.Blocks.Add(new Fs32Block
                {
                    BlockX = blockX,
                    BlockY = blockY,
                    S32Data = s32Data
                });
            }

            // 3. 讀取 MarketRegion (在 blocks/ 資料夾中)
            foreach (var entry in zipArchive.Entries)
            {
                if (entry.FullName.StartsWith("blocks/", StringComparison.OrdinalIgnoreCase) &&
                    entry.Name.EndsWith(".MarketRegion", StringComparison.OrdinalIgnoreCase))
                {
                    // 從檔名取得 block name (如 7fff8000)
                    string blockName = Path.GetFileNameWithoutExtension(entry.Name);

                    byte[] mrData;
                    using (var stream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        mrData = ms.ToArray();
                    }

                    if (mrData.Length > 0)
                    {
                        fs32.MarketRegions[blockName] = mrData;
                    }
                }
            }

            // 4. 讀取 Tile 索引
            var tileIndexEntry = zipArchive.GetEntry("tiles/index.json");
            if (tileIndexEntry != null)
            {
                // 有 index.json：使用索引讀取 tiles
                TileIndex tileIndex;
                using (var stream = tileIndexEntry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    tileIndex = JsonSerializer.Deserialize<TileIndex>(json);
                }

                // 讀取各個 Tile 檔案
                foreach (var kvp in tileIndex.Tiles)
                {
                    if (!int.TryParse(kvp.Key, out int tileId))
                        continue;

                    var tileEntry = zipArchive.GetEntry($"tiles/{tileId}.til");
                    if (tileEntry == null)
                        continue;

                    byte[] tilData;
                    using (var stream = tileEntry.Open())
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        tilData = ms.ToArray();
                    }

                    fs32.Tiles[tileId] = new TilePackageData
                    {
                        OriginalTileId = tileId,
                        Md5Hash = TileHashManager.HexToMd5(kvp.Value),
                        TilData = tilData
                    };
                }
            }
            else
            {
                // 沒有 index.json：掃描 tiles/ 目錄並自動計算 MD5
                foreach (var entry in zipArchive.Entries)
                {
                    if (!entry.FullName.StartsWith("tiles/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!entry.Name.EndsWith(".til", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 從檔名解析 tileId (例如 "12345.til")
                    string baseName = Path.GetFileNameWithoutExtension(entry.Name);
                    if (!int.TryParse(baseName, out int tileId))
                        continue;

                    byte[] tilData;
                    using (var stream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        tilData = ms.ToArray();
                    }

                    // 自動計算 MD5
                    byte[] md5Hash = TileHashManager.CalculateMd5(tilData);

                    fs32.Tiles[tileId] = new TilePackageData
                    {
                        OriginalTileId = tileId,
                        Md5Hash = md5Hash,
                        TilData = tilData
                    };
                }
            }

            // 5. 讀取 SPR 檔案
            // 收集所有 spr/file/ 和 spr/code/ 下的檔案
            var sprFileEntries = new Dictionary<int, Dictionary<string, ZipArchiveEntry>>();
            var sprCodeEntries = new Dictionary<int, ZipArchiveEntry>();

            foreach (var entry in zipArchive.Entries)
            {
                if (entry.FullName.StartsWith("spr/file/", StringComparison.OrdinalIgnoreCase) &&
                    entry.Name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
                {
                    // 解析 SprId: 可能是 "2197.spr" 或 "2197-0.spr"
                    string fileName = entry.Name;
                    int sprId = ParseSprIdFromFileName(fileName);
                    if (sprId > 0)
                    {
                        if (!sprFileEntries.ContainsKey(sprId))
                            sprFileEntries[sprId] = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                        sprFileEntries[sprId][fileName] = entry;
                    }
                }
                else if (entry.FullName.StartsWith("spr/code/", StringComparison.OrdinalIgnoreCase) &&
                         entry.Name.EndsWith(".sprtxt", StringComparison.OrdinalIgnoreCase))
                {
                    // 解析 SprId: "2197.sprtxt"
                    string baseName = Path.GetFileNameWithoutExtension(entry.Name);
                    if (int.TryParse(baseName, out int sprId))
                    {
                        sprCodeEntries[sprId] = entry;
                    }
                }
            }

            // 組合 SPR 資料
            var allSprIds = new HashSet<int>(sprFileEntries.Keys);
            foreach (var sprId in sprCodeEntries.Keys)
                allSprIds.Add(sprId);

            foreach (int sprId in allSprIds)
            {
                var sprData = new SprPackageData { SprId = sprId };

                // 讀取所有 SPR 檔案
                if (sprFileEntries.TryGetValue(sprId, out var fileDict))
                {
                    foreach (var kvp in fileDict)
                    {
                        using (var stream = kvp.Value.Open())
                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            sprData.Files[kvp.Key] = ms.ToArray();
                        }
                    }
                }

                // 讀取 CodeText
                if (sprCodeEntries.TryGetValue(sprId, out var codeEntry))
                {
                    using (var stream = codeEntry.Open())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        sprData.CodeText = reader.ReadToEnd();
                    }
                }

                fs32.Sprs[sprId] = sprData;
            }

            return fs32;
        }

        /// <summary>
        /// 從 SPR 檔名解析 SprId
        /// 支援格式: "2197.spr", "2197-0.spr", "2197-1.SPR" 等
        /// </summary>
        private static int ParseSprIdFromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return 0;

            // 移除 .spr 副檔名
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(baseName))
                return 0;

            // 檢查是否有 "-" (如 "2197-0")
            int dashIndex = baseName.IndexOf('-');
            if (dashIndex > 0)
            {
                baseName = baseName.Substring(0, dashIndex);
            }

            if (int.TryParse(baseName, out int sprId))
                return sprId;

            return 0;
        }

        /// <summary>
        /// 驗證檔案是否為有效的 fs32 格式 (ZIP)
        /// </summary>
        public static bool IsValidFs32File(string filePath)
        {
            try
            {
                using (var zipArchive = ZipFile.OpenRead(filePath))
                {
                    return zipArchive.GetEntry("manifest.json") != null;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取得 fs32 檔案資訊 (不載入完整資料)
        /// </summary>
        public static Fs32Info GetInfo(string filePath)
        {
            try
            {
                using (var zipArchive = ZipFile.OpenRead(filePath))
                {
                    var manifestEntry = zipArchive.GetEntry("manifest.json");
                    if (manifestEntry == null)
                        return null;

                    Fs32Manifest manifest;
                    using (var stream = manifestEntry.Open())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        manifest = JsonSerializer.Deserialize<Fs32Manifest>(json);
                    }

                    var info = new Fs32Info
                    {
                        Version = (ushort)manifest.Version,
                        LayerFlags = (ushort)manifest.LayerFlags,
                        Mode = (Fs32Mode)manifest.Mode,
                        SourceMapId = manifest.SourceMapId,
                        SelectionOriginX = manifest.SelectionOriginX,
                        SelectionOriginY = manifest.SelectionOriginY,
                        SelectionWidth = manifest.SelectionWidth,
                        SelectionHeight = manifest.SelectionHeight,
                        BlockCount = manifest.Blocks?.Count ?? 0,
                        FileSize = new FileInfo(filePath).Length
                    };

                    // 計算 Tile 數量
                    var tileIndexEntry = zipArchive.GetEntry("tiles/index.json");
                    if (tileIndexEntry != null)
                    {
                        // 有 index.json：從索引讀取數量
                        using (var stream = tileIndexEntry.Open())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string json = reader.ReadToEnd();
                            var tileIndex = JsonSerializer.Deserialize<TileIndex>(json);
                            info.TileCount = tileIndex?.Tiles?.Count ?? 0;
                        }
                    }
                    else
                    {
                        // 沒有 index.json：掃描 tiles/*.til 檔案數量
                        int tileCount = 0;
                        foreach (var entry in zipArchive.Entries)
                        {
                            if (entry.FullName.StartsWith("tiles/", StringComparison.OrdinalIgnoreCase) &&
                                entry.Name.EndsWith(".til", StringComparison.OrdinalIgnoreCase))
                            {
                                tileCount++;
                            }
                        }
                        info.TileCount = tileCount;
                    }

                    return info;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// fs32 檔案資訊 (輕量級)
    /// </summary>
    public class Fs32Info
    {
        public ushort Version { get; set; }
        public ushort LayerFlags { get; set; }
        public Fs32Mode Mode { get; set; }
        public string SourceMapId { get; set; }
        public int SelectionOriginX { get; set; }
        public int SelectionOriginY { get; set; }
        public int SelectionWidth { get; set; }
        public int SelectionHeight { get; set; }
        public int BlockCount { get; set; }
        public int TileCount { get; set; }
        public long FileSize { get; set; }
    }
}
