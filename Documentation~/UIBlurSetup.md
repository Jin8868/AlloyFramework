# UI 背景模糊接入说明

## 实现范围

- UI 核心位于 `Runtime/Framework/UI/Blur`，不依赖 URP 类型。
- URP 14 后端位于 `Runtime/UIBlurURP`，独立程序集 `Alloy.UIBlur.URP`。
- `UIDefinition.BackgroundMode == UIBackgroundMode.Blur` 自动启用背景模糊。
- 默认实时更新，支持业务切换静态模式。
- 包含场景及下层 UI，同层按框架维护的打开及置顶顺序判定。
- 当前模糊界面及上层 UI 保持清晰；只运行最高模糊边界的一套 GPU 模糊。
- 等待首张纹理后才播放开场动画；关闭动画期间保持模糊，最后一个模糊界面关闭后释放资源。
- 输入规则保持独立：模糊 RawImage 不拦截输入，黑色遮罩由具体界面自行添加。

## 必须手动完成的 Unity 设置

AI 没有改写任何预制体、场景、Renderer 资产或 TagManager。

1. 在 `Project Settings > Tags and Layers` 中创建 `UIBlurForeground` Layer。
   默认背景使用 Unity 内置的 `UI` Layer。这两个 Layer 必须专用于框架 UI，不能用于场景模型。
2. 选中 UICamera 实际使用的 URP Universal Renderer Data。
   当前项目默认为 `Assets/Settings/UniversalRenderer.asset`。
   在 Add Renderer Feature 中添加一次 `UIBlurRendererFeature`，保持启用。
3. 给 Feature 的 Blur Shader 字段赋值：
   `Packages/com.alloy.framework/Runtime/UIBlurURP/UIBlur.shader`。
   Shader 名称为 `AlloyFramework/UI/BackgroundBlur`。必须显式引用，不能依赖 Shader.Find，避免构建裁剪。
4. 业务场景必须有启用且标记为 `MainCamera` 的 URP Base 相机，使用支持 Camera Stacking 的 Universal Renderer。
   框架后端在运行时将持久化 UICamera 接入它的 Stack 并设为 Overlay，不需要把运行时预制体引用写入场景。
   Boot 或没有场景 MainCamera 时保留独立 UI 渲染，但此时不能打开需要完整场景模糊的界面。
5. 八层 Canvas 使用与 WindowRoot 相同的 Sorting Layer，默认都为 Default。
   Source UICamera 使用全屏 Viewport、Target Display 0，关闭 Dynamic Resolution，不能设置 TargetTexture。
6. 在 UI 生成面板把需要模糊的界面背景选为 Blur，并重新生成定义；已有定义为 None 的界面不会改变行为。

使用自定义 UICamera Renderer 时，配置 `UIBlurSettings.URPRendererIndex` 为它在 URP Asset 的 Renderer List 索引。
默认 `-1` 表示使用项目默认 Renderer。前景和源 UICamera 的 Renderer 必须一致。

## 两个独立根画布与固定相机

预制体结构如下，ForegroundRoot 与 WindowRoot 必须平级：

```text
UIRoot
├─ CameraRoot
│  ├─ UICamera
│  └─ BlurForegroundCamera
├─ WindowRoot（Canvas、CanvasScaler、原八层节点、唯一 EventSystem）
└─ ForegroundRoot（RectTransform、Canvas、CanvasScaler，初始无业务子节点）
```

- ForegroundRoot 的 Canvas 使用 Screen Space - Camera，绑定 BlurForegroundCamera。
- 前景相机 GameObject 保持激活，Camera 组件可以禁用；框架启动时会禁用，在模糊期间启用。
- 前景相机使用 Overlay，与 UICamera 的 Renderer 相同；只勾选 UIBlurForeground Layer。
- 开启 Clear Depth，关闭 Post Processing、Render Shadows、Occlusion Culling；不要添加 AudioListener。
- 投影与 Transform 由框架跟随 UICamera，前景 CanvasScaler 跟随 WindowRoot 的参考分辨率和缩放。
- 不复制 EventSystem 或业务界面；不创建逻辑排序占位节点；普通全屏层不创建额外前景层容器。
- 前景 UI 实例临时进入 ForegroundRoot，UIEntry.DisplayOrder 保留逻辑顺序。
  同层比较、置顶、缓存重开及最后一个 Blur 关闭时恢复原归属，绑定引用保持有效。
