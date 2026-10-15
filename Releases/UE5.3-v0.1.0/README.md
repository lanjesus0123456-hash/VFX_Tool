# VFX Mesh Tools UE 0.1.0（UE 5.3 测试版）

这是独立于 Unity v1.6.0、v1.7.0、v1.8.0 的 UE 5.3 插件版本。将本目录的 `VFXMeshToolsUE` 复制到目标工程的 `Plugins/`，启用后重启编辑器；也可使用上级 `Releases/` 目录的 ZIP 包。附带本机 Windows UE 5.3 编辑器 DLL 和完整 C++ 源码，游戏目标由项目打包时编译。其他 UE 5.3 构建的 BuildId 不同时请从源码重新编译。

在关卡里放置 **VFXMeshToolActor**，Details 中选择 `Mode`。修改参数会重建，点击 `Rebuild` 也可手动刷新；`Material` 可指定自己的特效材质。几何使用 UE 的 `ProceduralMeshComponent`。

| 模式 | 可用功能 |
| --- | --- |
| Spline | `SplinePaths` 可新增多条路径；条带、十字、管状。 `PointSpace` 在 Local / World 间切换时换算数据，形状保持原位。 |
| Helix | `HelixProfile` 按高度 0→1 缩放整个水平截面；中点值 0.5 可收细腰部。 |
| Custom | `CustomBoundary` 的 XY 是平面轮廓、Z 是高度；`CustomHeightPoints` 只保存 XY 和相对高度，所以边界抬高时面内点随之抬高。可开启不规则边缘、设幅度／细分／频率／种子。 |
| Fracture | 指定 `FractureSource` Static Mesh；生成相邻三角面分组的独立碎片，补背面和侧壁，设置厚度及预览散开。 |
| GroundSpikes | 形状、排列、生长三个参数模块；锥／晶／刃／混合、直线／圆环／弧形、三种顺序。编辑器用 `PreviewGrowthTime` 预览，运行时 `bPlayGrowth` 自动播放，可循环。 |

UE 使用 Z 作为竖直轴，对应 Unity 版本中的 Y。地刺根部在局部原点，生长驱动每根节点的局部 Z 缩放。随机种子相同时可重复生成。

## FBX

- Unity [v1.7.0](../v1.7.0/com.vfxtools.meshgenerator/) 提供破碎多节点 FBX 导出。
- Unity [v1.8.0](../v1.8.0/com.vfxtools.meshgenerator/) 提供地刺最终模型 FBX 与带每根节点 Y 缩放关键帧的生长动画 FBX。
- UE 测试插件在引擎内直接生成与播放，不提供 UE 内 FBX 导出按钮。需要 FBX 文件时使用上述 Unity 发布版。UE 对普通静态网格 FBX 的节点动画导入方式取决于导入工作流；此测试版在 UE 中直接播放生长，避免依赖该导入设置。

## 初版限制

破碎是表面分片并补厚度，适合薄片和特效模型；尚非闭合实体的布尔体积切割。UV 接缝可能将相邻面分开。打包运行时请为源 Static Mesh 开启 **Allow CPU Access**，并使用可用的 LOD0 数据。单次最多处理 50,000 个三角面、64 个碎片。Custom 不规则边缘检测到自交时会缩小扰动；若原始轮廓已经自交，则无法保证正确三角化。程序生成面片只写 UV0；复杂材质所需的额外 UV 与自定义顶点数据仍需后续扩展。

## 验收

- 本机 UE 5.3 `BuildPlugin`：UnrealEditor Development、UnrealGame Development、UnrealGame Shipping 均构建成功。
- UE 5.3 无界面自动化测试 `VFX.MeshTool.Smoke`：1 成功、0 失败、0 警告；检查五种模式、双 Spline 坐标换算、Helix 腰部 0.5、Custom 边界高度跟随、2 块带厚度的碎片、8 根确定性地刺、Tick 驱动的 0→1 生长，以及生成碎片不写入关卡实例组件。
- 本机 VS 2022 工具链比 UE 5.3 自带版本新；插件自身禁用共享 PCH 并定义兼容宏后通过构建，未修改 UE 安装目录。



