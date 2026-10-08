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

打开 Wwise 工程，点击 `Generate Checked` 或 `Generate All`。当前项目已配置 Windows 平台的
Post-Generation Step，生成结束后自动调用项目 `ExportAudioContent.ps1`，更新清单与收集源。
生成日志应出现“框架音频清单已导出”和“RawFile 收集目录”。导出失败时先修复错误再运行游戏。

项目手动生成入口 `Tools/Wwise/GenerateSoundBanks.ps1` 已删除。
框架 `Tools~/Wwise/GenerateSoundBanks.ps1` 仍保留为可复用的命令行工具，需显式传入 Unity 项目目录。
项目 `ExportAudioContent.ps1` 和 `ResolveFrameworkTools.ps1` 保留，生成后命令依赖这两个入口。

已有生成结果时，可在 UnityProj 目录只重新导出：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Wwise\ExportAudioContent.ps1 -Platform Windows
```

修改 Event、媒体、Bank、平台或语言后重新生成。EditorSimulate 退出再运行即可刷新清单；
Offline/Host 仍需在 Unity 中执行“打包音频资源包”并交付新包。

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
启动按钮预加载示例已删除；事件按首次调用加载。
预加载通过 `PreloadAudioAsync(key, scope)` 持有，scope.Dispose 释放预加载引用。

空间声音使用 `new AudioEmitter(target)` 或 PlayOptions.Position，二者不能同时指定。
可复用 emitter 的 Switch 状态保留；不再使用时调用 emitter.Dispose，原生声音退出后注销。
音量接口统一使用 0～1；框架音量参数及总线路由约定见下方音量控制说明。其他 RTPC 的参数名称、范围和单位仍由制作侧约定。
同对象多次播放使用 PlayingID 级 RTPC，不能误改另一个实例。

## 人工验证与错误日志

从 Boot 启动后应看到：

- `[Audio/Wwise] 默认监听器已注册，框架持久化对象已创建。`
- `[Audio] 引擎与 Init 就绪：WwiseAudioBackend`
- `[GameStartup] 完成：初始化框架音频与默认监听器`

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
## 音量控制

框架提供总音量、BGM、音效和单次播放四种控制，不调用 PlayerPrefs 或存档系统。
分类音量初始值为 1，在当前进程中保存设置值；原生后端接受设置后才更新框架值。
未初始化或后端拒绝操作时返回失败结果并打印 `[Audio/Volume]`，设置值保持不变。
音量参数只允许 0～1 的有限值，越界、NaN、Infinity 会抛出 ArgumentOutOfRangeException。
获取接口返回设置值，不查询实时响度。

```csharp
AudioManager audio = AudioManager.Instance;
audio.SetMasterVolume(0.8f);
audio.SetBGMVolume(0.5f);
audio.SetSFXVolume(0.7f);
float masterVolume = audio.GetMasterVolume();
float bgmVolume = audio.GetBGMVolume();
float sfxVolume = audio.GetSFXVolume();

long playID = audio.PlayAudio("Play_ButtonClick", new AudioPlayOptions { Volume = 0.6f });
audio.SetVolume(playID, 0.3f);
if (audio.TryGetVolume(playID, out float volume))
{
    // 仅活跃播放可查询单次音量，声音结束后 TryGetVolume 返回 false。
}
```

加载中的请求也可以设置单次音量，提交事件时应用最后一次设置值。
已提交的单次音量通过 Wwise PlayingID 设置，不改变同一个 GameObject 上的其他声音。
音量控制与资源准备、暂停、停止和作用域回收分别管理；调小音量不会主动释放资源组。

Wwise 配置包含四个 Game Parameter，范围均为 -200～0 dB，默认值为 0 dB：

| 参数 | 绑定目标 |
| --- | --- |
| AlloyMasterVolume | Main Audio Bus 的 BusVolume |
| AlloyBGMVolume | BGM 总线的 BusVolume |
| AlloySFXVolume | SFX 总线的 BusVolume |
| AlloyInstanceVolume | AlloyBGM / AlloySFX 声音分组的 Voice Volume |

后端把归一化线性音量转换为 `20 * log10(volume)`，0 映射到 -200 dB。
实际增益同时受到总音量、分类音量、单次音量和制作侧混音设置影响。
这四个参数由音量接口管理，请不要再通过通用 SetParameter 接口修改它们。

现有 ButtonClick 已放入 Containers 的 AlloySFX 分组，路由到 SFX 总线，声音 GUID 保持不变。
后续普通音效放入 AlloySFX，普通背景音乐放入 AlloyBGM，即可继承单次音量曲线及输出路由。
独立的交互音乐层级或自行覆盖继承设置的声音，需在其根对象绑定 AlloyInstanceVolume 的
Voice Volume 曲线，并将输出路由到对应总线。分类依据来自 Wwise 路由，不依靠 Event 名称猜测。

本次修改了 Wwise 源配置：重新加载 Wwise 工程后执行 Generate All，自动导出完成后再启动 Unity。
Offline/Host 还需要重建 AudioPackage。框架代码、Wwise 配置和新 SoundBank 应使用同一版本。
音频面板新增三种分类滑条及活跃实例滑条，可用于用户手动检查；未自动运行编译或播放检查。
若失败，请提供首条 `[Audio/Volume]` 或音频初始化异常，以及紧邻的 Wwise 错误。