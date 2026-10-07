# 框架音频与 Wwise

当前安装：Wwise 2025.1.11.9262，Unity Integration 2025.1.11.4331。
已实现 Windows Editor 接入代码；未编译、未运行播放检查、未验证 Android 或流式音乐。

## 运行前手动配置

1. 退出 Play Mode。项目配置已关闭 `Create Wwise Global` 和
   `Load Sound Engine In Edit Mode`，重启 Unity 后核对这两个开关。
2. 删除 SampleScene 中的 `WwiseGlobal` / `AkInitializer`，其他运行场景也不能有第二个初始化入口。
   框架会创建 `[AlloyFramework] WwiseAudio` 持久化对象，并原生注册默认监听器和二维发声对象。
   业务相机无需挂 `AkAudioListener`；空间监听目标通过 `AudioManager.Instance.SetListener(target)` 指定。
3. 已关闭 Wwise 的 `Generate SoundBanks As Pre Build Step` 和 `Copy SoundBanks As Pre Build Step`。
   由下方工具明确生成和导出资源，避免构建时产生另一套加载来源。
4. 重启 Unity，确保之前在编辑模式初始化的官方引擎已经退出，再从原有 Boot 启动流程运行。
   直接单独运行 SampleScene 不会执行框架启动管线。

场景、Prefab 和官方初始化设置资产未修改。按用户确认的独立包方案，Collector 已新增
AudioPackage 和 DefaultPackage 的初始化设置收集组。

## 生成与导出

在 UnityProj 目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Wwise\GenerateSoundBanks.ps1
```

其他机器可指定 `-WwiseInstallationPath`，或设置 `WWISE_INSTALLATION_PATH`。
脚本读取 `Assets/WwiseSettings.xml`，生成对应平台，再执行 `ExportAudioContent.ps1`。
已有生成结果时，可只重新导出：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Wwise\ExportAudioContent.ps1 -Platform Windows
```

两个脚本都是长期制作工具，需要保留。修改 Event、媒体、Bank、平台或语言后重新生成和导出。
只在 Wwise 中点击 Generate 而没有执行导出，会留下过期的框架清单并导致摘要校验失败。

导出器读取当前 Schema 16 / SoundBank 172 的 JSON 元数据和 ProjectInfo 的生成器版本，
生成 `AlloyAudioManifest.json`，同步运行所需文件到 `Assets/Res/WwiseAudio/<平台>`。
普通制作 JSON / TXT / 头文件留在 Wwise 输出目录，不作为运行媒体收集。
导出会覆盖本次清单引用的生成文件，不删除其他文件；Collector 应只收集清单引用的文件。

