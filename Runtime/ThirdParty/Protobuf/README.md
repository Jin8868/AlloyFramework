# Protobuf 运行库

来源：同电脑 `D:/WorkSpeace/UnityProject/Character/Assets/Plugins/protobuf` 的既有插件，
原文件和导入设置沿用，统一随框架放在 `Runtime/ThirdParty/Protobuf`，游戏 Plugins 不再保留副本。

已读取 Google.Protobuf 程序集标识 `3.34.1.0`，目标框架 `.NETStandard,Version=v2.0`。
它实际引用 System.Memory 4.0.1.1、System.Buffers 4.0.2.0、
System.Runtime.CompilerServices.Unsafe 4.0.4.1 和 netstandard 2.0。
官方 Protobuf 许可证见 `LICENSE.txt`。三个既有系统依赖的 NuGet 来源与许可证见
`DependencyLicenses.md`；没有替换 DLL，需按其实际包来源进一步核对。

`Alloy.Runtime` 不引用 Google.Protobuf；可选 `Alloy.Net.Protobuf`、`Alloy.Net.Editor` 与
`Game.Protocol` 使用明确的预编译引用。游戏业务程序集保留原有的 Auto Reference 策略，
避免改变 Wwise 等既有依赖；没有擅自关闭这些插件的 Auto Reference。

可选模块通过 `ALLOY_PROTOBUF` 程序集条件启用；本项目已为 Standalone、Android 启用。
这些 DLL 随框架提供；复用框架时按需启用该宏。若在本游戏移除该功能，还应同时从
`Game.Runtime.asmdef` 移除 Game.Protocol 和 Alloy.Net.Protobuf 引用。

用户需要检查 Plugin Inspector 的 Editor/目标平台设置、Validate References，
验证没有与 Unity 系统程序集冲突，并完成 Android ARM64 IL2CPP 编译和裁剪验证。
本次没有修改 DLL 的 .meta，没有执行编译或序列化测试。
