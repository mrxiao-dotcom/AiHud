# AI大本营 · 黑悟空风格皮肤方案

> 项目代号：**WukongSkin**
> 目标：把现有 `MaterialDesignInXAML` 紫色主题改造为《黑神话：悟空》同款暗黑国风 + 烫金 + 水墨风格
> 文档版本：v1.0 · 2026-08-29
> 状态：方案定稿，待实施

---

## 1. 设计目标

### 1.1 风格关键词
- 暗黑国风 · 烫金 · 水墨 · 卷轴 · 紧箍咒
- 视觉气质：黑底烫金，毛笔字，宣纸纹理，远看肃穆近看精致

### 1.2 设计原则
| 原则 | 说明 |
|---|---|
| 暗色优先 | 主背景必须墨黑，**不允许**出现 MaterialDesign 默认紫 |
| 烫金克制 | 古铜金只用于强调、标题、激活态；不用做大面积背景 |
| 字体统一 | 全局使用霞鹜文楷（中文）+ Cinzel（西文/数字） |
| 纹理点到为止 | 宣纸背景做整体氛围，紧箍咒/卷轴只在关键位置出现 |
| 国风图标 | MaterialDesign 图标可继续用，但加烫金色覆盖层 |

---

## 2. 视觉规范

### 2.1 调色板

| 角色 | 色值 | 用途 |
|---|---|---|
| `--wukong-ink-black` | `#0a0a0a` | 主背景 |
| `--wukong-deep-black` | `#1a1a1a` | 卡片 / 次级背景 |
| `--wukong-ink-gray` | `#2a2a2a` | 边框、分割 |
| `--wukong-bronze-gold` | `#c9a063` | 强调、标题、激活态（主金） |
| `--wukong-dark-gold` | `#8b6f3a` | 边框、分隔线（暗金） |
| `--wukong-bright-gold` | `#e8c775` | hover 高亮、烫金高光 |
| `--wukong-cinnabar-red` | `#8b1a1a` | 删除 / 危险操作 |
| `--wukong-stone-blue` | `#1a3a5c` | 选中态背景 |
| `--wukong-rice-white` | `#f5e6c8` | 主文字（宣纸米） |
| `--wukong-ash-gray` | `#888888` | 次级文字 |

### 2.2 字体规范

| 角色 | 字体 | 字号 | 字重 |
|---|---|---|---|
| 窗口标题 | LXGW WenKai Bold | 24pt | Bold |
| 分类标题 | LXGW WenKai Regular | 15pt | Medium |
| 应用名 | LXGW WenKai Regular | 15pt | Medium |
| 按钮文字 | LXGW WenKai Regular | 14pt | Regular |
| 副标题 | LXGW WenKai Regular | 12pt | Regular |
| 数字（计数） | LXGW WenKai Regular | 12pt | Regular |
| 西文 / 数字 | Cinzel（备用，系统 Segoe UI 兜底） | — | — |

### 2.3 间距与圆角

- 全局基础间距：`8px`
- 卡片圆角：`8px`（侧栏分类）/ `12px`（应用卡片）
- 按钮圆角：`4px`（主操作）/ `8px`（次要操作）
- 烫金描边宽度：`1px`（默认）/ `2px`（hover）

### 2.4 阴影

```
默认:  0 2px 8px rgba(0, 0, 0, 0.6)
hover: 0 4px 16px rgba(201, 160, 99, 0.3)  ← 古铜金光晕
激活:  0 0 12px rgba(201, 160, 99, 0.5)
```

---

## 3. 素材清单

按优先级分为 **P0 必装 / P1 强烈建议 / P2 加分项**。所有素材必须**可商用**（CC0 / MIT / 个人免费商用）。

### 3.1 P0 · 必装（缺一不可）

