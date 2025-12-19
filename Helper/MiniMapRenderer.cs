using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using L1FlyMapViewer;
using L1MapViewer.Converter;
using L1MapViewer.Models;
using L1MapViewer.Other;
using L1MapViewer.Reader;

namespace L1MapViewer.Helper
{
    /// <summary>
    /// MiniMap 渲染器 - CLI 和 Form 共用的渲染邏輯
    /// </summary>
    public class MiniMapRenderer
    {
        /// <summary>
        /// 渲染統計資訊
        /// </summary>
        public class RenderStats
        {
            public long TotalMs;
            public long GetBlockMs;
            public long DrawImageMs;
            public int BlockCount;
            public int ScaledWidth;
            public int ScaledHeight;
            public float Scale;
            public bool IsSimplified;
        }

        // S32 區塊快取
        private ConcurrentDictionary<string, Bitmap> _s32BlockCache = new ConcurrentDictionary<string, Bitmap>();

        // 取樣版 S32 區塊快取
        private ConcurrentDictionary<string, Bitmap> _s32BlockCacheSampled = new ConcurrentDictionary<string, Bitmap>();

        // Tile 檔案快取
        private ConcurrentDictionary<int, List<byte[]>> _tilFileCache = new ConcurrentDictionary<int, List<byte[]>>();

        // 常數
        public const int BlockWidth = 64 * 24 * 2;  // 3072
        public const int BlockHeight = 64 * 12 * 2; // 1536

        /// <summary>
        /// 渲染 MiniMap
        /// </summary>
        public Bitmap RenderMiniMap(
            int mapWidth,
            int mapHeight,
            int targetSize,
            Dictionary<string, S32Data> s32Files,
            HashSet<string> checkedFiles,
            out RenderStats stats)
        {
            stats = new RenderStats();
            var totalSw = Stopwatch.StartNew();

            // 計算縮放比例
            float scale = Math.Min((float)targetSize / mapWidth, (float)targetSize / mapHeight);
            int scaledWidth = (int)(mapWidth * scale);
            int scaledHeight = (int)(mapHeight * scale);

            stats.Scale = scale;
            stats.ScaledWidth = scaledWidth;
            stats.ScaledHeight = scaledHeight;

            // 決定渲染模式：超過 10 個 S32 時使用簡化渲染
            int s32Count = checkedFiles.Count;
            bool useSimplifiedRendering = s32Count > 10;
            stats.IsSimplified = useSimplifiedRendering;

            // 建立小地圖 Bitmap
            Bitmap miniBitmap = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format16bppRgb555);

            // 透明色設定
            ImageAttributes vAttr = new ImageAttributes();
            vAttr.SetColorKey(Color.FromArgb(0), Color.FromArgb(0));

            long totalGetBlockMs = 0;
            long totalDrawImageMs = 0;
            int blockCount = 0;