- SafeArea 等适配组件仍处于同一个 UIRoot 下，普通全屏层的实例直接进入 ForegroundRoot；存在自定义 Content、非全屏布局或 CanvasGroup 时，
  才镜像祖先路径的 RectTransform、激活状态和 CanvasGroup。
- 不创建、加载或销毁前景相机；最后一个 Blur 关闭后移出相机栈、禁用相机并释放模糊纹理。
- 业务自行放进 ForegroundRoot 的非框架 UI 不受自动路由管理，请保持该节点初始为空。

不能只勾选 UICamera 的 UIBlurForeground 作为修复，否则当前界面可能进入背景捕获。
仅改变嵌套 Canvas 的 Layer 并不足以完成独立 Screen Space - Camera 根画布的路由。

## 业务配置

在 UIManager 初始化完成后调用，通常放在业务框架扩展安装步骤中：

```csharp
UIManager.Instance.ConfigureBlur(new UIBlurSettings
{
    UpdateMode = EUIBlurUpdateMode.Realtime,
    Downsample = 4,
    Iterations = 2,
    Radius = 1f
});
```

低性能设备的降级配置：

```csharp
UIManager.Instance.ConfigureBlur(new UIBlurSettings
{
    UpdateMode = EUIBlurUpdateMode.Static,
    Downsample = 4,
    Iterations = 1,
    Radius = 1f
});
```

支持在界面打开期间切换更新模式和质量，会请求新纹理。
已有模糊界面时不允许修改前景和背景 Layer 名称。
该接口不会自动判断设备档位；设备分级由业务决定。

## 实时与静态行为

实时模式每个源相机渲染帧降采样并执行横向、纵向高斯模糊。
静态模式只在首次打开、最高模糊边界变更、配置变更或渲染尺寸/格式变化时重新捕获。

静态模式冻结的是显示纹理，并不自动停止下层 Controller、动画、场景逻辑或网络请求。
已有模糊界面跨场景、暂时没有 Base 相机时保留上一张纹理并暂停捕获；新的 MainCamera 就绪后重新捕获。
没有任何历史纹理时仍要求场景 Base 相机就绪，不能用空画面冒充完整背景。
多弹窗关闭最高界面后，以剩余最高模糊界面为新边界重新捕获，不保存每个弹窗独立的历史截图。
返回前一个弹窗时看到的是当前背景的快照。

## 渲染过程

```text
场景 Base MainCamera
    ↓
原 UICamera：场景颜色上叠加背景/下层 UI
    ↓ AfterRenderingTransparents
复制到低分辨率纹理 → 横向高斯 → 纵向高斯
    ↓
预制体前景 Overlay 相机（ForegroundRoot）：全屏模糊 RawImage + 当前界面 + 上层 UI
```

不使用 ScreenCapture CPU 读回，不通过额外相机重复渲染 3D 场景。
前景相机只绘制前景专用 Layer。捕获排除模糊 RawImage 自身，避免画面递归反馈。

模糊期间框架会临时为每个管理的 UI 根节点补充独立 Canvas/GraphicRaycaster，并统一临时排序，
使同一个 HUD/WINDOW/POPUP 层内的前后实例也能分离到两台 UI 相机。
动态 Item 每帧由统一 LateUpdate 递归路由到所在实例的前景或背景 Layer。
停止模糊后恢复原 Layer、Canvas 排序和相机掩码。
补充的 Canvas/GraphicRaycaster 随 UI 实例复用，直到实例销毁时释放，避免快速关闭重开与延迟 Destroy 冲突。
这会保留独立 Canvas 批次；复杂界面需同时观察普通模式下的 Draw Call。

## 层级节点优化

