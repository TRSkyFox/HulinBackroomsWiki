# content／Wiki 內容來源

這個資料夾是**網頁唯一的資料來源**。  
`site/index.html` 是由這裡產生的成品，不要直接改 HTML 裡的資料。

> 不使用 `_data/*.md`、`頁面/*.md` 當來源；那些舊 md 沒有被收進這裡的內容，也不會被讀取。

---

## 目錄結構

```
content/
  characters/
    _meta.json                 ← 角色順序、永久能力、狀態表
    _skills.json               ← 戰鬥技能表
    白璃/
      character.json
    小猦/
      character.json
      design.css               ← 角色專屬樣式（可選）

  entities/
    竊皮者/
      entity.json

  items/
    武器/
      骨刃短刀/
        item.json
    裝備/
      防暴盾/
        item.json
    消耗品/
      item.json …

  floors/
    LX-盡力彌補/
      floor.json               ← 樓層資料、正文、配色
      events.json              ← 這一層的事件／岔路（唯一來源）
      endings.json             ← 這一層的結局（唯一來源）
      design.css               ← 這一層的專屬頁面設計（只作用於本層）
    197-打烊的量販店/
      floor.json
      events.json
      endings.json
      design.css

  shops/<商店>/shop.json
  pages/pages.json             ← 關於頁等靜態頁
  recipes/recipes.json
  enhancements/enhancements.json
  template.html                ← 外殼（字體、CSS、UI），一般不用改
  build.mjs                    ← 產生 site/index.html
  new.mjs                      ← 快速新增條目
```

---

## ⚠️ 樓層事件只能放該樓層資料夾

**硬性規則：**

```text
floors/<樓層資料夾>/events.json
```

- 每個樓層的事件、岔路、戰鬥觸發都只寫在自己的 `events.json`
- **不要再建立或使用 `content/events.json`** 全域事件檔
- `build.mjs` 只掃描各樓層的 `events.json` 來建立事件瀏覽器資料
- `event-import.mjs` 匯入時會依樓層 ID 寫回該樓層資料夾

---

## ⚠️ 樓層結局只能放該樓層資料夾

**硬性規則：**

```text
floors/<樓層資料夾>/endings.json
```

- 每個結局只寫在它對應樓層的 `endings.json`
- 多樓層共用的結局（例如「迷失」）要在每個對應樓層各放一份 variant
- **不要再建立或使用 `content/endings/` 全域結局資料夾**
- `build.mjs` 只掃描各樓層的 `endings.json` 來建立結局瀏覽器資料
- `category` 用 `bad`／`core`／`good`／`lost`／`extra`

---

## 修改後一定要重建

```powershell
node content/build.mjs
# 或雙擊／執行
powershell -ExecutionPolicy Bypass -File content/build.ps1
```

成功後會覆蓋 `site/index.html`；第一次重建前會保留一份 `site/index.backup-before-content.html`。

---

## 快速新增條目

```powershell
# 實體
node content/new.mjs entity 新實體名

# 角色（第二個參數是種族）
node content/new.mjs character 新角色 狐狸

# 物品（分類＋名稱＋價格）
node content/new.mjs item 武器 新武器 15

# 樓層（樓層 ID＋名稱）
node content/new.mjs floor 197 打烊的量販店
```

建立後編輯產生的 JSON，再跑 `node content/build.mjs`。

也可以把參數直接交給批次檔：

```powershell
content\新增條目.bat entity 新實體名
content\新增條目.bat character 新角色 狐狸
content\新增條目.bat item 武器 新武器 15
content\新增條目.bat floor 197 打烊的量販店
```

---

## 各類檔案怎麼填

### 角色 `character.json`

```json
{
  "n": "白璃",
  "title": "夢之方舟的狐巫女",
  "bio": "介紹…",
  "attrs": "力 0　敏 4…",
  "skills": ["技能…"],
  "items": ["自帶道具…"],
  "sp": "狐狸"
}
```

### 實體 `entity.json`

```json
{
  "n": "竊皮者",
  "boss": false,
  "skills": [{ "n": "撕抓", "eff": "效果…" }],
  "drops": ["菜刀", "急救包"],
  "desc": "介紹…",
  "resist": { "res": "鈍擊", "nor": "", "weak": "揮砍、火焰" }
}
```

- 不要再加 `floor`、`hp`、`atk`、`def`、`spd`、`variants`、`src`；這些不會顯示。
- BOSS 加 `"boss": true`，網頁會顯示名字後綴 `[BOSS]` 並優先排序。
- 掉落物可以是字串，或 `{ "n": "巨大鱗片", "e": "防禦+3…" }`。

### 物品 `item.json`

```json
{
  "i": "item-骨刃短刀",
  "n": "骨刃短刀",
  "c": "weapon",
  "sl": "單手",
  "p": "11",
  "x": "false",
  "eff": ["單手近戰 揮砍 1d6（敏捷）", "耐久 16"],
  "desc": "介紹…"
}
```

- `p` 是**售價**，只填遊戲顯示的參考價格；沒標價就留空。
- 不要再加 `d`、`st`、`q`、`s`、`tier`；這些不會顯示。
- **不要記「這一件／耐久 16/16」**這種當前實例耐久。
- 不要寫「實測」或任何測試數據。
- `c` 分類：`weapon`／`armor`／`accessory`／`tool`／`consumable`／`ammo`。

### 樓層 `floor.json`

```json
{
  "key": "LX|盡力彌補",
  "id": "LX",
  "name": "盡力彌補",
  "info": { "id": "LX", "name": "盡力彌補", "roman": "LX", "items": [], "desc": "" },
  "doc": "<h4>環境</h4><blockquote>…</blockquote>",
  "palette": { "bg": "#EDEBE6", "acc": "#6F6A5E" },
  "paletteKey": "60"
}
```

- `doc` 只放樓層頁真的會顯示的「環境」引言 HTML；舊事件紀錄、表格、`<code>`、未顯示段落一律不要放。
- 這一層的專屬設計寫在**同資料夾的 `design.css`**，重建時會一起塞進網頁。
- `design.css` 只作用於 `html[data-pal="…"]`，不要引用其他樓層。

---

## 規則

- **不要**把沒收進 Wiki 的舊 md 內容搬進來。
- **不要**把截圖放進資料夾；截圖只是轉錄素材，轉錄完就刪。
- 修改內容只改 JSON；`template.html` 只放外殼，不要把資料寫回去。
- 重建後再用瀏覽器開 `site/index.html` 驗證。
