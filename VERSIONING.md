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
│   ├── v1.5.0/                   历史归档
│   └── v1.9.0/                   （最新 Unity 版）
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
| v1.3.0 | 2026-07-06 | **大版本**：4 项新功能。① Plane 新增矩形/圆形 UV 模式（极坐标投影，UV1/UV2 独立）。② Ring 龙卷风模式：半径曲线（上→下 AnimationCurve 权重）+ 高度曲线 + 倾斜 X/Z（-45~45°），RingCircularUV 改用实际顶点最大距离归一化。③ 全局 Noise 形变（所有面片类型）：噪声贴图采样偏移顶点，5 种方向（法线/X/Y/Z/自定义），UV 缩放+顶点色遮罩+像素缓存。④ 新增 Custom 自定义多边形模型（第 8 种 Mesh 类型）：Scene 视图编辑边界控制点 → Ear Clipping 三角剖分自动补面 → UV 流动方向（世界X/Z·本地X/Z·包围盒自动）+ U Tiling + 翻转；复用 Spline 交互（拖拽/Shift插入/框选/软选）+ 边界细分 |
| v1.4.0 | 2026-07-07 | **大版本**：① Custom 补面重写——网格角点采样（边缘有缝）→ 逐单元 Sutherland-Hodgman 裁剪 + 耳切三角化，顶点精确贴边零缝隙；顶点焊接消除法线断裂（Noise/高度形变不再开裂）。② Custom 立体高度控制——面内加高度点（位置+高度+半径+衰减曲线），Scene 三轴拖拽雕成立体面。③ 极坐标 UV（Plane/Ring/Custom）——角度→U、半径→V；修复环绕缝 + 中心奇点（复制跨缝/触心三角形独享 UV）。④ Plane 扭曲带动 UV——极坐标用扭曲前原始网格算角度，贴图随扭曲螺旋（流光沿螺旋边缘→中心）。⑤ Noise 修复与增强——采样按贴图尺寸+双线性（原只采左上角）、免 Read/Write（Blit 读像素）、UV 缩放→Tiling+Offset、遮罩与顶点色解耦（独立 UV 渐变 + 高度曲线遮罩，修复高度遮罩失效）。⑥ 预览流速 U/V（不烘焙）+ 预览剔除背面开关。移除 Plane 圆形 UV、Custom U Tiling（与主 UV 重复） |
| v1.5.0 | 2026-07-30 | **大版本**：① 修复 Custom 边界控制点 Y 被完全忽略（面片恒为平面）——新增 MVC（均值坐标）边界高度插值，拖动边界点上下即整片起伏；边缘点走沿边线性插值故无台阶；Shift 插点的 Y 沿边插值、就近判定改比 XZ；顺带修复高度点「高度/半径」滑条硬编码 ±5/0.1~10 导致 Scene 拖超范围被默默夹回。② Spline/Custom 控制点新增旋转 / 缩放工具（原先只有移动），绕选集质心、支持软选权重衰减。③ Spline/Custom 控制点新增世界坐标 / 本地坐标切换，并给出本地+世界两组 Vector3 数值框可双向输入。④ UV Tiling 预览（全类型）——「预览平铺 U/V」（不烘焙，Shader 加 `_PreviewTile`）+ 面板平铺缩略图（跟随预览流速滚动）；U/V Tiling 四个滑条上限改为可自定义。⑤ Spline 宽度/管半径上限从硬编码 5 改为可自定义。⑥ 各含十字面片的类型（Ring/Helix/Spline/GhostTrail/Cross）新增「十字面宽度(×)」，默认 1 = 与主面等宽故旧结果不变，上限均可自定义。新增通用控件 `SliderWithMax`（滑条 + 可改 max）。验证：Unity 2020.3.26f1 引用集编译 0 error、警告与 v1.4.0 一致；MVC 插值独立数值测试全通过 |
| v1.6.0 | 2026-10-09 | 多 Spline 合并，Local/World 点位置切换，Helix 沿 Y 轮廓缩放，Custom 高度点跟随与不规则边缘。Unity 2020.3 验收通过 |
| v1.7.0 | 2026-10-09 | 表面破碎初版，独立轴点、厚度和多节点 FBX。Unity 生成及 FBX 回读验收通过 |
| v1.8.0 | 2026-10-09 | 模块化地刺、错峰生长预览，最终模型及节点生长动画 FBX。8 节点／8 动画曲线验收通过 |
| v1.9.0 | 2026-10-10 | Spline 全路径数据保存／读取，保留曲线及公共形状设置，读取后可继续编辑。三种形态往返、再编辑及错误文件保护验收通过 |

> 编号说明：2026-06-12 起，日常保险快照走顺序补丁号（每备份 +0.0.1，如 1.0.x）；积累一批新功能的大版本则进次版本号（MINOR，如 1.1.0）。旧语义化编号已对齐重命名：v1.1.0→v1.0.1、v1.2.0→v1.0.2、v1.3.0→v1.0.3。各归档 `package.json` version 已同步；CHANGELOG.md 内的历史小节标题保留原编号，作为"该功能何时加入"的事实记录。

