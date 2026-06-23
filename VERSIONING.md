# VFX_Tool 版本归档规范

> 本文件定义工作区的目录职责与版本归档规则，避免源码副本散落各处。

## 目录职责

```
VFX_Tool/
├── Editor/                       开发源（唯一）—— 引擎实际加载的散装脚本
│   └── VFXMeshGeneratorWindow.cs
├── Runtime/                      运行时组件（样条、序列帧、粒子追踪）
├── Shaders/                      VFX Shader 集
├── Releases/                     版本归档（只读存档，发布快照）
│   ├── v1.0.0/                   每个版本一套完整 UPM 包
│   │   └── com.vfxtools.meshgenerator/
│   └── v1.1.0/
│       └── com.vfxtools.meshgenerator/
├── docs/superpowers/             设计文档与实现计划
├── .github/                      工作区指令
├── BUGFIX_README.md              Bug 修复历史
└── VERSIONING.md                 本文件
```

## 唯一开发源

- **`Editor/VFXMeshGeneratorWindow.cs` 是唯一的开发源**，引擎直接加载它。
- 任何代码改动只改这一份，**不再在工作区其他位置保留副本**。
- 历史上曾存在 5 份重复副本（`Editor\`、`com.vfxtools.meshgenerator\`、`Package\`、`VFX_MeshTool\com.vfxtools.meshgenerator\`、根目录裸文件），已于 2026-05-30 清理，只保留开发源 + Releases 归档。

## 版本归档规则

1. **何时归档**：每次功能迭代完成、引擎内验证通过后，打一个新版本。
2. **版本号**：语义化版本 `主.次.修订`（MAJOR.MINOR.PATCH）。
   - 修订（PATCH）：纯 Bug 修复
   - 次版本（MINOR）：新增面片类型 / 新增参数 / 向后兼容的新功能
   - 主版本（MAJOR）：不兼容改动（如导出格式变更、参数语义变更）
3. **归档内容**：把当前开发源打包成完整 UPM 包，复制到 `Releases/v{版本号}/com.vfxtools.meshgenerator/`，包含：
   - `Editor/VFXMeshGeneratorWindow.cs` + `.meta`
   - `Editor/VFXTools.MeshGenerator.Editor.asmdef` + `.meta`
   - `package.json`（version 字段与目录版本号一致）+ `.meta`
   - `CHANGELOG.md`、`README.md`、`LICENSE.md` + 各自 `.meta`
4. **CHANGELOG**：每个版本在 `CHANGELOG.md` 顶部追加一段，格式 `## [版本号] - 日期`，列出 Added / Changed / Fixed。
5. **归档只读**：`Releases/` 下的内容是历史快照，**不再修改**。

## 版本历史

