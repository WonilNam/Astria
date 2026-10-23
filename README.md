# 星屿 Astria

保留用户原有的 Avalonia 界面和全部界面交互，以重新实现的应用层、领域模型和系统适配层提供底层能力。

## 程序与结构

只有一个可执行程序：`Astria.exe`。解决方案只有一个 UI 项目 `src/Astria/Astria.csproj`，没有两套界面入口。

- `Astria`：原有 XAML、样式、卡片、动效、管理页、设置页、拖拽、搜索和托盘菜单；新增的 Adapters 只负责连接新底层。
- `Astria.Application`：应用导入、搜索、排序、分组、备份等用例及能力接口。
- `Astria.Domain`：领域模型和状态校验。
- `Astria.Infrastructure`：Windows API、快捷方式、图标、单实例与 JSON 存储。它是类库，不能独立运行。

原来重写候选中的简化 UI 已不包含在本目录。23 个原有 UI、样式、动效与资源文件通过 SHA-256 对照保持逐字节一致，清单为 `UI-BASELINE.json`，测试会持续检查。

## 构建和运行

Windows 11 x64，.NET SDK 10.0.401，Avalonia 11.3.22。

```powershell
dotnet build Astria.slnx -c Release
dotnet test Astria.slnx -c Release
dotnet run --project src/Astria/Astria.csproj -c Release
./build/publish.ps1
```

默认发布为包含 .NET 10 运行时的完整便携目录 `artifacts/win-x64`。运行 `Astria.exe`，分发时保留整个目录。

## 原有功能

管理页每页 21 项，输入即搜索，支持拖入程序、快捷方式、文件与文件夹、跨页排序、常用、重命名、自定义图标、分组和开始菜单扫描。Dock 沿用原来的浮动卡片、三种形状、旋转、分页、左侧快捷区、中央搜索和功能栏。页面与 Dock 主题、快捷键录入、桌面图标按钮、启动隐藏桌面图标及登录启动入口均保留。

默认快捷键：Ctrl+Alt+Space 呼出/隐藏 Dock，Ctrl+Alt+S 呼出/隐藏设置；再次按下收起前台窗口。

## 数据

数据保存在 `%LOCALAPPDATA%/Astria/Data/catalog.json`，原有外观配置作为 presentationJson 随同保存。默认数据目录首次没有 catalog.json 时，读取 `%LOCALAPPDATA%/GlassDesk.Avalonia/Data` 并复制应用库、布局和外观到新目录，原目录不会改动。源目录异常会显示导入失败提示。

原来的「设置 → 恢复备份」既支持本版备份目录，也支持包含 apps.json 的原数据/备份目录。恢复前自动保存安全副本。备份包含自定义图标；移除和清空仍只移除启动入口，不删除目标文件。

`--data-root <目录>` 指定独立数据目录；`--settings` 打开设置；`--autostart` 后台启动；`--exit` 请求当前数据目录实例退出。切换前退出正在运行的其他版本，避免快捷键冲突。

架构见 docs/ARCHITECTURE.md，来源和验证边界见 docs/IMPLEMENTATION.md。第三方库及 Unicode 数据保留各自声明，见 THIRD-PARTY.md。

## 作者与阅读入口

作者：**WonilNam**。Copyright © 2026 WonilNam. All rights reserved.

版权声明见 [COPYRIGHT.md](COPYRIGHT.md)，作者信息见 [AUTHORS.md](AUTHORS.md)。第三方材料仍适用其原有许可。

第一次阅读项目，先看 [项目架构入门说明](docs/ARCHITECTURE-BEGINNER.md)，再看 [架构与界面保留边界](docs/ARCHITECTURE.md)。