| 文件 | 用途 | 加载方式 |
| --- | --- | --- |
| Init.bnk | 工程初始化数据、Bus 等 | 引擎生命周期持有 |
| Event/*.bnk | 自动 Event Bank 的事件定义 | 按事件依赖加载 |
| Media/*.wem 及语言文件 | 驻留或流式媒体 | 共享资源组持有至原生声音退出 |
| AlloyAudioManifest.json | 事件、资源组、文件摘要和后端版本 | 安装时读取 |

首版使用自动 Event Bank。改为手动 Bank 时保持业务 Event 名称不变，
调整制作配置与元数据导出映射即可；仍需验证新的依赖关系，不支持活跃播放期间切换后端或内容版本。
当前导出器拒绝同名 Event 重复出现在多个 Bank、未知元数据版本和缺文件，防止静默加载错误资源。
复杂的跨 Bank 引用、自动 Bus Bank、External Sources、多语言切换需扩展制作映射后再接入。

## 资源交付边界

业务 → AudioManager → ResourceAudioContentProvider → ResourceManager.LoadRawFileAsync。
原始文件服务接口不暴露 YooAsset 类型；YooAssetService 是现有资源系统的一个实现。
Editor 与 Player 均初始化 AudioPackage，通过相同的资源地址与 RawFile 接口交付文件。
Editor 使用现有框架 EditorSimulate 模式，逐文件 PackRawFile 对应一个虚拟 bundle；
正式音频包使用 RawFileBuildPipeline，不再直接读取 Wwise 制作目录。

YooAsset 2.3.19 的原始文件使用独立的 `RawFileBuildPipeline`，不能仅给现有 AssetBundle
DefaultPackage 增加 PackRawFile 后宣称两种类型可以混用。已建立独立 AudioPackage。
普通 Wwise 初始化设置 `.asset` 仍属于 Unity 资产，不应放入 RawFile Collector。

现有 `Assets/Wwise/ScriptableObjects/AkWwiseInitializationSettings.asset` 已通过 DefaultPackage
的 audioSettings 组单独收集，地址为 `Config/WwiseInitializationSettings`，保留平台设置依赖。
不复制第二份 SDK 设置资产。安装器用框架 `LoadAssetAsync<ScriptableObject>` 加载并持有它，
随后关闭引擎才释放资源租约。

| 包 | Collector | 寻址 | 打包 | 过滤 |
| --- | --- | --- | --- | --- |
| AudioPackage | Assets/Res/WwiseAudio | AddressWwiseAudio | PackRawFile | CollectWwiseAudio |
| DefaultPackage | 官方 AkWwiseInitializationSettings.asset | AddressWwiseInitializationSettings | PackSeparately | CollectAll |

AudioPackage 必须配置 `RawFileIgnoreRule`，否则 `NormalIgnoreRule` 会排除 Unity 无法识别的 Bank 和媒体。`CollectWwiseAudio` 只收集当前平台清单引用的文件，旧输出与其他平台不会混入本次资源包。
正式出包前，先生成目标平台 SoundBank，再运行 `★AlloyFramework★/音频/打包音频资源包`。
该菜单以 RawFileBuildPipeline 构建 AudioPackage 并复制到 YooAsset 内置目录；
DefaultPackage 按项目原有 AssetBundle 管线构建，两包都必须交付。
Host 模式的远程 URL 提供器也需为 AudioPackage 提供对应目录，音频下载仍由框架资源系统执行。

RawFile 加载先保留资源句柄并建立文件快照。内容提供器校验 SHA256，再交付版本隔离的本地目录。
Wwise 后端使用会话独占的原生读取目录，并对共享媒体文件引用计数。
正在播放、暂停和淡出的声音继续持有依赖；正常 EndOfEvent 后才卸载媒体与 Bank。
卸载失败会保留依赖至引擎终止，禁止先删除原生引擎仍可能读取的文件。
关闭时先 Term 原生引擎，再清理目录与资源引用。

Android 的 StreamingAssets 可能位于 APK 内，原始文件入口支持 jar/file 地址，
不能直接把它们当作普通磁盘路径传给 Wwise。目前仅安装了 Windows/Mac 插件，
Android 需安装匹配 SDK、生成 Android 内容并由用户进行真机与 Streaming 验证。

## 业务调用

```csharp
using AlloyFramework.Audio;

long playID = AudioManager.Instance.PlayAudio("Play_ButtonClick");
AudioManager.Instance.StopAudio(playID);
```

默认 LoadIfNeeded，首次使用未准备事件时加载后提交。返回 ID 只代表请求已登记。
`PlayAudioAsync` 等待提交成功，`onStarted` 回调报告开始结果；都不等待整段声音结束。
已准备事件可以同步提交。RequireReady 缺依赖时立即失败，避免动作结束后补播。
Key 为空、未就绪、失效作用域等调用错误直接抛出；资源或原生提交失败产生 `[Audio]` 日志与 Failed 消息。

界面可在 OnCreate 创建 `AudioScope`，调用时指定 Scope，OnDispose 释放它。
NavigationHomeUIController 已按这个方式改为框架调用，两个按钮都调用 `Play_ButtonClick`。
启动管线会预加载该事件并持有 StartupUI 作用域，其他事件仍按首次调用加载。
预加载通过 `PreloadAudioAsync(key, scope)` 持有，scope.Dispose 释放预加载引用。

空间声音使用 `new AudioEmitter(target)` 或 PlayOptions.Position，二者不能同时指定。
可复用 emitter 的 Switch 状态保留；不再使用时调用 emitter.Dispose，原生声音退出后注销。
音量/RTPC 参数名、数值范围与单位由项目制作侧约定，框架不创建未经确认的 MasterVolume 参数。
同对象多次播放使用 PlayingID 级 RTPC，不能误改另一个实例。

## 人工验证与错误日志

从 Boot 启动后应看到：

- `[Audio/Wwise] 默认监听器已注册，框架持久化对象已创建。`
- `[Audio] 引擎与 Init 就绪：WwiseAudioBackend`
- `[GameStartup] 完成：预加载按钮事件`

点击首页两个按钮，确认声音正常，切换页面不会创建第二份音频引擎。
若失败，复制首条 `[Audio]` / `[Audio/Wwise]` 错误和紧邻的 Wwise 错误，不需要开启调试器。
诊断菜单 `★AlloyFramework★/音频/音频调试面板` 可查看组引用、Key、scope、框架 ID、
Wwise PlayingID、状态和近期错误，并手动暂停/恢复/停止。
Started 仅表示引擎接受提交，不是扬声器输出延迟测量。
EndOfEvent 不提供自然结束与引擎抢占的区别；未由框架主动停止的终态原因显示 Unknown。
框架主动停止、作用域释放和关闭分别显示 Stopped、ScopeDisposed、Shutdown。

后续由用户验证：连播与精确停止；加载中取消与缺文件；循环暂停与 timeScale=0；
共享引用与场景切换；真机 RawFile 流式音乐；退出与重入无重复通知。
未执行编译、自动化测试、运行播放或真机验证。

## 提交内容

提交框架与业务代码、工具、文档、Wwise 源工程、Originals、官方集成和原生插件、配置及稳定 `.meta`。
不提交 SoundBank 输出、转换缓存、RawFile 收集源、个人设置、离线文档与调试符号。
`UnityProj/.gitignore` 已忽略新的 WwiseAudio 生成目录。
不要忽略全部 Assets/Wwise 或全部 DLL，否则其他机器无法使用 SDK。

SDK 生命周期与 Bank API 按本机安装源码核实；原始文件接口按已安装 YooAsset 2.3.19 源码核实。
参考：[Audiokinetic SoundBank 说明](https://www.audiokinetic.com/library/edge/?id=soundengine_banks_general.html&source=SDK)。

## 代码归属

- `Runtime/Framework/Audio`：通用音频管理、作用域、内容租约及资源提供器。
- `Runtime/AudioWwise`：独立程序集 `Alloy.Audio.Wwise`，包含原生后端、持久化宿主和安装器。
- `Editor/Audio`：音频状态与控制面板，保留独立的 `Alloy.Audio.Editor` 程序集。
- `Editor/YooAsset/Audio`：音频地址规则、清单收集规则及原始音频包构建工具。
- `Tools~/Wwise`：SoundBank 生成及音频依赖清单导出实现，调用时显式传入 `-UnityProjectRoot`。

Wwise 接入模块依赖项目安装的 `AK.Wwise.Unity.API`。官方 Wwise SDK、原生插件、初始化设置资产、
Wwise 源工程和音频源文件仍由项目维护。

项目 `GameAudioInstaller` 仅配置包名、内容地址根目录和 SDK 设置地址，调用框架
`WwiseAudioInstaller.InstallAsync`。项目保留业务启动预加载、业务事件调用和 Collector 资产。
原有 `Tools/Wwise` 命令入口保留为薄包装，按照 `Packages/manifest.json` 定位框架工具。
框架与项目两个仓库的迁移改动需要一起提交，原有脚本及程序集的 `.meta` GUID 保持不变。

修正收集规则后，EditorSimulate 退出再进入运行模式即可刷新清单。
Offline/Host 必须重新构建并交付 AudioPackage，旧包清单不会因修改 Collector 自动更新。