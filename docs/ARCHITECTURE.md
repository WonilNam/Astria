# 架构与界面保留边界

程序入口是唯一的 `Astria` 项目。Infrastructure 是 Windows 与存储类库，不是另一个程序；Domain 和 Application 同样只输出 DLL。

依赖为：Astria → Application、Infrastructure；Infrastructure → Application；Application → Domain。组合根通过构造参数接入新 Workspace、存储和系统能力，不增加消息总线或通用仓储。

## 保留的 UI

Views、ViewModels、Styles 以及卡片布局、动效和托盘菜单均来自用户原有的 Astria 界面。UI-BASELINE.json 记录 23 个批准保留文件的哈希，测试保证没有为适配底层而改写这些文件。

## 适配层

DesktopSession 和 Adapters 保留原界面使用的模型形状，将操作转发到新 Workspace。对界面暴露的数据模型不负责系统调用；真实导入、排序、分组、搜索和保存由 Application / Infrastructure 处理。部分命名空间为了保持原 UI 源文件兼容而保留，不代表依赖旧 Core 或 Platform 程序集。

使用适配层的代价是需要做模型映射，但它避免以底层重构为由重写用户界面，也使后续能逐项移除兼容契约。原界面偏好完整序列化到 Catalog 的 presentationJson，卡片坐标、主题和快捷键不会因领域配置较少而丢失。

Workspace 使用不可变候选状态：先验证、保存，成功后替换内存状态。JSON 保存采用同目录临时文件和原子替换。运行窗口在后台定期核对，图标由单独 STA 工作者缓存；窗口隐藏时沿用原 UI 的动画暂停逻辑。

本次不改变原界面布局、功能入口与交互行为。后续新增功能或改视觉需要单独提出，不与底层架构修改绑定。
