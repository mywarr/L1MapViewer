# TIL Block 規格文件

## TIL 檔案結構

```
+------------------+
| Block Count (4B) |  int32, little-endian
+------------------+
| Offset[0] (4B)   |  int32, 相對於資料區起始位置
| Offset[1] (4B)   |
| ...              |
| Offset[N-1] (4B) |
| End Offset (4B)  |  資料區結尾位置
+------------------+
| Block Data       |  實際 block 資料
| ...              |
+------------------+
```

- **Block Count**: 4 bytes, Block 總數 (通常為 256)
- **Offset Table**: (Block Count + 1) * 4 bytes
  - 每個 offset 指向該 block 資料相對於資料區的起始位置
  - 最後一個 offset 指向資料區結尾
  - 多個 block 可以指向相同 offset (共用資料)
- **Block Data**: 實際 block 資料，由 offset 計算大小

## Block Type 分類

### 正常 Block Types

| Type | 格式 | 說明 |
|------|------|------|
| 0 | SimpleDiamond | 24x24 無透明 |
| 1 | SimpleDiamond | 24x24 有透明 |
| 2 | Compressed | 24x24 壓縮格式 |
| 3 | Compressed | 24x24 壓縮格式 |
| 6 | Compressed | 24x24 壓縮格式 |
| 7 | Compressed | 24x24 壓縮格式 |
| 8 | SimpleDiamond | 24x24 變體 |
| 9 | SimpleDiamond | 24x24 變體 (有透明) |
| 16 | SimpleDiamond | 48x48 R版 無透明 |
| 17 | SimpleDiamond | 48x48 R版 有透明 |
| 18 | Compressed | 48x48 R版 壓縮格式 |
| 19 | Compressed | 48x48 R版 壓縮格式 |
| 22 | Compressed | 壓縮變體 |
| 23 | Compressed | 壓縮變體 |
| 34 | Compressed | 壓縮變體 (Type 2 + 32) |
| 35 | Compressed | 壓縮變體 (Type 3 + 32) |

### 異常 Block Types

除上述以外的 type 值視為異常，可能表示：
- 資料損壞
- 不正確的降級處理
- 未知的新格式

## Block 資料格式

### SimpleDiamond 格式 (Type 0, 1, 8, 9, 16, 17)

```
+----------+
| Type (1B)|
+----------+
| Pixels   |  每個 pixel 2 bytes (RGB565)
| ...      |
+----------+
```

- **24x24**: 約 625 bytes (2 * 12 * 13 * 2 + 1)，約 312 pixels
- **48x48**: 約 2401 bytes (2 * 24 * 25 * 2 + 1)，約 1200 pixels

菱形 pixel 排列 (以 24x24 為例):
```
Row 0:  2 pixels  (中心開始)
Row 1:  4 pixels
...
Row 11: 24 pixels (最寬處)
Row 12: 24 pixels
...
Row 23: 2 pixels
```

### Compressed 格式 (Type 2, 3, 6, 7, 18, 19, 34, 35)

```
+------------+
| Type (1B)  |
+------------+
| X_Offset   |  起始 X 座標
| Y_Offset   |  起始 Y 座標
| XxLen      |  X 方向長度
| YLen       |  Y 方向長度
+------------+
| RLE Data   |  壓縮的 pixel 資料
| ...        |
+------------+
```

座標範圍判斷版本：
- **24x24**: X_Offset + XxLen <= 24, Y_Offset + YLen <= 24
- **48x48 Remaster**: XxLen > 24 或 YLen > 24
- **48x48 Hybrid**: 座標 > 24 但尺寸 <= 24

## Tile 版本

| 版本 | Pixel Size | 說明 |
|------|------------|------|
| Classic | 24x24 | 舊版，所有 block 都使用 24x24 座標 |
| Remaster | 48x48 | R版，block 使用 48x48 像素 |
| Hybrid | 48x48 coord | 混合格式：48x48 座標系統，但像素尺寸 24x24 |

## 版本判斷邏輯

1. 解析所有 block 的 offset，計算最大 block size
2. 如果 max block size >= 1800 bytes → Remaster
3. 如果 max block size 在 10-1000 bytes → Classic
4. 否則解析 blocks 檢查：
   - 有任何 block 的 pixel count >= 1000 → Remaster
   - 有座標 > 24 但尺寸 <= 24 → Hybrid
   - 全部座標和尺寸 <= 24 → Classic

## 實際案例

### 正常 Classic Tile (4702R.til 降級後)
```
Block 數量: 256
空 Block: 107
Types: 2(55), 3(57), 6(17), 7(20)
```

### 損壞 Tile (4702E.til)
```
Block 數量: 256
空 Block: 1
異常 Types: 4, 10, 11, 36, 37, 39, 41, 42, 45, 49, 53, 57, 61, 65...
共 48 種異常 type
```

## CLI 檢查工具

```bash
# 檢查單一 til 檔案的 block type
L1MapViewerCore.exe -cli til-info <til檔案>

# 掃描整個 Tile.idx 的異常 tile
L1MapViewerCore.exe -cli scan-tiles <客戶端路徑>

# 掃描目錄中的 til 檔案
L1MapViewerCore.exe -cli scan-tiles <til目錄>
```

## 參考

- 程式碼: `Converter/L1Til.cs`
- SimpleDiamond Types: `{ 0, 1, 8, 9, 16, 17 }`
- Normal Types: `{ 0, 1, 2, 3, 6, 7, 8, 9, 16, 17, 18, 19, 22, 23, 34, 35 }`
