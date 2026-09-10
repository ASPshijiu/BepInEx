# macOS IL2CPP 兼容维护说明

适用范围：Esports Manager 2026，Steam build 25134229，Unity 6000.5.10f1，metadata 107，macOS x86_64 / Apple Silicon Rosetta。下面的补丁用于单独构建 macOS 依赖，不改动 Windows 发布包的依赖锁，也不构成完整的 macOS 安装包。

## 2026-09-10 实机结果

隔离游戏副本已实际验证：

- `Update`、`OnGUI` 首次回调确认日志均出现；F8 能打开、关闭修改器面板。
- Unity 原生 `Button.onClick` / `UnityEvent` 调用能够切换设置页、切换中英文、执行关闭按钮；界面变化已通过截图核对。
- 加载日志记录 24 个补丁类挂载、46/46 项直写接口检查通过。它们不是全部功能的实测结果。
- 两项独立原生回归测试通过：Rosetta 执行已预热函数的跳转与撤销、代码空洞分配不覆盖已有跳板。两项均已在旧实现上复现失败。

测试在主菜单进行，没有加载或修改经营存档。物理鼠标点击、拖动、输入框、经营数据写入与比赛结算尚未完成本轮验收。自动化鼠标测试中，Unity 的 `Input.mousePosition` 固定不变，自动点击同样不能操作游戏原生菜单；上述按钮验证通过临时测试入口调用原生 UnityEvent 完成，不能宣称鼠标点击已通过。临时测试入口和诊断代码不属于正式插件。

## 修复内容

BepInEx 仓库内已有的 macOS 适配包括应用包数据目录识别、`GameAssembly.dylib` 路径识别，以及通过主 dispatch 队列延迟启动插件。

`patches/dobby-macos-x64-inline-jump.patch` 现在包含三处配套修复：

1. 使用 14 字节内联绝对跳转，避免为跳转目标单独分配附近的数据页。
2. x64 Darwin 直接写入目标映射并恢复执行权限。旧 `mach_vm_remap` 写法能读到修改后的字节，但 Rosetta 仍会执行旧翻译；独立复现中，目标函数返回原值且没有进入钩子。
3. 为代码空洞记录完整分配范围。旧实现再次搜索连续零字节时，会把跳板地址末尾的零字节及填充误认为空闲空间。实机调试捕获到后一个跳板覆盖前一个返回地址，随后跳向损坏地址并崩溃。

`patches/il2cppinterop-macos-unity6000.5.patch` 基于 Il2CppInterop `81a6f78c8b653e0da4a3420ac4cd00819e8b6292`：

