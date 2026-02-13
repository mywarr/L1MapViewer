using System;
using System.Collections.Generic;
using System.IO;
using Lin.Helper.Core.Tile;
using NLog;

namespace L1MapViewer.Helper
{
    /// <summary>
    /// Tile 檔案完整性檢查器
    /// </summary>
    public static class TileIntegrityChecker
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Tile 檔案檢查結果
        /// </summary>
        public class TileCheckResult
        {
            public int TileId { get; set; }
            public bool IsValid { get; set; }
            public int FrameCount { get; set; }
            public int FileSize { get; set; }
            public int CorruptedFromFrame { get; set; } = -1;  // 從哪個 frame 開始損壞，-1 表示未損壞
            public string ErrorMessage { get; set; } = string.Empty;
            public List<int> CorruptedFrames { get; set; } = new List<int>();
        }

        /// <summary>
        /// 檢查 Tile 資料的完整性
        /// </summary>
        /// <param name="tileId">Tile ID</param>
        /// <param name="data">Tile 檔案資料</param>
        /// <returns>檢查結果</returns>
        public static TileCheckResult CheckTileData(int tileId, byte[] data)
        {
            var result = new TileCheckResult { TileId = tileId };

            if (data == null || data.Length < 4)
            {
                result.IsValid = false;
                result.ErrorMessage = "資料為空或過短";
                return result;
            }

            result.FileSize = data.Length;

            try
            {
                // 讀取 frame count（前 4 bytes）
                int frameCount = BitConverter.ToInt32(data, 0);
                result.FrameCount = frameCount;

                if (frameCount <= 0 || frameCount > 65536)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"無效的 frame 數量: {frameCount}";
                    return result;
                }

                // 計算 header 大小
                int headerSize = 4 + frameCount * 4;  // frame count + offset table

                if (headerSize > data.Length)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"檔案過小，無法容納 {frameCount} 個 frame 的偏移表";
                    return result;
                }

                // 讀取偏移表
                var offsets = new int[frameCount];
                for (int i = 0; i < frameCount; i++)
                {
                    offsets[i] = BitConverter.ToInt32(data, 4 + i * 4);
                }

                // 檢查偏移表完整性
                int corruptedFrom = -1;
                int previousValidDiff = -1;

                for (int i = 1; i < frameCount; i++)
                {
                    int diff = offsets[i] - offsets[i - 1];

                    // 偵測異常：連續多個 frame 偏移差值只有 1 byte
                    // 這通常表示檔案被截斷，後續 frame 沒有實際資料
                    if (diff == 1)
                    {
                        // 如果前面的差值正常（>10），現在突然變成 1，表示損壞開始
                        if (previousValidDiff > 10 && corruptedFrom == -1)
                        {
                            corruptedFrom = i;
                        }
                        result.CorruptedFrames.Add(i);
                    }
                    else if (diff > 1)
                    {
                        previousValidDiff = diff;
                    }

                    // 檢查偏移是否超出檔案範圍
                    int actualOffset = headerSize + offsets[i];
                    if (actualOffset >= data.Length)
                    {
                        if (corruptedFrom == -1)
                        {
                            corruptedFrom = i;
                        }
                        result.CorruptedFrames.Add(i);
                    }
                }

                result.CorruptedFromFrame = corruptedFrom;

                // 判斷是否有效
                // 如果有連續 5 個以上的 frame 差值為 1，視為損壞
                if (result.CorruptedFrames.Count >= 5)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"偏移表異常：從 frame {corruptedFrom} 開始，共 {result.CorruptedFrames.Count} 個 frame 可能損壞";
                }
                else if (result.CorruptedFrames.Count > 0)
                {
                    // 少量異常，可能是正常的空 frame
                    result.IsValid = true;
                }
                else
                {
                    result.IsValid = true;
                }

                // Block 層級驗證：檢查壓縮 block 的 scan data 完整性
                if (result.IsValid)
                {
                    try
                    {
                        L1Til.ParseToTileBlocks(data);
                    }
                    catch (InvalidDataException ex)
                    {
                        result.IsValid = false;
                        // 從英文訊息中提取數字，轉為中文說明
                        result.ErrorMessage = TranslateTilParseError(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TileIntegrityChecker] 檢查 Tile {tileId} 時發生錯誤");
                result.IsValid = false;
                result.ErrorMessage = $"檢查時發生錯誤: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 從 Pak 讀取並檢查 Tile
        /// </summary>
        /// <param name="tileId">Tile ID</param>
        /// <returns>檢查結果，如果無法讀取則返回 null</returns>
        public static TileCheckResult CheckTileFromPak(int tileId)
        {
            try
            {
                byte[] data = Reader.L1PakReader.UnPack("Tile", $"{tileId}.til");
                if (data == null || data.Length == 0)
                {
                    return new TileCheckResult
                    {
                        TileId = tileId,
                        IsValid = false,
                        ErrorMessage = "無法讀取 Tile 資料"
                    };
                }

                return CheckTileData(tileId, data);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TileIntegrityChecker] 讀取 Tile {tileId} 時發生錯誤");
                return new TileCheckResult
                {
                    TileId = tileId,
                    IsValid = false,
                    ErrorMessage = $"讀取時發生錯誤: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 批次檢查多個 Tile
        /// </summary>
        /// <param name="tileIds">要檢查的 Tile ID 列表</param>
        /// <param name="progressCallback">進度回呼 (當前, 總數)</param>
        /// <returns>損壞的 Tile 列表</returns>
        public static List<TileCheckResult> CheckMultipleTiles(
            IEnumerable<int> tileIds,
            Action<int, int> progressCallback = null)
        {
            var corruptedTiles = new List<TileCheckResult>();
            var tileIdList = new List<int>(tileIds);
            int total = tileIdList.Count;
            int current = 0;

            // 快取已檢查過的 Tile，避免重複檢查
            var checkedTiles = new Dictionary<int, TileCheckResult>();

            foreach (int tileId in tileIdList)
            {
                current++;
                progressCallback?.Invoke(current, total);

                if (tileId <= 0) continue;

                // 檢查快取
                if (checkedTiles.TryGetValue(tileId, out var cached))
                {
                    if (!cached.IsValid)
                    {
                        // 避免重複加入
                        if (!corruptedTiles.Exists(t => t.TileId == tileId))
                        {
                            corruptedTiles.Add(cached);
                        }
                    }
                    continue;
                }

                var result = CheckTileFromPak(tileId);
                checkedTiles[tileId] = result;

                if (!result.IsValid)
                {
                    corruptedTiles.Add(result);
                }
            }

            return corruptedTiles;
        }

        /// <summary>
        /// 將 ParseToTileBlocks 的英文錯誤訊息翻譯為中文
        /// </summary>
        private static string TranslateTilParseError(string englishMessage)
        {
            // "TIL format error: 255/256 blocks have truncated scan data. This may be caused by..."
            var match = System.Text.RegularExpressions.Regex.Match(
                englishMessage, @"(\d+)/(\d+) blocks have truncated scan data");
            if (match.Success)
            {
                return $"Block 資料不完整：{match.Groups[1].Value}/{match.Groups[2].Value} 個 block 的掃描資料被截斷";
            }
            return englishMessage;
        }
    }
}