| 版本 | 日期 | 摘要 |
|------|------|------|
| v1.0.0 | 2026-04-02 | 初版：6 种面片（Plane/Ring/Cross/Sphere/Helix/Spline）+ FBX 导出 |
| v1.0.1 | 2026-05-30 | Spline 新增管状形态（三选一下拉）；Ring 新增圆形 UV 模式（Auto/Rect/Circular）。含一处修复：Tube 代码曾误插入 BuildHelix 导致 CS0103，已移回 BuildSpline（旧编号 v1.1.0） |
| v1.0.2 | 2026-05-30 | Helix 头尾扭曲（头/尾独立角度）；Spline 矩形框选+多点整体移动+距离衰减软选；预览隐藏开关（旧编号 v1.2.0） |
| v1.0.3 | 2026-06-11 | 第二套 UV（UV2，独立 Tiling/Rotate，默认关）；FBX 导出 UVSet1（独立 Layer1）；重构共享 TransformUV（旧编号 v1.3.0） |
| v1.0.4 | 2026-06-12 | 修复版：恢复 Ring「UV 模式」下拉（面板漏画控件）；修复 Spline 矩形框选卡死（起框抢占 hotControl + 消费事件，框选框不再残留）。已部署到 `F:\trunk-test\trunk-test\dev\client\nshm\Assets\Editor\`，并作为网盘 1.0.4 |
| v1.1.0 | 2026-06-12 | **大版本**：新增 UV 预览窗口（独立可停靠，UV1/UV2 切换+贴图背景+缩放平移）；UV2 独立 UV 模式（矩形/圆形按通道）；预览模型同步显示选中通道 UV（shader 加 `_UVChannel`）；UV 模式去 Auto 并中文化（矩形/圆形）；Spline 形态中文化（条带/十字/管状）；顶部 Transform(TRS) 折叠收起、翻转/轴点更名「法线/轴点」折叠。已上传网盘 |
| v1.2.0 | 2026-06-23 | **新增 GhostTrail 刀光**（第 7 种 Mesh 类型，复刻 3ds Max GhostTrail）：挂标记点采样武器运动连成刀光带→导出模型。多轮迭代成完整工作流：三种采样（自动播放并记录 / 跟随播放记录 / 采样当前帧）+ 自动检测 EffectEditorDirector + 子帧采样密度；单点宽度实时生效、统一面数重采样、纵向分段、保持宽度的中心线平滑（强度+可调上限）、十字面片；拉伸轴向/轴点（默认 Forward / 起始点）；UV1+UV2 各加 U/V 翻转；导出时保持世界位置开关。修复刀光法线朝向、首尾帧采样污染。界面精简（次要项收入「更多设置」） |

> 编号说明：2026-06-12 起，日常保险快照走顺序补丁号（每备份 +0.0.1，如 1.0.x）；积累一批新功能的大版本则进次版本号（MINOR，如 1.1.0）。旧语义化编号已对齐重命名：v1.1.0→v1.0.1、v1.2.0→v1.0.2、v1.3.0→v1.0.3。各归档 `package.json` version 已同步；CHANGELOG.md 内的历史小节标题保留原编号，作为"该功能何时加入"的事实记录。

> v1.0.0 源码已于 2026-06-11 从原生备份 `G:\nsh_beifen\02\dev\client\nshm\Assets\VFX_Tools\VFX MeshGenerator\Editor\` 找回并归档（MD5 `0CE30A7C`，与历史记录一致）。三个版本归档齐全。

## 经验沉淀

### 改源码前必须备份
- 改任何源码前，先备份到带时间戳的临时目录 `C:\Users\lanhao01\AppData\Local\Temp\opencode\`，并做哈希校验（SHA256）。
- 当前已知备份：
  - `VFXMeshGenerator_WS_broken_backup_20260530_195306\`（损坏版，SHA256 `A6CE61D0...`）— Tube 误插 BuildHelix 的版本，可按需清理。

### 长文件 Edit 同名代码块匹配风险（本次教训）
- `BuildHelix()` 和 `BuildSpline()` 里都有 `for (int p = 0; p < passes; p++)` 这种相同语句。用 Edit 按字符串插入代码时，容易匹配到错误方法里的那一行，导致代码块插错位置，引用了不属于该方法的局部变量（CS0103）。
- **对策**：在长文件里插入代码，oldString 必须带足够上下文确保唯一匹配；插入后立即做结构校验（确认目标方法范围内无外部局部变量引用）+ 花括号配平检查。

### 编译报错先分清两类问题
- **环境问题**：如 F 工程「启动器开关导致不本地编译」，工具菜单打不开但代码本身没错。
- **真实编译错误**：如 H 工程「代码合并/粘贴错位」的 CS 报错。
- **诊断方法**：先看 Unity `Editor.log` 里有没有 `error CS`。有 → 真实编译错误，查代码；无 → 环境/编译模式问题。

### 各工程编译模式
- H 工程（`H:\release\release_new\dev\client\nshm\`）：本地编译模式，改码后 Unity 刷新即生效。