- 保留 [PR #273](https://github.com/BepInEx/Il2CppInterop/pull/273) 的 macOS 模块发现和 xref 基址适配，同时保留该基线在 Windows 上的可读区域扫描。
- 回移 [v2 rewrite 的 Class_105_0 定义](https://github.com/ds5678/Il2CppInterop/blob/f46e88d0e8a5515ed7f60938aba3de5339c752fb/Il2CppInterop.Runtime/Structs/VersionSpecific/Class/Class_105_0.cs)，使稳定版 Unity 6000.5 的 methods、interfaces、typeHierarchy 等字段位置匹配。仅适配 1.x 包装接口，没有引入整个 v2 分支。
- 修正类型层级复制的越界：只复制父类型实际拥有的项，再追加新类型。

修复后 `Il2CppToMonoDelegateReference` 注册成功，原先创建界面事件时报出的 `ClassInjector.RegisterTypeInIl2Cpp` 空引用错误不再出现。这是定制构建，不是上游已发布的新稳定版本。

## 构建 BepInEx

使用 `global.json` 固定的 .NET SDK 8.0.422，在本仓库根目录执行：

```sh
dotnet restore Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj --locked-mode
dotnet build Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj -c Release --no-restore -p:GeneratePackageOnBuild=false
```

采用目标项目实际输出的 DLL，避免与仓库其他输出目录中的旧文件混用。修改器源码位于 [EsportsManager2026Trainer](https://github.com/ASPshijiu/EsportsManager2026Trainer)。macOS 使用原生 UniTask 触发器驱动界面，Windows 保留组件承载路径。

## 构建 macOS Dobby

需要 Xcode Command Line Tools、CMake 和 Git。以下命令从本仓库根目录执行，目标为新的干净检出；已应用旧补丁的目录先保留本地修改，不能重复叠加补丁。

```sh
git clone https://github.com/BepInEx/Dobby.git ../Dobby-macos
git -C ../Dobby-macos checkout --detach 9d271803881f467aabfc24593c7dcaeb710fe861
git -C ../Dobby-macos apply --check "$PWD/patches/dobby-macos-x64-inline-jump.patch"
git -C ../Dobby-macos apply "$PWD/patches/dobby-macos-x64-inline-jump.patch"
cmake -S ../Dobby-macos -B ../Dobby-macos/build-macos-x64 \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_SYSTEM_NAME=Darwin \
  -DCMAKE_SYSTEM_PROCESSOR=amd64 -DCMAKE_OSX_ARCHITECTURES=x86_64 \
  -DDynamicBinaryInstrument=OFF -DPlugin.SymbolResolver=OFF
cmake --build ../Dobby-macos/build-macos-x64 --target dobby --parallel 4
tests/macos/run-dobby-tests.sh ../Dobby-macos
```

两项测试均应输出 `PASS` 并以 0 退出。在 Apple Silicon 上，测试程序以 x86_64 编译并由 Rosetta 运行。只将生成的 `libdobby.dylib` 放入 macOS 测试环境的 `BepInEx/core`。

## 构建 Il2CppInterop 兼容依赖

```sh
git clone https://github.com/BepInEx/Il2CppInterop.git ../Il2CppInterop-macos
git -C ../Il2CppInterop-macos checkout --detach 81a6f78c8b653e0da4a3420ac4cd00819e8b6292
git -C ../Il2CppInterop-macos apply --check "$PWD/patches/il2cppinterop-macos-unity6000.5.patch"
git -C ../Il2CppInterop-macos apply "$PWD/patches/il2cppinterop-macos-unity6000.5.patch"
cd ../Il2CppInterop-macos
dotnet build Il2CppInterop.Runtime/Il2CppInterop.Runtime.csproj -c Release -p:GeneratePackageOnBuild=false
```

将 `bin/Il2CppInterop.Runtime/net6.0` 中的 `Il2CppInterop.Runtime.dll` 和 `Il2CppInterop.Common.dll` 一起部署到隔离环境的 `BepInEx/core`。其他程序集仍使用匹配的 1.5.3 基线，不能直接混入 v2 Runtime。当前构建成功，但存在上游的可空性等编译警告。

Dobby 和 Il2CppInterop 的源码、许可证与贡献归属各自上游仓库；这里维护差异补丁，构建或分发时应同时保留来源和许可证信息。

## 游戏 interop 与启动

macOS 必须针对本平台、本游戏构建重新生成 interop，不能使用 Windows 完整包中的方法地址缓存。本次生成 168 个程序集，采用以下分阶段流程：

1. 在游戏副本中把 Universal `GameAssembly.dylib` 提取为 x86_64 单架构文件，规避当前 Cpp2IL 对 Universal Mach-O chained fixups 使用只读流的问题。保留原始 Universal 文件，不修改 Steam 安装目录。
2. 由匹配 metadata 107 的 Cpp2IL 输出 dummy 程序集。
3. 在独立进程中运行 Il2CppInterop CLI `generate`，使用这些 dummy、匹配的 Unity 基础库及同一个 macOS GameAssembly。这样避免 Cpp2IL 与 Generator 的不同 AsmResolver 版本在同一进程中冲突。
4. 部署生成的 interop 后再关闭启动时自动重建。不能只修改 `assembly-hash.txt` 掩盖过期文件。

启动必须明确选择 x86_64，并同时使用 x86_64 Doorstop、dlsym interposer 和 CoreCLR。直接打开 Universal 游戏应用可能启动 arm64，无法加载这些 x86_64 依赖。完整包组装、真实鼠标操作及存档验收仍需单独完成。
