using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using L1MapViewer.Other;
using NLog;

namespace L1MapViewer.Models
{
    /// <summary>
    /// 地圖文件 - 管理整張地圖的所有 S32 資料
    /// </summary>
    public class MapDocument
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// 當前地圖 ID
        /// </summary>
        public string MapId { get; set; }

        /// <summary>
        /// 地圖資訊
        /// </summary>
        public Struct.L1Map MapInfo { get; private set; }

        /// <summary>
        /// 所有 S32 資料 (FilePath -> S32Data)
        /// </summary>
        public Dictionary<string, S32Data> S32Files { get; private set; } = new Dictionary<string, S32Data>();

        /// <summary>
        /// S32 檔案顯示項目清單
        /// </summary>
        public List<S32FileItem> S32FileItems { get; private set; } = new List<S32FileItem>();

        /// <summary>
        /// 已勾選顯示的 S32 檔案路徑
        /// </summary>
        public HashSet<string> CheckedS32Files { get; private set; } = new HashSet<string>();

        /// <summary>
        /// 是否有未儲存的修改
        /// </summary>
        public bool HasUnsavedChanges => S32Files.Values.Any(s => s.IsModified);

        /// <summary>
        /// 取得已修改的 S32 檔案清單
        /// </summary>
        public IEnumerable<S32Data> ModifiedS32Files => S32Files.Values.Where(s => s.IsModified);

        /// <summary>
        /// 地圖像素寬度
        /// </summary>
        public int MapPixelWidth { get; private set; }

        /// <summary>
        /// 地圖像素高度
        /// </summary>
        public int MapPixelHeight { get; private set; }

        #region 地圖遊戲座標邊界

        /// <summary>
        /// 地圖遊戲座標最小 X
        /// </summary>
        public int MapMinGameX { get; private set; }

        /// <summary>
        /// 地圖遊戲座標最大 X
        /// </summary>
        public int MapMaxGameX { get; private set; }

        /// <summary>
        /// 地圖遊戲座標最小 Y
        /// </summary>
        public int MapMinGameY { get; private set; }

        /// <summary>
        /// 地圖遊戲座標最大 Y
        /// </summary>
        public int MapMaxGameY { get; private set; }

        /// <summary>
        /// 地圖遊戲座標寬度
        /// </summary>
        public int MapGameWidth => MapMaxGameX - MapMinGameX;

        /// <summary>
        /// 地圖遊戲座標高度
        /// </summary>
        public int MapGameHeight => MapMaxGameY - MapMinGameY;

        #endregion

        /// <summary>
        /// 每個區塊的像素寬度 (3072)
        /// </summary>
        public const int BlockPixelWidth = 64 * 24 * 2;

        /// <summary>
        /// 每個區塊的像素高度 (1536)
        /// </summary>
        public const int BlockPixelHeight = 64 * 12 * 2;

        /// <summary>
        /// 文件變更事件
        /// </summary>
        public event EventHandler DocumentChanged;

        /// <summary>
        /// S32 資料變更事件
        /// </summary>
        public event EventHandler<S32DataChangedEventArgs> S32DataChanged;

        /// <summary>
        /// 設定地圖像素尺寸（用於外部載入的情況）
        /// </summary>
        public void SetMapPixelSize(int width, int height)
        {
            MapPixelWidth = width;
            MapPixelHeight = height;
        }