            using (Graphics g = Graphics.FromImage(miniBitmap))
            {
                // 使用最快的縮放模式
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                g.Clear(Color.Black);

                // 排序
                var sortedFilePaths = Utils.SortDesc(s32Files.Keys);

                if (useSimplifiedRendering)
                {
                    // 直接渲染到 mini map（不經過 full-size bitmap）
                    var getBlockSw = Stopwatch.StartNew();

                    // 收集所有需要渲染的區塊資訊
                    var blocksToRender = new List<(S32Data s32Data, int blockX, int blockY)>();
                    foreach (object filePathObj in sortedFilePaths)
                    {
                        string filePath = filePathObj as string;
                        if (filePath == null || !s32Files.ContainsKey(filePath)) continue;
                        if (!checkedFiles.Contains(filePath)) continue;

                        var s32Data = s32Files[filePath];
                        int[] loc = s32Data.SegInfo.GetLoc(1.0);
                        blocksToRender.Add((s32Data, loc[0], loc[1]));
                    }
                    blockCount = blocksToRender.Count;

                    // 平行處理：每個區塊計算自己貢獻的像素
                    var pixelData = new ConcurrentDictionary<(int x, int y), ushort>();

                    System.Threading.Tasks.Parallel.ForEach(blocksToRender, block =>
                    {
                        RenderBlockToMiniMapDirect(block.s32Data, block.blockX, block.blockY, scale, scaledWidth, scaledHeight, pixelData);
                    });
                    getBlockSw.Stop();
                    totalGetBlockMs = getBlockSw.ElapsedMilliseconds;

                    // 寫入 bitmap
                    var drawSw = Stopwatch.StartNew();
                    Rectangle rect = new Rectangle(0, 0, scaledWidth, scaledHeight);
                    BitmapData bmpData = miniBitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format16bppRgb555);
                    unsafe
                    {
                        byte* ptr = (byte*)bmpData.Scan0;
                        int stride = bmpData.Stride;
                        foreach (var kvp in pixelData)
                        {
                            int x = kvp.Key.x;
                            int y = kvp.Key.y;
                            if (x >= 0 && x < scaledWidth && y >= 0 && y < scaledHeight)
                            {
                                ushort color = kvp.Value;
                                int offset = y * stride + x * 2;
                                *(ptr + offset) = (byte)(color & 0xFF);
                                *(ptr + offset + 1) = (byte)((color >> 8) & 0xFF);
                            }
                        }
                    }
                    miniBitmap.UnlockBits(bmpData);
                    drawSw.Stop();
                    totalDrawImageMs = drawSw.ElapsedMilliseconds;
                }
                else
                {
                    // 完整渲染
                    var blocksToRender = new List<(S32Data s32Data, int destX, int destY, int destW, int destH)>();
                    foreach (object filePathObj in sortedFilePaths)
                    {
                        string filePath = filePathObj as string;
                        if (filePath == null || !s32Files.ContainsKey(filePath)) continue;
                        if (!checkedFiles.Contains(filePath)) continue;

                        var s32Data = s32Files[filePath];
                        int[] loc = s32Data.SegInfo.GetLoc(1.0);
                        int blockX = loc[0];
                        int blockY = loc[1];

                        int destX = (int)(blockX * scale);
                        int destY = (int)(blockY * scale);
                        int destW = (int)(BlockWidth * scale);
                        int destH = (int)(BlockHeight * scale);

                        blocksToRender.Add((s32Data, destX, destY, destW, destH));
                    }

                    // 平行渲染
                    var getBlockSw = Stopwatch.StartNew();
                    var renderedBlocks = new ConcurrentBag<(Bitmap bmp, int destX, int destY, int destW, int destH)>();

                    System.Threading.Tasks.Parallel.ForEach(blocksToRender, block =>
                    {
                        Bitmap blockBmp = GetOrRenderS32Block(block.s32Data, true, false, true);
                        renderedBlocks.Add((blockBmp, block.destX, block.destY, block.destW, block.destH));
                    });
                    getBlockSw.Stop();
                    totalGetBlockMs = getBlockSw.ElapsedMilliseconds;
                    blockCount = blocksToRender.Count;

                    // 順序繪製
                    var drawSw = Stopwatch.StartNew();
                    var orderedBlocks = renderedBlocks.OrderBy(b => b.destY).ThenBy(b => b.destX).ToList();
                    foreach (var block in orderedBlocks)
                    {
                        g.DrawImage(block.bmp,
                            new Rectangle(block.destX, block.destY, block.destW, block.destH),
                            0, 0, block.bmp.Width, block.bmp.Height,
                            GraphicsUnit.Pixel);
                    }
                    drawSw.Stop();
                    totalDrawImageMs = drawSw.ElapsedMilliseconds;
                }
            }

            totalSw.Stop();
            stats.TotalMs = totalSw.ElapsedMilliseconds;
            stats.GetBlockMs = totalGetBlockMs;
            stats.DrawImageMs = totalDrawImageMs;
            stats.BlockCount = blockCount;

