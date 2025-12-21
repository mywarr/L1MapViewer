# fs32 / fs3p 格式與素材庫系統規劃

## 概述

實作兩種新格式用於地圖資料轉移：
- **fs32** - 地圖打包格式（S32 + Tiles）
- **fs3p** - 素材庫格式（可選 Layer + Tiles，跨地圖共享）

---

## 1. 格式規格

### 1.1 fs32 格式（地圖打包）- ZIP 結構

**用途**：
- 整張地圖備份
- 選取區塊匯出
- 選取區域匯出

**ZIP 結構**：
```
mappack.fs32 (ZIP 壓縮檔)
├── manifest.json           # 元資料
├── blocks/
│   ├── 7fff8000.s32       # S32 區塊檔案 (標準命名格式)
│   ├── 7fff8001.s32
│   └── ...
└── tiles/
    ├── 100.til            # Tile 檔案
    ├── 200.til
    ├── ...
    └── index.json         # Tile 索引 (id -> md5)
```

**manifest.json 結構**：
```json
{
  "Version": 2,
  "LayerFlags": 255,
  "Mode": 0,
  "SourceMapId": "79",
  "SelectionOriginX": 0,
  "SelectionOriginY": 0,
  "SelectionWidth": 0,
  "SelectionHeight": 0,
  "Blocks": ["7fff8000", "7fff8001"]
}
```

**tiles/index.json 結構**：
```json
{
  "Tiles": {
    "100": "a1b2c3d4e5f6...",
    "200": "f6e5d4c3b2a1..."
  }
}
```

### 1.2 fs3p 格式（素材庫）- ZIP 結構

**用途**：
- 跨地圖共享的素材
- 可重複使用的地圖模板
- 預製件 (Prefab) 管理

**ZIP 結構**：
```
material.fs3p (ZIP 壓縮檔)
├── metadata.json          # 元資料
├── thumbnail.png          # 縮圖 (可選)
├── layers/
│   ├── layer1.bin        # Layer1 二進位資料
│   ├── layer2.bin        # Layer2 二進位資料
│   ├── layer3.bin        # Layer3 二進位資料
│   └── layer4.bin        # Layer4 二進位資料
└── tiles/
    ├── 100.til           # Tile 檔案
    ├── 200.til
    ├── ...
    └── index.json        # Tile 索引 (id -> md5)
```

**metadata.json 結構**：
```json
{
  "Version": 2,
  "LayerFlags": 15,
  "Name": "草地模板",
  "OriginOffsetX": 0,
  "OriginOffsetY": 0,
  "Width": 10,
  "Height": 10,
  "CreatedTime": 1703145600,
  "ModifiedTime": 1703145600,
  "Tags": ["草地", "自然"]
}
```