- 不创建 BlurSlot：打开和置顶顺序由 UIEntry.DisplayOrder 保存，返回原层时统一恢复物理顺序。
- 普通全屏层直接使用 ForegroundRoot；只有自定义布局或 CanvasGroup 才按需创建 BlurLayer。
- BlurBackground 在框架生命周期内只创建一次，关闭模糊时隐藏并清空纹理，下一次打开复用。
- 无模糊会话时不反复排序原层；活跃会话复用排序缓冲和比较器。

## 性能与限制

- 默认工作纹理宽高各为源颜色纹理的四分之一，模糊像素数量为原来的 1/16。
- 两张 GPU 工作纹理复用，分辨率/质量变更才重新分配。
- 无模糊请求时不分配模糊材质和 RenderTexture，前景相机禁用且移出相机栈，也不执行模糊 Render Pass。
- URP 可能因为颜色读取要求增加中间颜色纹理/颜色复制，成本不只有两张工作纹理。
- GPU Profiler 标记为 `Alloy UI Blur`，需在目标设备实测。
- 为覆盖运行时动态 Item，活动模糊期间每帧扫描 UI 层级；已稳定节点不重复写入 Layer，
  缓冲列表和字典复用。新增节点会产生初次记录开销，复杂列表需要观察 CPU 成本。
- 同一模糊期间每层管理实例数限制为 500，避免临时排序值跨层。
- 层级/Content 容器的 Mask、RectMask2D 及直接布局 UI 根节点的 LayoutGroup 暂不镜像；
  这些组件应放在 UI 预制体内部随实例移动。
- 界面内部子 Canvas 必须继承排序，不支持业务子 Canvas 自行 overrideSorting 穿越其他 UI 层。
- 额外的非框架 UI、Screen Space - Overlay Canvas、位于 UICamera 之后的其他相机内容不在捕获范围内。
- 当前后端仅支持 URP 14 Universal Renderer，不支持 XR、分屏、多显示器、TargetTexture 或动态分辨率。
  URP Asset 的普通 Render Scale 变化可重新分配纹理。
- Render Feature 在背景 UI 透明物体之后捕获，UICamera 请关闭后处理，避免源和前景重复后处理。
- 模糊输出是完整不透明背景纹理，Radius/Iterations 控制模糊程度，不提供 Dim 或自动黑色遮罩。
- 相机存在外部手动掩码修改时，应避免在活动模糊期间改动同一相机的掩码。

## 最小手动验收

1. 下层 UI 播放位移动画，场景中放一个转动物体；打开 Blur 弹窗，两者继续运动且模糊，弹窗清晰。
2. 开启弹窗开场/关闭动画，确认开场前没有自身进入截图，关闭动画结束前背景模糊仍存在。
3. 切 Static，背景视觉静止，弹窗动画仍正常；切回 Realtime 后背景继续变化。
4. 同层依次打开两个 Blur 弹窗，只运行一套模糊，关闭顶层后下层保持清晰且背景重新捕获。
5. 模糊界面上再显示 Toast，Toast 清晰；模糊界面下的 Toast 按真实排序进入捕获。
6. 动态创建和移除 Item，确认它出现在正确相机，没有清晰漏出或递归模糊。
7. 修改 Game View 尺寸及 URP Render Scale，确认静态模式也重新捕获，不拉伸旧纹理。
8. 取消首次打开、移除 Feature、清空 Shader 引用，确认报错清晰、前景相机禁用并清理模糊纹理。
9. 使用 HideOnClose 缓存重开，确认重新捕获、正常点击，没有重复 Canvas/GraphicRaycaster。
10. 关闭全部模糊界面和关闭框架，确认相机栈、Layer、排序、RenderTexture 正确恢复和释放。
11. 从一个业务场景切换到另一个 MainCamera 场景，确认持久化 UICamera 被接入新栈。
12. 在目标低性能设备分别测 Realtime/Static 的 CPU、GPU 和显存成本。

编译、Shader 编译及真机验收由用户执行。此次实现不自动修改项目资产，也不运行编译或测试。