            return miniBitmap;
        }

        /// <summary>
        /// 直接渲染區塊到 mini map 像素（不經過 full-size bitmap）
        /// </summary>
        private void RenderBlockToMiniMapDirect(S32Data s32Data, int blockX, int blockY, float scale,
            int scaledWidth, int scaledHeight, ConcurrentDictionary<(int x, int y), ushort> pixelData)
        {
            // 計算這個區塊在 mini map 上的範圍
            int destX = (int)(blockX * scale);
            int destY = (int)(blockY * scale);
            int destW = Math.Max(1, (int)(BlockWidth * scale));
            int destH = Math.Max(1, (int)(BlockHeight * scale));

            // 計算每個 mini map 像素對應多少個 Layer1 格子
            float cellsPerPixelX = 128.0f / destW;
            float cellsPerPixelY = 64.0f / destH;

            // 對於每個目標像素，取樣多個格子並混合顏色
            for (int dy = 0; dy < destH; dy++)
            {
                int miniY = destY + dy;
                if (miniY < 0 || miniY >= scaledHeight) continue;

                for (int dx = 0; dx < destW; dx++)
                {
                    int miniX = destX + dx;
                    if (miniX < 0 || miniX >= scaledWidth) continue;

                    // 計算這個像素對應的 Layer1 格子範圍
                    int cellStartX = (int)(dx * cellsPerPixelX);
                    int cellEndX = (int)((dx + 1) * cellsPerPixelX);
                    int cellStartY = (int)(dy * cellsPerPixelY);
                    int cellEndY = (int)((dy + 1) * cellsPerPixelY);

                    // 確保至少取樣一個格子
                    if (cellEndX <= cellStartX) cellEndX = cellStartX + 1;
                    if (cellEndY <= cellStartY) cellEndY = cellStartY + 1;

                    // 限制範圍
                    cellEndX = Math.Min(cellEndX, 128);
                    cellEndY = Math.Min(cellEndY, 64);

                    // 收集這個範圍內的顏色並混合
                    int totalR = 0, totalG = 0, totalB = 0;
                    int colorCount = 0;

                    for (int cy = cellStartY; cy < cellEndY; cy++)
                    {
                        for (int cx = cellStartX; cx < cellEndX; cx++)
                        {
                            var cell = s32Data.Layer1[cy, cx];
                            if (cell != null && cell.TileId > 0)
                            {
                                ushort color = GetTileRepresentativeColor(cell.TileId, cell.IndexId);
                                if (color != 0)
                                {
                                    // RGB555 解碼
                                    int r = (color >> 10) & 0x1F;
                                    int g = (color >> 5) & 0x1F;
                                    int b = color & 0x1F;
                                    totalR += r;
                                    totalG += g;
                                    totalB += b;
                                    colorCount++;
                                }
                            }
                        }
                    }

                    if (colorCount > 0)
                    {
                        // 計算平均顏色
                        int avgR = totalR / colorCount;
                        int avgG = totalG / colorCount;
                        int avgB = totalB / colorCount;
                        ushort avgColor = (ushort)((avgR << 10) | (avgG << 5) | avgB);
                        pixelData[(miniX, miniY)] = avgColor;
                    }
                }
            }
        }

        /// <summary>
        /// 取得 tile 的代表色（快取）
        /// </summary>
        private ConcurrentDictionary<(int tileId, int indexId), ushort> _tileColorCache = new ConcurrentDictionary<(int, int), ushort>();

        private ushort GetTileRepresentativeColor(int tileId, int indexId)
        {
            var key = (tileId, indexId);
            if (_tileColorCache.TryGetValue(key, out ushort cached))
            {
                return cached;
            }

            ushort color = CalculateTileRepresentativeColor(tileId, indexId);
            _tileColorCache.TryAdd(key, color);
            return color;
        }

        private ushort CalculateTileRepresentativeColor(int tileId, int indexId)
        {
            try
            {
                List<byte[]> tilArray = _tilFileCache.GetOrAdd(tileId, _ =>
                {
                    string key = $"{tileId}.til";
                    byte[] data = L1PakReader.UnPack("Tile", key);
                    if (data == null) return null;
                    return L1Til.Parse(data);
                });

                if (tilArray == null || indexId >= tilArray.Count) return 0;
                byte[] tilData = tilArray[indexId];
                if (tilData == null || tilData.Length < 10) return 0;

                // 取中間的像素作為代表色
                // 跳過 type byte，讀取中間區域的顏色
                int offset = 1;
                byte type = tilData[0];

                if (type == 1 || type == 9 || type == 17 || type == 0 || type == 8 || type == 16)
                {
                    // 菱形 tile，取中間行（約第 12 行）的像素
                    // 計算到第 12 行的偏移
                    for (int row = 0; row < 11; row++)
                    {
                        int n = (row + 1) * 2;
                        offset += n * 2; // 每像素 2 bytes
                    }
                    // 現在在第 12 行，有 24 個像素，取中間
                    if (offset + 24 < tilData.Length)
                    {
                        return (ushort)(tilData[offset + 12] | (tilData[offset + 13] << 8));
                    }
                }
                else
                {
                    // 壓縮格式，取前面的像素
                    if (tilData.Length > 10)
                    {
                        offset = 5; // 跳過 header
                        // 找第一個像素
                        if (offset + 5 < tilData.Length)
                        {
                            int skipBytes = tilData[offset];
                            offset += 1 + skipBytes / 2;
                            if (offset + 2 < tilData.Length)
                            {
                                int len = tilData[offset];
                                offset++;
                                if (len > 0 && offset + 2 <= tilData.Length)
                                {
                                    return (ushort)(tilData[offset] | (tilData[offset + 1] << 8));
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略錯誤
            }
            return 0;
        }

        /// <summary>
        /// 取得或渲染 S32 區塊
        /// </summary>
        private Bitmap GetOrRenderS32Block(S32Data s32Data, bool showLayer1, bool showLayer2, bool showLayer4)
        {
            string cacheKey = s32Data.FilePath;
            if (_s32BlockCache.TryGetValue(cacheKey, out Bitmap cached))
            {
                return cached;
            }

            Bitmap rendered = RenderS32Block(s32Data, showLayer1, showLayer2, showLayer4);
            _s32BlockCache.TryAdd(cacheKey, rendered);
            return rendered;
        }

        /// <summary>
        /// 取得或渲染取樣版 S32 區塊
        /// </summary>
        private Bitmap GetOrRenderS32BlockSampled(S32Data s32Data, int sampleStep)
        {
            string cacheKey = $"{s32Data.FilePath}_s{sampleStep}";
            if (_s32BlockCacheSampled.TryGetValue(cacheKey, out Bitmap cached))
            {
                return cached;
            }

            Bitmap rendered = RenderS32BlockSampled(s32Data, sampleStep);
            _s32BlockCacheSampled.TryAdd(cacheKey, rendered);
            return rendered;
        }

        /// <summary>
        /// 渲染單個 S32 區塊
        /// </summary>
        private Bitmap RenderS32Block(S32Data s32Data, bool showLayer1, bool showLayer2, bool showLayer4)
        {
            Bitmap result = new Bitmap(BlockWidth, BlockHeight, PixelFormat.Format16bppRgb555);

            Rectangle rect = new Rectangle(0, 0, result.Width, result.Height);
            BitmapData bmpData = result.LockBits(rect, ImageLockMode.ReadWrite, result.PixelFormat);
            int rowpix = bmpData.Stride;

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;

                // 第一層（地板）
                if (showLayer1)
                {
                    for (int y = 0; y < 64; y++)
                    {
                        for (int x = 0; x < 128; x++)
                        {
                            var cell = s32Data.Layer1[y, x];
                            if (cell != null && cell.TileId > 0)
                            {
                                int baseX = 0;
                                int baseY = 63 * 12;
                                baseX -= 24 * (x / 2);
                                baseY -= 12 * (x / 2);

                                int pixelX = baseX + x * 24 + y * 24;
                                int pixelY = baseY + y * 12;

                                DrawTilToBufferDirect(pixelX, pixelY, cell.TileId, cell.IndexId, rowpix, ptr, BlockWidth, BlockHeight);
                            }
                        }
                    }
                }

                // 第二層
                if (showLayer2)
                {
                    foreach (var item in s32Data.Layer2)
                    {
                        if (item.TileId > 0)
                        {
                            int x = item.X;
                            int y = item.Y;

                            int baseX = 0;
                            int baseY = 63 * 12;
                            baseX -= 24 * (x / 2);
                            baseY -= 12 * (x / 2);

                            int pixelX = baseX + x * 24 + y * 24;
                            int pixelY = baseY + y * 12;

                            DrawTilToBufferDirect(pixelX, pixelY, item.TileId, item.IndexId, rowpix, ptr, BlockWidth, BlockHeight);
                        }
                    }
                }

                // 第四層（物件）
                if (showLayer4)
                {
                    var sortedObjects = s32Data.Layer4.OrderBy(o => o.Layer).ToList();

                    foreach (var obj in sortedObjects)
                    {
                        int baseX = 0;
                        int baseY = 63 * 12;
                        baseX -= 24 * (obj.X / 2);
                        baseY -= 12 * (obj.X / 2);

                        int pixelX = baseX + obj.X * 24 + obj.Y * 24;
                        int pixelY = baseY + obj.Y * 12;

                        DrawTilToBufferDirect(pixelX, pixelY, obj.TileId, obj.IndexId, rowpix, ptr, BlockWidth, BlockHeight);
                    }
                }
            }

            result.UnlockBits(bmpData);
            return result;
        }

        /// <summary>
        /// 渲染取樣版 S32 區塊
        /// </summary>
        private Bitmap RenderS32BlockSampled(S32Data s32Data, int sampleStep)
        {
            Bitmap result = new Bitmap(BlockWidth, BlockHeight, PixelFormat.Format16bppRgb555);

            Rectangle rect = new Rectangle(0, 0, result.Width, result.Height);
            BitmapData bmpData = result.LockBits(rect, ImageLockMode.ReadWrite, result.PixelFormat);
            int rowpix = bmpData.Stride;

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;

                // Layer1（地板）- 取樣並填滿
                for (int sy = 0; sy < 64; sy += sampleStep)
                {
                    for (int sx = 0; sx < 128; sx += sampleStep)
                    {
                        var cell = s32Data.Layer1[sy, sx];
                        if (cell == null || cell.TileId == 0) continue;

                        // 用取樣的 tile 填滿整個區域
                        for (int dy = 0; dy < sampleStep && sy + dy < 64; dy++)
                        {
                            for (int dx = 0; dx < sampleStep && sx + dx < 128; dx++)
                            {
                                int x = sx + dx;
                                int y = sy + dy;

                                int baseX = 0;
                                int baseY = 63 * 12;
                                baseX -= 24 * (x / 2);
                                baseY -= 12 * (x / 2);

                                int pixelX = baseX + x * 24 + y * 24;
                                int pixelY = baseY + y * 12;

                                DrawTilToBufferDirect(pixelX, pixelY, cell.TileId, cell.IndexId, rowpix, ptr, BlockWidth, BlockHeight);
                            }
                        }
                    }
                }

                // Layer4（物件）- 取樣
                var sortedObjects = s32Data.Layer4.OrderBy(o => o.Layer).ToList();
                foreach (var obj in sortedObjects)
                {
                    // 簡單取樣：只渲染符合取樣間隔的物件
                    if (obj.X % sampleStep != 0 || obj.Y % sampleStep != 0) continue;

                    int baseX = 0;
                    int baseY = 63 * 12;
                    baseX -= 24 * (obj.X / 2);
                    baseY -= 12 * (obj.X / 2);

                    int pixelX = baseX + obj.X * 24 + obj.Y * 24;
                    int pixelY = baseY + obj.Y * 12;

                    DrawTilToBufferDirect(pixelX, pixelY, obj.TileId, obj.IndexId, rowpix, ptr, BlockWidth, BlockHeight);
                }
            }

            result.UnlockBits(bmpData);
            return result;
        }

        /// <summary>
        /// 繪製 Tile 到緩衝區
        /// </summary>
        private unsafe void DrawTilToBufferDirect(int pixelX, int pixelY, int tileId, int indexId, int rowpix, byte* ptr, int maxWidth, int maxHeight)
        {
            try
            {
                List<byte[]> tilArray = _tilFileCache.GetOrAdd(tileId, _ =>
                {
                    string key = $"{tileId}.til";
                    byte[] data = L1PakReader.UnPack("Tile", key);
                    if (data == null) return null;
                    return L1Til.Parse(data);
                });

                if (tilArray == null || indexId >= tilArray.Count) return;
                byte[] tilData = tilArray[indexId];
                if (tilData == null) return;

                fixed (byte* til_ptr_fixed = tilData)
                {
                    byte* til_ptr = til_ptr_fixed;
                    byte type = *(til_ptr++);

                    if (type == 1 || type == 9 || type == 17)
                    {
                        for (int ty = 0; ty < 24; ty++)
                        {
                            int n = (ty <= 11) ? (ty + 1) * 2 : (23 - ty) * 2;
                            int tx = 0;
                            for (int p = 0; p < n; p++)
                            {
                                ushort color = (ushort)(*(til_ptr++) | (*(til_ptr++) << 8));
                                int startX = pixelX + tx;
                                int startY = pixelY + ty;
                                if (startX >= 0 && startX < maxWidth && startY >= 0 && startY < maxHeight)
                                {
                                    int v = startY * rowpix + (startX * 2);
                                    *(ptr + v) = (byte)(color & 0x00FF);
                                    *(ptr + v + 1) = (byte)((color & 0xFF00) >> 8);
                                }
                                tx++;
                            }
                        }
                    }
                    else if (type == 0 || type == 8 || type == 16)
                    {
                        for (int ty = 0; ty < 24; ty++)
                        {
                            int n = (ty <= 11) ? (ty + 1) * 2 : (23 - ty) * 2;
                            int tx = 24 - n;
                            for (int p = 0; p < n; p++)
                            {
                                ushort color = (ushort)(*(til_ptr++) | (*(til_ptr++) << 8));
                                int startX = pixelX + tx;
                                int startY = pixelY + ty;
                                if (startX >= 0 && startX < maxWidth && startY >= 0 && startY < maxHeight)
                                {
                                    int v = startY * rowpix + (startX * 2);
                                    *(ptr + v) = (byte)(color & 0x00FF);
                                    *(ptr + v + 1) = (byte)((color & 0xFF00) >> 8);
                                }
                                tx++;
                            }
                        }
                    }
                    else if (type == 34 || type == 35)
                    {
                        // 壓縮格式 - 需要與背景混合
                        byte x_offset = *(til_ptr++);
                        byte y_offset = *(til_ptr++);
                        byte xxLen = *(til_ptr++);
                        byte yLen = *(til_ptr++);

                        for (int ty = 0; ty < yLen; ty++)
                        {
                            int tx = x_offset;
                            byte xSegmentCount = *(til_ptr++);
                            for (int nx = 0; nx < xSegmentCount; nx++)
                            {
                                tx += *(til_ptr++) / 2;
                                int xLen = *(til_ptr++);
                                for (int p = 0; p < xLen; p++)
                                {
                                    ushort color = (ushort)(*(til_ptr++) | (*(til_ptr++) << 8));
                                    int startX = pixelX + tx;
                                    int startY = pixelY + ty + y_offset;
                                    if (startX >= 0 && startX < maxWidth && startY >= 0 && startY < maxHeight)
                                    {
                                        int v = startY * rowpix + (startX * 2);
                                        ushort colorB = (ushort)(*(ptr + v) | (*(ptr + v + 1) << 8));
                                        color = (ushort)(colorB + 0xffff - color);
                                        *(ptr + v) = (byte)(color & 0x00FF);
                                        *(ptr + v + 1) = (byte)((color & 0xFF00) >> 8);
                                    }
                                    tx++;
                                }
                            }
                        }
                    }
                    else
                    {
                        // 其他壓縮格式
                        byte x_offset = *(til_ptr++);
                        byte y_offset = *(til_ptr++);
                        byte xxLen = *(til_ptr++);
                        byte yLen = *(til_ptr++);

                        for (int ty = 0; ty < yLen; ty++)
                        {
                            int tx = x_offset;
                            byte xSegmentCount = *(til_ptr++);
                            for (int nx = 0; nx < xSegmentCount; nx++)
                            {
                                tx += *(til_ptr++) / 2;
                                int xLen = *(til_ptr++);
                                for (int p = 0; p < xLen; p++)
                                {
                                    ushort color = (ushort)(*(til_ptr++) | (*(til_ptr++) << 8));
                                    int startX = pixelX + tx;
                                    int startY = pixelY + ty + y_offset;
                                    if (startX >= 0 && startX < maxWidth && startY >= 0 && startY < maxHeight)
                                    {
                                        int v = startY * rowpix + (startX * 2);
                                        *(ptr + v) = (byte)(color & 0x00FF);
                                        *(ptr + v + 1) = (byte)((color & 0xFF00) >> 8);
                                    }
                                    tx++;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略錯誤
            }
        }

        /// <summary>
        /// 清除快取
        /// </summary>
        public void ClearCache()
        {
            foreach (var bmp in _s32BlockCache.Values)
            {
                bmp?.Dispose();
            }
            _s32BlockCache.Clear();

            foreach (var bmp in _s32BlockCacheSampled.Values)
            {
                bmp?.Dispose();
            }
            _s32BlockCacheSampled.Clear();

            _tilFileCache.Clear();
            _tileColorCache.Clear();
        }
    }
}
