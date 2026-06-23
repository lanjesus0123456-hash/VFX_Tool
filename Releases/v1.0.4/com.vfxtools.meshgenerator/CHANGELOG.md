# Changelog

## [1.0.4] - 2026-06-12
### Fixed
- Ring「UV 模式」下拉（Auto / Rect / Circular）恢复显示：此前 `ringUVMode` 字段与 BuildRing 逻辑都在，但面板漏画了下拉控件，导致无法选择。已在 Ring 参数区补回。
- Spline 矩形框选不再卡死：起框时抢占 `GUIUtility.hotControl` 并消费事件，避免 MouseUp 被 SceneView 默认工具吞掉导致框选框一直残留；拖拽/抬起按 hotControl 归属处理，画框块加 hotControl 门槛防鬼影。
### Notes
- 自此采用顺序补丁编号：每次备份 +0.0.1。对应旧语义化编号：1.0.0=v1.0.0、1.0.1=v1.1.0、1.0.2=v1.2.0、1.0.3=v1.3.0。

## [1.3.0] - 2026-06-11
### Added
- 新增第二套 UV（UV2）：可选开关，基于原始 0~1 展开生成，带独立 Rotate / U Tiling / V Tiling，与 UV1 分开调，互不影响
- FBX 导出支持 UV2：写入独立 Layer 1 的 UVSet1（标准多 UV 集做法）
### Changed
- 重构 UV 变换为共享方法 TransformUV，UV1/UV2 复用（DRY）；默认关闭 UV2 时行为与既有版本完全一致

## [1.2.0] - 2026-05-30
### Added
- Helix 新增「头部扭曲 / 尾部扭曲」两个独立角度（-360~360），条带绕路径(tangent)旋转，端点强、中间(t=0.5)平滑过渡到 0
- Spline 新增矩形框选（空白处拖框多选控制点）+ 多点整体移动（拖选集质心 Handle）
- Spline 新增「软选」（开关 + 软选半径 + 衰减曲线），移动选中点时半径内未选点按距离衰减跟随
- 预览新增「隐藏预览」开关（只关 MeshRenderer，不销毁；线框同步隐藏，Spline 控制点 Handle 保留以便继续编辑路径）

## [1.1.0] - 2026-05-30
### Added
- Spline 新增「管状」形态（Strip / Cross / Tube 三选一下拉），沿路径生成封闭圆形截面，复用宽度+宽度曲线作管半径，新增截面边数参数
- Ring 新增「UV 模式」（Auto / Rect / Circular），圆盘场景（高度≈0）下 UV 按 XZ 位置圆形映射，贴图不变形

### Fixed
- 修复 Tube 代码块被误插入 BuildHelix() 导致的 6 条 CS0103（samples/tangents/rights 未定义）编译错误，将 Tube 逻辑移回 BuildSpline() 变量定义之后，花括号配平（202/202）

## [1.0.0] - 2026-04-02
### Added
- Initial release
