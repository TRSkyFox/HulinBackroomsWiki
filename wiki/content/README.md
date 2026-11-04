# content／Wiki 内容来源

这个文件夹是**网页唯一的资料来源**。  
`site/index.html` 是由这里生成的成品，不要直接改 HTML 里的资料。

> 不使用 `_data/*.md`、`页面/*.md` 当来源；那些旧 md 没有被收进这里的内容，也不会被读取。

---

## 目录结构

```
content/
  characters/
    _meta.json                 ← 角色顺序、永久能力、状态表
    _skills.json               ← 战斗技能表
    白璃/
      character.json
    小猦/
      character.json
      design.css               ← 角色专属样式（可选）

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
    60-盡力彌補/
      floor.json               ← 楼层资料、正文、配色
      events.json              ← 这一层的事件／岔路（唯一来源）
      endings.json             ← 这一层的结局（唯一来源）
      design.css               ← 这一层的专属页面设计（只作用于本层）
    197-打烊的量販店/
      floor.json
      events.json
      endings.json
      design.css

  shops/<商店>/shop.json
  pages/pages.json             ← 关于页等静态页
  recipes/recipes.json
  enhancements/enhancements.json
  template.html                ← 外壳（字体、CSS、UI），一般不用改
  build.mjs                    ← 生成 site/index.html
  new.mjs                      ← 快速新增条目
```

---

## ⚠️ 事件按来源分类，楼层事件只能放该楼层文件夹

**硬性规则：**

```text
floors/<楼层文件夹>/events.json        楼层事件／岔路
characters/<角色>/events.json          角色专属事件
events/common/events.json              通用事件
```

- **楼层事件**只写在自己的 `floors/<楼层文件夹>/events.json`；不要建立全域 `content/events.json`。
- **角色事件**只写在该角色的 `characters/<角色>/events.json`；**通用事件**写在 `events/common/events.json`。两者不属于任何楼层。
- `build.mjs` 会扫描楼层、角色、通用三种 events 来建立事件浏览器。
- `event-import.mjs` 依楼层 ID 写回该楼层；`import-manual.mjs` 依 `scopes` 写楼层／角色／通用。

---

## ⚠️ 楼层结局只能放该楼层文件夹

**硬性规则：**

```text
floors/<楼层文件夹>/endings.json
```

- 每个结局只写在它对应楼层的 `endings.json`
- 多楼层共用的结局（例如“迷失”）要在每个对应楼层各放一份 variant
- **不要再建立或使用 `content/endings/` 全域结局文件夹**
- `build.mjs` 只扫描各楼层的 `endings.json` 来建立结局浏览器资料
- `category` 用 `bad`（坏结局）／`settle`（安顿下来）／`neutral`（中立结局）；旧 `core`／`good`／`lost`／`extra` 仍兼容。

---

## 修改后一定要重建

```powershell
node content/build.mjs
# 或双击／执行
powershell -ExecutionPolicy Bypass -File content/build.ps1
```

成功后会覆盖 `site/index.html`；第一次重建前会保留一份 `site/index.backup-before-content.html`。

---

## 快速新增条目

```powershell
# 实体
node content/new.mjs entity 新实体名

# 角色（第二个参数是种族）
node content/new.mjs character 新角色 狐狸

# 物品（分类＋名称＋价格）
node content/new.mjs item 武器 新武器 15

# 楼层（楼层 ID＋名称）
node content/new.mjs floor 197 打烊的量贩店
```

建立后编辑生成的 JSON，再跑 `node content/build.mjs`。

也可以把参数直接交给批量档：

```powershell
content\新增条目.bat entity 新实体名
content\新增条目.bat character 新角色 狐狸
content\新增条目.bat item 武器 新武器 15
content\新增条目.bat floor 197 打烊的量贩店
```

---

## 各类文件怎么填

### 角色 `character.json`

```json
{
  "n": "白璃",
  "title": "梦之方舟的狐巫女",
  "bio": "介绍…",
  "attrs": "力 0　敏 4…",
  "skills": ["技能…"],
  "items": ["自带道具…"],
  "sp": "狐狸"
}
```

### 实体 `entity.json`

```json
{
  "n": "竊皮者",
  "boss": false,
  "skills": [{ "n": "撕抓", "eff": "效果…" }],
  "drops": ["菜刀", "急救包"],
  "desc": "介绍…",
  "resist": { "res": "钝击", "nor": "", "weak": "挥砍、火焰" }
}
```

- 不要再加 `floor`、`hp`、`atk`、`def`、`spd`、`variants`、`src`；这些不会显示。
- BOSS 加 `"boss": true`，网页会显示名字后缀 `[BOSS]` 并优先排序。
- 掉落物可以是字串，或 `{ "n": "巨大鱗片", "e": "防禦+3…" }`。

### 物品 `item.json`

```json
{
  "i": "item-骨刃短刀",
  "n": "骨刃短刀",
  "c": "weapon",
  "sl": "单手",
  "p": "11",
  "x": "false",
  "eff": ["单手近战 挥砍 1d6（敏捷）", "耐久 16"],
  "desc": "介绍…"
}
```

- `p` 是**售价**，只填游戏显示的参考价格；没标价就留空。
- 不要再加 `d`、`st`、`q`、`s`、`tier`；这些不会显示。
- **不要记“这一件／耐久 16/16”**这种当前实例耐久。
- 不要写“实测”或任何测试数据。
- `c` 分类：`weapon`／`armor`／`accessory`／`tool`／`consumable`／`ammo`。

### 楼层 `floor.json`

```json
{
  "key": "LX|尽力弥补",
  "id": "LX",
  "name": "尽力弥补",
  "info": { "id": "LX", "name": "尽力弥补", "roman": "LX", "items": [], "desc": "" },
  "doc": "<h4>环境</h4><blockquote>…</blockquote>",
  "palette": { "bg": "#EDEBE6", "acc": "#6F6A5E" },
  "paletteKey": "60",
  "length": 13
}
```

- `length` 是该层“出口进度 N/M”的总数 M；由记录器的 `floor-lengths-*.jsonl` 独立导入，不属于事件。
- `doc` 只放楼层页真的会显示的“环境”引言 HTML；旧事件记录、表格、`<code>`、未显示段落一律不要放。
- 这一层的专属设计写在**同文件夹的 `design.css`**，重建时会一起塞进网页。
- `design.css` 只作用于 `html[data-pal="…"]`，不要引用其他楼层。

---

## 规则

- **不要**把没收进 Wiki 的旧 md 内容搬进来。
- **不要**把截图放进文件夹；截图只是转录素材，转录完就删。
- 修改内容只改 JSON；`template.html` 只放外壳，不要把资料写回去。
- 重建后再用浏览器开 `site/index.html` 验证。
