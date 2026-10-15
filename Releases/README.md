# 新版本位置

| 版本 | 内容 | 验收 |
| --- | --- | --- |
| [Unity v1.6.0](v1.6.0/com.vfxtools.meshgenerator/) | 多 Spline、Local / World 点位置、Helix 收腰曲线、Custom 高度点跟随及不规则边缘 | Unity 2020.3 编译与实际网格验证通过 |
| [Unity v1.7.0](v1.7.0/com.vfxtools.meshgenerator/) | 表面破碎初版、独立轴点与多节点 FBX | Unity 2020.3 中生成 6 块；FBX SDK 回读 6 节点、148 面 |
| [Unity v1.8.0](v1.8.0/com.vfxtools.meshgenerator/) | 模块化地刺、错峰生长、最终与动画 FBX | Unity 2020.3 生成与导出通过；FBX SDK 回读 8 节点、8 动画曲线 |
| [Unity v1.9.0](v1.9.0/com.vfxtools.meshgenerator/) | Spline 全路径数据保存／读取，恢复后继续编辑 | Unity 2020.3 三种形态往返、改点／增路径／再保存及错误文件保护通过 |
| [UE 5.3 v0.1.0](UE5.3-v0.1.0/) | UE 内五种模式生成与地刺运行时生长 | UE 5.3 三目标构建成功，自动化测试 1/1 通过 |

每个版本在独立目录，旧版本保留。

已验证的示例 FBX 在 [Samples](Samples/)：v1.7.0 破碎样例、v1.8.0 最终地刺模型与生长动画。
UE 5.3 测试版下载包：[VFXMeshToolsUE-0.1.0-UE5.3-Win64.zip](VFXMeshToolsUE-0.1.0-UE5.3-Win64.zip)。


Spline 可读取示例：[Spline_ThreePaths_Tube.json](Samples/v1.9.0/Spline_ThreePaths_Tube.json)。v1.9.0 验收记录：[ACCEPTANCE.md](v1.9.0/ACCEPTANCE.md)。
