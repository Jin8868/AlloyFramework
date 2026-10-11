# 既有系统依赖说明

本次沿用 Character 项目中的三个 DLL，没有重新下载或更改这些二进制文件。
程序集版本不能直接当成 NuGet 包版本；原包来源尚需由原插件提供者核对。

| DLL | 程序集版本 | 官方包来源 |
| --- | --- | --- |
| System.Buffers.dll | 4.0.2.0 | https://www.nuget.org/packages/System.Buffers |
| System.Memory.dll | 4.0.1.1 | https://www.nuget.org/packages/System.Memory |
| System.Runtime.CompilerServices.Unsafe.dll | 4.0.4.1 | https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe |

这些组件来自 Microsoft .NET 的 System 库，源码许可证参考
[dotnet/corefx MIT 许可证](https://github.com/dotnet/corefx/blob/master/LICENSE.TXT)。
本记录不声称已经定位原 DLL 对应的确切 NuGet 包版本，也不改动其导入策略。