> v1.0.0 源码已于 2026-06-11 从原生备份 `G:\nsh_beifen\02\dev\client\nshm\Assets\VFX_Tools\VFX MeshGenerator\Editor\` 找回并归档（MD5 `0CE30A7C`，与历史记录一致）。三个版本归档齐全。

## 经验沉淀

### 改源码前必须备份
- 改任何源码前，先备份到带时间戳的临时目录 `C:\Users\lanhao01\AppData\Local\Temp\opencode\`，并做哈希校验（SHA256）。
- 当前已知备份：
  - `VFXMeshGenerator_WS_broken_backup_20260530_195306\`（损坏版，SHA256 `A6CE61D0...`）— Tube 误插 BuildHelix 的版本，可按需清理。
  - `VFXMeshGenerator_pre_v1.5.0_20260730_191727\`（v1.4.0 源码，SHA256 `8b2e76cf...`）— 做 v1.5.0 六项改动前的快照。

### 不开 Unity 也能验编译（v1.5.0 新增）
改完源码不必等 Unity 刷新，可直接用 Roslyn + 目标工程的 Unity 引用集离线编译，几秒出结果：

```bash
U="/e/nsh_trunk/binary/unity_win/Editor/Data"          # 目标工程用的 Unity 2020.3.26f1
API="$U/MonoBleedingEdge/lib/mono/4.7.1-api"           # mscorlib/System/... 引用集
CSC="/c/Program Files/dotnet/sdk/9.0.300/Roslyn/bincore/csc.dll"
# -r: API/*.dll + API/Facades/*.dll + Managed/UnityEditor.dll + Managed/UnityEngine/UnityEngine*.dll
#     + Autodesk.Fbx.dll（从 <工程>/Library/ScriptAssemblies/ 拷一份）
dotnet "$CSC" -nostdlib+ -noconfig -target:library -langversion:8 -define:UNITY_EDITOR ... 源码.cs
```

- 完整脚本存于 `C:\Users\lanhao01\AppData\Local\Temp\vfxtool_compile\build2020.sh`。
- **引用集必须对上工程的 Unity 版本**：用 2022.3 的引用集编译会在 `ShaderUtil.CreateShaderAsset(ref src, ...)` 处报 CS1615（2020.3 走的是不带 `ref` 的重载，由 `UNITY_2021_2_OR_NEWER` 分支控制）。
- 判据：拿改前的备份也编一遍，**警告集合应完全一致**（本工具历史遗留 4 条未使用字段：`backMat` / `customFoldout` / `splineFoldout` / `meshIndex`），多出来的就是这次改坏的。

### 纯数学逻辑可以脱离 Unity 单测
- 像 MVC 边界高度插值这种纯数值算法，把函数原样抄进一个 `net9.0` 控制台工程（只需自己写 `Mathf.Sqrt/Abs/Lerp` 几个桩），就能对着解析解断言：顶点命中、边上线性插值、正方形中心对称权重、三角形内退化为重心坐标、全平输入输出恒 0、凹多边形无 NaN。
- v1.5.0 的测试工程：`C:\Users\lanhao01\AppData\Local\Temp\vfxtool_mvctest\`。

### 加参数上限时不要硬编码
- 历史上多处滑条把上限写死（Spline 宽度 5、高度点 ±5、Tiling 10）。硬编码上限的真实危害不只是「调不上去」：`EditorGUILayout.Slider` 会**默默把超范围的值夹回**再写回字段，于是 Scene 里拖出去的值一回到面板就被吃掉，表现为「拖不动 / 拖了没反应」。
- 对策：统一用 `SliderWithMax(label, value, min, ref max)`（滑条右侧带 max 输入框），并在检测到实际值超过上限时自动抬高上限。

### 长文件 Edit 同名代码块匹配风险（本次教训）
- `BuildHelix()` 和 `BuildSpline()` 里都有 `for (int p = 0; p < passes; p++)` 这种相同语句。用 Edit 按字符串插入代码时，容易匹配到错误方法里的那一行，导致代码块插错位置，引用了不属于该方法的局部变量（CS0103）。
- **对策**：在长文件里插入代码，oldString 必须带足够上下文确保唯一匹配；插入后立即做结构校验（确认目标方法范围内无外部局部变量引用）+ 花括号配平检查。

### 编译报错先分清两类问题
- **环境问题**：如 F 工程「启动器开关导致不本地编译」，工具菜单打不开但代码本身没错。
- **真实编译错误**：如 H 工程「代码合并/粘贴错位」的 CS 报错。
- **诊断方法**：先看 Unity `Editor.log` 里有没有 `error CS`。有 → 真实编译错误，查代码；无 → 环境/编译模式问题。

### 各工程编译模式
- H 工程（`H:\release\release_new\dev\client\nshm\`）：本地编译模式，改码后 Unity 刷新即生效。


## 完整版本入口

Unity v1.5.0～v1.9.0、UE 5.3 测试版、FBX 与样条示例见 [Releases/README.md](Releases/README.md)。当前根目录开发源码仍保留已有版本；测试新功能时使用相应 Releases 版本的安装入口。