| # | 素材 | 规格 | 用途 |
|---|---|---|---|
| 1 | `LXGWWenKai-Regular.ttf` | TTF | 全局正文字体 |
| 2 | `LXGWWenKai-Bold.ttf` | TTF | 标题强调 |
| 3 | `bg_xuan_dark.png` | 2048×2048+，无缝贴图 | 主窗口背景（墨色基调） |
| 4 | `bg_xuan_light.png` | 2048×2048+，无缝贴图 | 浅色模式备用 |
| 5 | `decor_jingkungold.png` | 2048×2048，PNG 透明 | 顶栏、Logo 背景、对话框头部 |

### 3.2 P1 · 强烈建议

| # | 素材 | 规格 | 用途 |
|---|---|---|---|
| 6 | `icon_wukong.ico` | 256×256 多分辨率 ICO | 替换 `TextIcon.ico` |
| 7 | `decor_scroll_horizontal.png` | 1024×64，PNG 透明 | 顶栏下沿卷轴边 |
| 8 | `decor_scroll_vertical.png` | 64×1024，PNG 透明 | 侧栏边缘卷轴 |
| 9 | `divider_ink.png` | 1×200，PNG 透明 | 毛笔墨色分隔线 |

### 3.3 P2 · 加分项

| # | 素材 | 用途 |
|---|---|---|
| 10 | `decor_flame_gold.png` | 空状态、Loading 圈外圈 |
| 11 | `glow_gold.png` | 选中态辉光 |
| 12 | `select_ink.mp3` / `click_jing.mp3` | UI 反馈音效（可选） |

### 3.4 资源目录结构

```
AiHUD/
└── Resources/
    └── Skins/
        └── Wukong/
            ├── Fonts/
            │   ├── LXGWWenKai-Regular.ttf
            │   └── LXGWWenKai-Bold.ttf
            ├── Backgrounds/
            │   ├── bg_xuan_dark.png
            │   └── bg_xuan_light.png
            ├── Decorations/
            │   ├── decor_jingkungold.png
            │   ├── decor_scroll_horizontal.png
            │   ├── decor_scroll_vertical.png
            │   ├── divider_ink.png
            │   ├── decor_flame_gold.png
            │   └── glow_gold.png
            ├── Icon/
            │   └── icon_wukong.ico
            └── Audio/  (可选)
                ├── select_ink.mp3
                └── click_jing.mp3
```

---

## 4. 工具与素材来源

### 4.1 字体获取

**霞鹜文楷 LXGW WenKai**
- GitHub：`https://github.com/lxgw/LxgwWenKai` → Releases 下载
- 推荐文件：`LXGWWenKai-Regular.ttf` + `LXGWWenKai-Bold.ttf`
- 许可：SIL Open Font License 1.1（免费商用）

### 4.2 纹理与装饰（AI 生成）

#### 国内可访问（推荐）

| 工具 | 网址 | 价格 |
|---|---|---|
| 即梦 AI | jimeng.jianying.com | 免费 |
| 通义万相 | tongyi.aliyun.com/wanxiang | 免费 |
| 可灵 AI | klingai.com | 免费额度 |
| 智谱清影 | chatglm.cn | 免费 |
| SeaArt | seaart.ai | 免费 |
| Liblib.art | liblib.art | 免费 |

#### 国外

| 工具 | 价格 |
|---|---|
| Midjourney v6 | $10/月起 |
| Leonardo.ai | 免费 150 token/天 |
| Stable Diffusion 本地 | 免费（需显卡） |
| Bing Image Creator | 免费 |

### 4.3 矢量与设计

| 工具 | 价格 | 用途 |
|---|---|---|
| Figma | 免费版 | LOGO、卷轴、UI 稿 |
| Inkscape | 免费开源 | 矢量、SVG、ICO 导出 |
| Pixso | 免费 | Figma 国内替代 |
| Affinity Designer 2 | $70 买断 | Illustrator 替代 |

### 4.4 后期处理

| 工具 | 用途 |
|---|---|
| GIMP | 调色、无缝贴图、合成、ICO 导出 |
| Photopea | 在线 PS 临时用 |
| Paint.NET | 轻量调色 |
| remove.bg | AI 抠透明 PNG |
| ConvertICO.com | PNG → 多分辨率 ICO |
| TinyPNG | PNG 压缩 |