        /// <summary>
        /// 載入地圖
        /// </summary>
        public bool Load(string mapId)
        {
            if (string.IsNullOrEmpty(mapId) || !Share.MapDataList.ContainsKey(mapId))
                return false;

            MapId = mapId;
            MapInfo = Share.MapDataList[mapId];

            // 計算地圖像素大小
            MapPixelWidth = MapInfo.nBlockCountX * BlockPixelWidth;
            MapPixelHeight = MapInfo.nBlockCountX * BlockPixelHeight / 2 + MapInfo.nBlockCountY * BlockPixelHeight / 2;

            // 清除現有資料
            S32Files.Clear();
            S32FileItems.Clear();
            CheckedS32Files.Clear();

            // 載入所有 S32 檔案
            LoadS32Files();

            DocumentChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// 載入 S32 檔案（平行解析）- 使用 MapInfo.FullFileNameList
        /// </summary>
        private void LoadS32Files()
        {
            if (MapInfo?.FullFileNameList == null || MapInfo.FullFileNameList.Count == 0)
                return;

            var totalSw = Stopwatch.StartNew();

            // 從 MapInfo.FullFileNameList 取得檔案清單（已包含 SegInfo）
            var fileList = MapInfo.FullFileNameList.ToList();
            _logger.Debug($"[LoadS32Files] Loading {fileList.Count} files from MapInfo.FullFileNameList");

            // 平行解析 S32/SEG 檔案
            var parsedFiles = new System.Collections.Concurrent.ConcurrentBag<(string filePath, S32Data s32Data, Struct.L1MapSeg segInfo)>();
            var parseSw = Stopwatch.StartNew();

            System.Threading.Tasks.Parallel.ForEach(fileList, kvp =>
            {
                string filePath = kvp.Key;
                Struct.L1MapSeg segInfo = kvp.Value;

                try
                {
                    if (!File.Exists(filePath))
                        return;

                    S32Data s32Data;
                    if (segInfo.isS32)
                    {
                        // 解析 .s32 檔案
                        s32Data = CLI.S32Parser.ParseFile(filePath);
                    }
                    else
                    {
                        // 解析 .seg 檔案
                        s32Data = CLI.SegParser.ParseFile(filePath);
                    }

                    if (s32Data != null)
                    {
                        s32Data.FilePath = filePath;
                        s32Data.SegInfo = segInfo;
                        s32Data.IsModified = false;

                        // 載入對應的 MarketRegion 檔案
                        LoadMarketRegion(s32Data, filePath);

                        parsedFiles.Add((filePath, s32Data, segInfo));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"[LoadS32Files] Failed to parse: {filePath}");
                }
            });
            parseSw.Stop();

            // 順序加入集合（確保執行緒安全）
            var collectSw = Stopwatch.StartNew();
            foreach (var (filePath, s32Data, segInfo) in parsedFiles)
            {
                S32Files[filePath] = s32Data;

                // 建立顯示項目
                string fileName = Path.GetFileName(filePath);
                string fileType = segInfo.isS32 ? "" : " [SEG]";
                string displayName = $"{fileName}{fileType} ({segInfo.nBlockX:X4},{segInfo.nBlockY:X4}) [{segInfo.nLinBeginX},{segInfo.nLinBeginY}~{segInfo.nLinEndX},{segInfo.nLinEndY}]";

                var fileItem = new S32FileItem
                {
                    FilePath = filePath,
                    DisplayName = displayName,
                    SegInfo = segInfo,
                    IsChecked = true
                };
                S32FileItems.Add(fileItem);
                CheckedS32Files.Add(filePath);
            }
            collectSw.Stop();

            // 計算遊戲座標邊界
            CalculateGameBounds();

            totalSw.Stop();

            _logger.Info($"[LoadS32Files] files={fileList.Count}, parse={parseSw.ElapsedMilliseconds}ms, collect={collectSw.ElapsedMilliseconds}ms, total={totalSw.ElapsedMilliseconds}ms");
            _logger.Debug($"[MapBounds] GameX: {MapMinGameX}~{MapMaxGameX}, GameY: {MapMinGameY}~{MapMaxGameY}");
        }

        /// <summary>
        /// 載入對應的 MarketRegion 檔案
        /// </summary>
        /// <param name="s32Data">S32 資料</param>
        /// <param name="filePath">S32 檔案路徑</param>
        public static void LoadMarketRegion(S32Data s32Data, string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string marketRegionPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"{fileName}.MarketRegion");

            if (File.Exists(marketRegionPath))
            {
                s32Data.MarketRegionFileExists = true;
                try
                {
                    s32Data.MarketRegion = Lin.Helper.Core.Map.L1MapMarketRegion.Load(marketRegionPath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"[LoadMarketRegion] Failed to load: {marketRegionPath}");
                }
            }
            else
            {
                s32Data.MarketRegionFileExists = false;
            }
        }