**Layer 二進位格式** (layers/*.bin)：
```
[Layer1] 4B Count, [每項: 4B RelX, 4B RelY, 1B IndexId, 2B TileId, 1B Reserved]
[Layer2] 4B Count, [每項: 4B RelX, 4B RelY, 1B IndexId, 2B TileId, 1B UK]
[Layer3] 4B Count, [每項: 4B RelX, 4B RelY, 2B Attr1, 2B Attr2]
[Layer4] 4B Count, [每項: 4B RelX, 4B RelY, 4B GroupId, 1B Layer, 1B IndexId, 2B TileId]
```

---

## 2. Tile 對碰處理機制

### 2.1 處理流程

匯入 fs3p/fs32 時的 Tile 處理邏輯：

```
對於每個打包的 Tile (originalId, md5, tilData):

1. 檢查 Tile.pak 中是否有相同 originalId
   │
   ├─ 存在 originalId:
   │   │
   │   ├─ 計算現有 Tile 的 MD5
   │   │   ├─ MD5 一致 → 直接使用現有 Tile (IdMapping[originalId] = originalId)
   │   │   └─ MD5 不同 → 搜尋其他相同 MD5 的 Tile
   │   │       ├─ 找到 → 使用該 Tile ID (IdMapping[originalId] = existingId)
   │   │       └─ 沒找到 → 找新編號匯入 (IdMapping[originalId] = newId)
   │
   └─ 不存在 originalId:
       │
       ├─ 搜尋是否有相同 MD5 的其他 Tile
       │   ├─ 找到 → 使用該 Tile ID (IdMapping[originalId] = existingId)
       │   └─ 沒找到 → 匯入新 Tile
       │              ├─ 嘗試使用 originalId (如果可用)
       │              └─ 否則從 StartSearchId 開始找空位
```

### 2.2 MD5 搜尋策略 (FindTileByMd5)

```
1. 先檢查快取（快速路徑）
   └─ 命中 → 直接返回

2. 快取沒有 → 掃描整個 tile.idx
   ├─ 跳過已在快取中的 tile
   ├─ 讀取 tile 並計算 MD5
   ├─ 更新快取
   └─ 比對成功 → 返回

3. 掃描完還是沒有 → 返回 null（需要新增）
```

### 2.3 list.til 上限管理

`list.til` 儲存 Tile 載入上限值（如 9999），超過此值的 Tile 將無法顯示或導致遊戲閃退。

**相關方法**：
- `TileHashManager.GetTileLimit()` - 讀取上限值
- `TileHashManager.UpdateTileLimit(int)` - 更新上限值（使用 L1PakWriter.UpdateFile）
- `TileHashManager.CheckTileIdsOverLimit()` - 檢查 Tile ID 是否超過上限

**自動檢查**：
- 載入地圖時，`UpdateLayer5InvalidButton()` 會檢查 Tile.idx 中是否有超過上限的 Tile
- 如果有，三角形警告按鈕會顯示
- 點擊後可在 "Tile超上限" Tab 中查看詳情並擴充上限（最大值 + 5000）

### 2.4 設定項目

| 設定項 | 預設值 | 說明 |
|--------|--------|------|
| `TileSearchStartId` | 10000 | 找新編號時的起始位置 |
| `MaterialLibraryPath` | `Documents\L1MapViewer\Materials` | 素材庫存放路徑 |
| `MaxRecentMaterials` | 10 | 最近使用列表數量 |

---

## 3. 新增檔案清單

### 3.1 資料模型 (`Models/`)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Fs32Data.cs` | ✅ 完成 | fs32 資料結構 |
| `Fs3pData.cs` | ✅ 完成 | fs3p 資料結構 |
| `TileMappingResult.cs` | ✅ 完成 | Tile 對碰結果 |

### 3.2 解析器/寫入器 (`CLI/`)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Fs32Parser.cs` | ✅ 完成 | fs32 ZIP 解析 |
| `Fs32Writer.cs` | ✅ 完成 | fs32 ZIP 寫入 |
| `Fs3pParser.cs` | ✅ 完成 | fs3p ZIP 解析 |
| `Fs3pWriter.cs` | ✅ 完成 | fs3p ZIP 寫入 |
| `Commands/MaterialCommands.cs` | ✅ 完成 | 素材相關 CLI 命令 |

### 3.3 輔助類別 (`Helper/`)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `TileHashManager.cs` | ✅ 完成 | MD5 計算、快取、list.til 管理 |
| `TileImportManager.cs` | ✅ 完成 | Tile 對碰處理 |
| `MaterialLibrary.cs` | ✅ 完成 | 素材庫管理（索引、搜尋、最近使用持久化） |

### 3.4 UI 元件 (`Forms/`)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `ExportOptionsDialog.cs` | ✅ 完成 | 匯出選項對話框 |
| `MaterialBrowserForm.cs` | ✅ 完成 | 素材瀏覽器（含右鍵選單） |
| `SaveMaterialDialog.cs` | ✅ 完成 | 儲存素材對話框 |

### 3.5 Pak 讀寫 (`Reader/`)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `L1PakWriter.cs` | ✅ 更新 | 新增 `UpdateFile()` 方法（更新已存在的檔案） |
| `L1IdxReader.cs` | ✅ 更新 | 新增 `GetAll()` 方法 |

---

## 4. 修改現有檔案

| 檔案 | 狀態 | 修改內容 |
|------|------|----------|
| `MapForm.cs` | ✅ 完成 | 素材面板、右鍵選單、素材貼上預覽、Tile 上限檢查、啟動時載入最近素材 |
| `MapForm.Designer.cs` | ✅ 完成 | 新增素材面板控件 |
| `Models/EditState.cs` | ✅ 完成 | 素材預覽狀態 |
| `CLI/CliHandler.cs` | ✅ 完成 | 新增 list-til 命令 |

---

## 5. 已實作功能

### 5.1 核心格式
- [x] fs32 格式讀寫
- [x] fs3p 格式讀寫
- [x] Tile MD5 計算與快取
- [x] Tile 對碰處理（ID 衝突、MD5 比對）

### 5.2 Tile 管理
- [x] FindTileByMd5 - 先查快取，再掃描全部
- [x] list.til 上限讀取/更新
- [x] Tile 超上限檢查與警告
- [x] 匯入時 Tile 上限檢查與擴充提示

### 5.3 UI 功能
- [x] 右鍵選單「儲存為素材」
- [x] 素材面板（最近使用）
- [x] 素材瀏覽器（搜尋、預覽、右鍵選單）
- [x] 素材貼上預覽模式
- [x] 匯出選項對話框
- [x] 啟動時載入最近使用的素材
- [x] 最近使用素材持久化（存檔到 LocalAppData）

### 5.4 CLI 命令
- [x] `list-til` - 讀取 list.til 上限值
- [x] `verify-material-tiles` - 驗證素材 Tile MD5

### 5.5 異常檢查整合
- [x] UpdateLayer5InvalidButton 添加 Tile 超限檢查
- [x] 三角形按鈕 tooltip 顯示超限數量
- [x] 「Tile超上限」Tab 頁（顯示列表、擴充上限按鈕）

---

## 6. UI 設計

### 6.1 右側素材面板

```
┌─────────────────────────────┐  y=185
│  最近使用的素材              │  lblMaterials
├─────────────────────────────┤  y=210
│ ┌────┐ ┌────┐ ┌────┐ ┌────┐│
│ │縮圖│ │縮圖│ │縮圖│ │縮圖││  lvMaterials
│ │    │ │    │ │    │ │    ││  (LargeIcon view)
│ └────┘ └────┘ └────┘ └────┘│  210x95
├─────────────────────────────┤  y=308
│        [更多...]            │  btnMoreMaterials
└─────────────────────────────┘  y=330
```

### 6.2 素材瀏覽器右鍵選單

```
├─ 使用此素材
├─ ─────────────
├─ 刪除
├─ 開啟所在資料夾
├─ ─────────────
├─ 重新整理
```

### 6.3 Tile 超限 Tab

```
┌─ Tile超上限 (N) ────────────────────────────┐
│ Tile.idx 中有 N 個 Tile ID 超過 list.til    │
│ 上限 (9999)。最大 Tile ID: 12345。          │
│ 這些 Tile 將無法顯示或導致閃退：            │
├─────────────────────────────────────────────┤
│ Tile ID: 10001                              │
│ Tile ID: 10002                              │
│ Tile ID: 10003                              │
│ ...                                         │
├─────────────────────────────────────────────┤
│ 建議將上限擴充至: 17345                     │
│                                             │
│ [擴充上限至 17345]  [自訂上限...]           │
└─────────────────────────────────────────────┘
```

---

## 7. 關鍵依賴與參考

| 現有檔案 | 參考內容 |
|----------|----------|
| `CLI/S32Parser.cs` | S32 二進位解析邏輯 |
| `CLI/S32Writer.cs` | S32 二進位寫入邏輯 |
| `Reader/L1PakWriter.cs` | `AppendFiles()` 批次寫入、`UpdateFile()` 更新檔案 |
| `Reader/L1PakReader.cs` | `UnPack()` 讀取現有 Tile |
| `Reader/L1IdxReader.cs` | `Find()` 檢查 Tile 是否存在、`GetAll()` 取得所有記錄 |
| `Helper/ClipboardManager.cs` | 複製貼上邏輯參考 |
| `Models/S32DataModels.cs` | 現有 Layer 資料結構定義 |

---

## 8. 測試項目

### 8.1 格式測試
- [x] fs32 寫入/讀取往返測試
- [x] fs3p 寫入/讀取往返測試
- [x] 各種 LayerFlags 組合測試

### 8.2 Tile 對碰測試
- [x] MD5 一致 → 直接使用
- [x] MD5 不同 → 搜尋其他相同 MD5
- [x] 新 Tile → 匯入
- [x] 批次匯入多個 Tile

### 8.3 UI 測試
- [x] 選取區域後右鍵儲存素材
- [x] 素材面板顯示最近使用
- [x] 素材瀏覽器搜尋
- [x] 素材貼上預覽
- [x] 啟動時載入最近素材

### 8.4 Tile 上限測試
- [x] 讀取 list.til 上限
- [x] 更新 list.til 上限（UpdateFile）
- [x] 檢測超限 Tile 並警告
- [x] 擴充上限功能

---

## 9. 未來擴充

- 素材分類（資料夾結構）
- 素材版本管理
- 雲端素材庫同步
- 素材合併（多個 fs3p 合併為一個）
- 素材預覽 3D 視圖（如果未來支援）