### 4.5 现成 CC0 素材站

- [Unsplash](https://unsplash.com/s/photos/rice-paper-texture) — 搜 `rice paper texture dark`
- [Pexels](https://www.pexels.com/zh-cn/search/rice%20paper/) — 搜 `rice paper`
- [Pixabay](https://pixabay.com/images/search/rice%20paper%20texture/)

---

## 5. AI 生成 Prompt 模板

直接复制粘贴到即梦 / 通义 / Midjourney 即可。

### 5.1 宣纸纹理

```
old Chinese rice paper texture, dark ink wash background, 
subtle fibers and stains, deep charcoal black and warm 
dark brown, top-down flat view, no text, seamless tileable, 
8K high detail
```

### 5.2 烫金紧箍咒

```
ornate golden Chinese Taoist magic circle talisman, 
intricate filigree and seal script characters, 
gold leaf on pure black background, transparent 
background, flat 2D vector style, top-down view, 
symmetrical, 8K
```

### 5.3 卷轴边（横）

```
ancient Chinese scroll border, horizontal orientation, 
ornate gold filigree on dark wood roll, both edges 
decorated, isolated on black background, transparent 
middle, 8K detail
```

### 5.4 卷轴边（竖）

```
ancient Chinese scroll border, vertical orientation, 
ornate gold filigree on dark wood roll, both edges 
decorated, isolated on black background, transparent 
middle, 8K detail
```

### 5.5 毛笔水墨分隔线

```
single Chinese ink brush horizontal stroke, 
black ink splash with flying white edges, 
isolated on transparent background, wide format, 
8K
```

### 5.6 应用 LOGO 概念图

```
minimalist golden monkey king silhouette wearing 
tight golden headband, holding staff, circular 
composition, gold leaf on black, vector logo style, 
icon design, 8K
```

### 5.7 烫金火焰纹

```
Chinese gold flame pattern ornament, traditional 
cloud and flame motif, gold leaf on transparent 
background, flat vector style, symmetrical, 8K
```

---

## 6. 实施阶段

### 阶段 ① 主题色板重构

**目标**：把 MaterialDesign 的紫色覆盖为黑悟空色板，**不改 XAML 业务代码**。

**任务**：
- 新建 `Themes/WukongTheme.xaml`
- 覆盖 `MaterialDesign.Brush.Primary` / `Secondary` / `Background` / `Foreground`
- 注册所有 `--wukong-*` 颜色到 `SolidColorBrush` 资源
- `App.xaml` 切换到 `WukongTheme.xaml`

**工时**：1h

### 阶段 ② 字体注入

**任务**：
- `App.xaml` 添加字体资源字典（`pack://application:,,,/Resources/Skins/Wukong/Fonts/#LXGW WenKai`）
- 全局 `FontFamily="{DynamicResource WukongFontRegular}"`
- 标题元素用 `FontFamily="{DynamicResource WukongFontBold}"`

**工时**：30 min

### 阶段 ③ 背景层

**任务**：
- 主窗口 `Background` 改为 `ImageBrush`（宣纸纹理）+ 半透明黑色叠加
- 5 个对话框同步
- 调整 z-index 确保前景内容可读（叠加 `Background="#A0000000"`）

**工时**：1h

### 阶段 ④ 装饰元素

**任务**：
- 顶栏右侧放紧箍咒 PNG（带 Opacity=0.15 装饰）
- Logo 区换为烫金 LOGO + 紧箍咒背景
- 侧栏左/右加卷轴边
- 替换所有 `Rectangle` 分隔线为毛笔水墨

**工时**：1.5h

### 阶段 ⑤ 控件微调

**任务**：
- 按钮 hover 烫金描边 + 古铜金光晕
- 选中态用石青 + 古铜金边
- 删除按钮保留朱红，但加烫金边
- 进度条 / Loading 圈用古铜金

**工时**：1h

### 阶段 ⑥ 皮肤切换器

**任务**：
- "切换主题" 按钮扩展为"切换皮肤"下拉
- 在"MaterialDesign 默认" / "黑悟空风" / "未来其他皮肤"间切换
- 用户选择保存到 `%LOCALAPPDATA%\AiDaBenYing\skin.json`
- 启动时读取

**工时**：1h

### 总工时：约 6-8 小时

---

## 7. 关键文件改动清单

| 文件 | 改动类型 |
|---|---|
| `App.xaml` | 切换主题字典 + 加载字体 |
| `Themes/WukongTheme.xaml` | **新建** · 颜色与样式资源 |
| `Themes/WukongDecorations.xaml` | **新建** · 装饰元素样式 |
| `Resources/Skins/Wukong/...` | **新建** · 素材目录 |
| `Views/MainWindow.xaml` | 背景、字体、装饰 |
| `Views/AddCategoryDialog.xaml` | 背景、字体 |
| `Views/EditCategoryDialog.xaml` | 背景、字体 |
| `Views/EditAppDialog.xaml` | 背景、字体 |
| `Views/MoveAppDialog.xaml` | 背景、字体 |
| `ViewModels/SettingsViewModel.cs` | **新建** · 皮肤选择持久化 |
| `Resources/TextIcon.ico` | 替换为 `icon_wukong.ico` |
| `AiHUD.csproj` | ICO 路径更新 + 字体嵌入（如需） |

---

## 8. 验收清单

### 视觉验收
- [ ] 主背景墨黑，**无任何紫色残留**
- [ ] 全局字体为霞鹜文楷（西文兜底 Segoe UI）
- [ ] 标题与强调元素用古铜金 `#c9a063`
- [ ] 选中态有烫金发光感
- [ ] 删除按钮仍为朱红但不刺眼
- [ ] 宣纸纹理在主窗口可见但不抢眼
- [ ] 紧箍咒装饰在顶栏可见且不喧宾夺主
- [ ] 所有对话框风格统一

### 功能验收
- [ ] "切换主题" 按钮可切换至少 2 套皮肤
- [ ] 选择的皮肤重启后保留
- [ ] 原所有功能（添加/编辑/删除/移动/搜索）不受影响
- [ ] 性能可接受（背景图不卡顿，4K 屏不模糊）

### 兼容性验收
- [ ] 1080P / 2K / 4K 屏幕均不模糊
- [ ] 字体在 Win10 / Win11 正常显示
- [ ] DPI 缩放 100% / 125% / 150% 均正常

---

## 9. 后续扩展（P3+）

- [ ] 浅色模式：宣纸米 `#f5e6c8` 底 + 墨色字
- [ ] 动态皮肤：节日限定（春节、端午）
- [ ] 用户自定义：让用户上传自己的紧箍咒
- [ ] 音效：毛笔落纸、铃铛、木鱼 等 UI 音
- [ ] 动效：选中态金粉飘落、按钮 hover 烫金流光
- [ ] 隐藏彩蛋：连续点击 Logo 5 次触发金箍棒动画

---

## 10. 风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| 霞鹜文楷体积大（~30MB） | 安装包变大 | WPF 嵌入子集字体或运行时下载 |
| 宣纸纹理在高分屏模糊 | 视觉降级 | 准备 2x / 3x 多分辨率版本 |
| AI 生成图风格不统一 | 视觉割裂 | 同一 prompt 多次生成挑最一致 |
| 烫金过亮导致眼睛疲劳 | 体验差 | 暗金为主，亮金仅 hover |
| WPF 字体加载失败 | 文字显示异常 | 系统字体兜底（Microsoft YaHei UI） |

---

## 11. 参考资料

- 《黑神话：悟空》官方美术设定集（公开采访与幕后）
- 霞鹜文楷 GitHub：`https://github.com/lxgw/LxgwWenKai`
- MaterialDesignInXAML 主题文档：`https://github.com/MaterialDesignInXAML`
- WPF ImageBrush 与字体嵌入官方文档

---

**文档结束 · 实施开始 → 阶段 ①**
