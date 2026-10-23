# 来源与验证

用户明确要求复用其原有 Astria UI。因此本版本保留其 Views、ViewModels、Styles、资源和 UI 动效代码，未将这些部分改写为另一套界面。23 个界面相关文件有逐文件 SHA-256 基线和自动化检查。

新 Application、Domain、Infrastructure 与界面适配器在本工作区实现，不引用历史 Core、AppServices 或 Platform 项目。命名相近的表示层 DTO 是新编写的兼容契约；源代码复用范围和新的底层实现分别记录，不声称整份项目都是从零实现。

拼音数据从 Unicode 17.0.0 官方 Unihan.zip 生成，依赖和许可见 third-party/SOURCES.json。图标是本次 Astria 品牌资源。第三方依赖声明保留；这里记录实际实现过程，不作消除所有历史权利义务的法律保证。

验证包括：原 UI 文件哈希一致、原三种 Dock 布局显示 14 个常用项、21 项分页、输入即搜索、原主题与卡片设置重载、分组、搜索别名、自定义图标备份恢复、新领域用例与磁盘错误处理，以及真实程序后台启动和 IPC 退出。

无头测试不能代替所有多屏 DPI、UAC、WindowsApps 权限和实际鼠标拖拽环境的完整验收。源码包不包含用户数据、.git、bin/obj 或前一候选的简化界面。
