# macOS IL2CPP 兼容维护说明

这些修改用于 Esports Manager 2026（Unity 6000.5.10f1、metadata 107）的 macOS x86_64 / Rosetta 兼容开发，尚未通过修改器界面的完整实机验收，不构成可直接安装的 macOS 发行包。

## 源码范围

- `Paths.cs`：识别应用包内 `Contents/Resources/Data`。
- `Il2CppInteropManager.cs`：识别 `Contents/Frameworks/GameAssembly.dylib`，保留环境变量覆盖入口。
- `IL2CPPChainloader.cs`：macOS 通过主 dispatch 队列延迟启动插件；其他平台继续使用原有 `il2cpp_runtime_invoke` 入口。
- `patches/dobby-macos-x64-inline-jump.patch`：为 macOS 测试库改用内联绝对间接跳转，避免 Rosetta 下附近跳板数据页分配失败。该补丁只用于下面的 macOS x86_64 构建，不应用到 Windows 发布依赖。

配套修改器源码位于 [EsportsManager2026Trainer](https://github.com/ASPshijiu/EsportsManager2026Trainer)。macOS 使用原生 UniTask 触发器转发界面回调，Windows 保留组件承载路径。

## 构建 BepInEx

安装 `global.json` 固定的 .NET SDK 8.0.422，在本仓库根目录执行；不要从修改器目录调用此项目，否则可能选中修改器固定的 .NET 6 SDK。

```sh
dotnet restore Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj --locked-mode
dotnet build Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj -c Release --no-restore -p:GeneratePackageOnBuild=false
```

2026-09-05 已在现有依赖上重新完成目标项目构建，0 警告、0 错误；本次没有重新验证完整发布打包流程。

## 重建 macOS Dobby 测试库

需要 macOS、Xcode Command Line Tools、Git 和 CMake。以下命令从 BepInEx 仓库根目录执行。Dobby 使用 BepInEx fork 的固定提交 `9d271803881f467aabfc24593c7dcaeb710fe861`，源码及其许可证归属该上游仓库；这里只维护所需差异。

```sh
git clone https://github.com/BepInEx/Dobby.git ../Dobby-macos
git -C ../Dobby-macos checkout --detach 9d271803881f467aabfc24593c7dcaeb710fe861
git -C ../Dobby-macos apply --check "$PWD/patches/dobby-macos-x64-inline-jump.patch"
git -C ../Dobby-macos apply "$PWD/patches/dobby-macos-x64-inline-jump.patch"
cmake -S ../Dobby-macos -B ../Dobby-macos/build-macos-x64 \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_SYSTEM_NAME=Darwin \
  -DCMAKE_SYSTEM_PROCESSOR=amd64 \
  -DCMAKE_OSX_ARCHITECTURES=x86_64 \
  -DDynamicBinaryInstrument=OFF \
  -DPlugin.SymbolResolver=OFF
cmake --build ../Dobby-macos/build-macos-x64 --target dobby --parallel 4
file ../Dobby-macos/build-macos-x64/libdobby.dylib
```

仅将生成的 `libdobby.dylib` 放入隔离 macOS 测试环境的 `BepInEx/core`。这一步不会自动组装 Doorstop、.NET 运行时、游戏 interop 或修改器插件，也不应覆盖 Windows 包中的原生库。

## 验证边界

2026-09-04 最后一次隔离游戏启动日志显示修改器原生回调桥初始化、21 个补丁类挂载、34/34 项直写检查通过以及 `Chainloader startup complete`。但 `Update` 与 `OnGUI` 的首次回调确认日志仍未出现，F8 面板没有完成验收。

当前修改器源码已将 macOS 顺序调整为先安装补丁、再添加原生触发器组件；此顺序调整后仍缺少成功的回调证据，原因尚未确定。后续应从实际消息回调和 F8 面板验证继续，不能仅凭补丁挂载统计认定修复成功。

更换电脑后使用匹配平台的游戏和 interop；macOS 测试需另备可用的 Doorstop 启动环境。使用隔离游戏副本和临时用户数据目录开展验证。Windows 实机启动、存档及 Steam Cloud 验收仍按 `METADATA_V107.md` 执行。
