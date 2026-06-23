# Changelog

## [1.1.0] - 2026-05-30
### Added
- Spline 新增「管状」形态（Strip / Cross / Tube 三选一下拉），沿路径生成封闭圆形截面，复用宽度+宽度曲线作管半径，新增截面边数参数
- Ring 新增「UV 模式」（Auto / Rect / Circular），圆盘场景（高度≈0）下 UV 按 XZ 位置圆形映射，贴图不变形

### Fixed
- 修复 Tube 代码块被误插入 BuildHelix() 导致的 6 条 CS0103（samples/tangents/rights 未定义）编译错误，将 Tube 逻辑移回 BuildSpline() 变量定义之后，花括号配平（202/202）

## [1.0.0] - 2026-04-02
### Added
- Initial release
