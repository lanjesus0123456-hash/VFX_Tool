# v1.9.0 Spline 保存／读取验收

验收时间：2026-10-10。引擎：Unity 2020.3.26f1，独立测试工程。

## 结果

| 验收项 | 结果 |
| --- | --- |
| 完整工具源码编译 | 0 error；原有 4 条未使用字段 warning |
| 两条路径，Strip / Cross / Tube 保存后读取 | 通过；分别 396 / 792 / 792 顶点 |
| 顶点、法线、三角形、UV1、UV2、顶点色 | 与保存前逐项完全一致 |
| 曲线关键帧、切线、权重、Loop / PingPong | 往返一致，采样值一致 |
| 阶梯切线 | 通过，Infinity 切线由显式标记恢复 |
| 预览 Transform 与 World Position | 完全一致 |
| 贴图资源 GUID + 路径 + 子资源标识 | 实际工程资源引用往返通过 |
| 读取后修改控制点及宽度曲线 | 新网格改变，可再次保存读取 |
| 读取后新增路径 | 第三条路径加入并保存读取通过 |
| 点与曲线深拷贝 | 编辑不会改变读取的预设对象 |
| 同文件覆盖保存 | 临时文件写入／替换通过 |
| 10 种错误文件 | 全部拒绝，当前参数、网格、记录路径保持不变 |
| 缺失贴图 | 显示说明，其它样条设置正常恢复 |
| 尚未保存的临时资源 | 保存前拒绝，避免静默丢失引用 |
| v1.5.0 原始归档 | SHA256 与之前记录一致 |

错误文件覆盖：不完整 JSON、空文档、错误格式、未来版本、少于两个控制点、翻转数量不符、超范围细分、未知形态、重复关键帧时间、无效旋转。

样条示例：`Releases/Samples/v1.9.0/Spline_ThreePaths_Tube.json`。示例包含三条管状路径及宽度曲线，无工程贴图／材质依赖，可直接读取并修改。

## 原始运行输出
```text
PASS
Unity 2020.3.26f1
Strip: 396 vertices; geometry/UV1/UV2/colors/settings/TRS identical.
Cross: 792 vertices; geometry/UV1/UV2/colors/settings/TRS identical.
Tube: 792 vertices; geometry/UV1/UV2/colors/settings/TRS identical.
Reload/edit/add path/resave: passed; lists and curves are independent.
10 invalid files rejected; current editing data and mesh preserved.
Missing texture reference: reported; spline data still editable.
Unsaved resources cannot be silently lost on save.
```