        /// <summary>
        /// 計算地圖遊戲座標邊界
        /// </summary>
        private void CalculateGameBounds()
        {
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

            foreach (var s32Data in S32Files.Values)
            {
                if (s32Data.SegInfo == null) continue;

                // SegInfo 包含遊戲座標範圍
                minX = Math.Min(minX, s32Data.SegInfo.nLinBeginX);
                maxX = Math.Max(maxX, s32Data.SegInfo.nLinEndX);
                minY = Math.Min(minY, s32Data.SegInfo.nLinBeginY);
                maxY = Math.Max(maxY, s32Data.SegInfo.nLinEndY);
            }

            if (minX != int.MaxValue)
            {
                MapMinGameX = minX;
                MapMaxGameX = maxX;
                MapMinGameY = minY;
                MapMaxGameY = maxY;
            }
            else
            {
                MapMinGameX = MapMaxGameX = 0;
                MapMinGameY = MapMaxGameY = 0;
            }
        }

        /// <summary>
        /// 重新載入地圖
        /// </summary>
        public void Reload()
        {
            if (!string.IsNullOrEmpty(MapId))
            {
                Load(MapId);
            }
        }

        /// <summary>
        /// 取得指定位置的 S32 資料
        /// </summary>
        public S32Data GetS32AtWorldPosition(int worldX, int worldY)
        {
            foreach (var s32Data in S32Files.Values)
            {
                int[] loc = s32Data.SegInfo.GetLoc(1.0);
                int mx = loc[0];
                int my = loc[1];

                if (worldX >= mx && worldX < mx + BlockPixelWidth &&
                    worldY >= my && worldY < my + BlockPixelHeight)
                {
                    return s32Data;
                }
            }
            return null;
        }

        /// <summary>
        /// 取得與指定區域相交的 S32 資料
        /// </summary>
        public IEnumerable<S32Data> GetS32IntersectingRect(int x, int y, int width, int height)
        {
            foreach (var s32Data in S32Files.Values)
            {
                if (!CheckedS32Files.Contains(s32Data.FilePath))
                    continue;

                int[] loc = s32Data.SegInfo.GetLoc(1.0);
                int mx = loc[0];
                int my = loc[1];

                // 檢查是否相交
                if (mx < x + width && mx + BlockPixelWidth > x &&
                    my < y + height && my + BlockPixelHeight > y)
                {
                    yield return s32Data;
                }
            }
        }

        /// <summary>
        /// 設置 S32 檔案勾選狀態
        /// </summary>
        public void SetS32Checked(string filePath, bool isChecked)
        {
            if (isChecked)
                CheckedS32Files.Add(filePath);
            else
                CheckedS32Files.Remove(filePath);

            var item = S32FileItems.FirstOrDefault(i => i.FilePath == filePath);
            if (item != null)
                item.IsChecked = isChecked;
        }

        /// <summary>
        /// 儲存所有已修改的 S32 檔案
        /// </summary>
        public int SaveAllModified()
        {
            int savedCount = 0;
            foreach (var s32Data in ModifiedS32Files.ToList())
            {
                if (SaveS32File(s32Data))
                    savedCount++;
            }
            return savedCount;
        }

        /// <summary>
        /// 儲存單一 S32 檔案
        /// </summary>
        public bool SaveS32File(S32Data s32Data)
        {
            if (s32Data == null || string.IsNullOrEmpty(s32Data.FilePath))
                return false;

            try
            {
                CLI.S32Writer.Write(s32Data, s32Data.FilePath);
                s32Data.IsModified = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 標記 S32 資料已變更
        /// </summary>
        public void MarkS32Modified(S32Data s32Data)
        {
            if (s32Data != null)
            {
                s32Data.IsModified = true;
                S32DataChanged?.Invoke(this, new S32DataChangedEventArgs(s32Data));
            }
        }

        /// <summary>
        /// 取得依照渲染順序排序的 S32 檔案路徑
        /// </summary>
        public IEnumerable<string> GetSortedS32FilePaths()
        {
            return Utils.SortDesc(S32Files.Keys).Cast<string>();
        }

        /// <summary>
        /// 卸載地圖
        /// </summary>
        public void Unload()
        {
            MapId = null;
            MapInfo = default;
            S32Files.Clear();
            S32FileItems.Clear();
            CheckedS32Files.Clear();
            MapPixelWidth = 0;
            MapPixelHeight = 0;

            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// S32 資料變更事件參數
    /// </summary>
    public class S32DataChangedEventArgs : EventArgs
    {
        public S32Data S32Data { get; }

        public S32DataChangedEventArgs(S32Data s32Data)
        {
            S32Data = s32Data;
        }
    }
}
