# 无限列表

框架组件目录为 `Runtime/Framework/UI/Components/UIInfiniteList/`。
首版支持横向或纵向固定尺寸单列虚拟化，不提供循环首尾、分页、多模板或异形布局。

## 配置

1. 使用 UIInfiniteList 替代 ScrollRect，同一节点不要同时挂两个组件。
2. 配置 Viewport（建议 RectMask2D）和直接位于它下面的 Content，只启用一个滚动轴。
3. Content 不启用 LayoutGroup 或 ContentSizeFitter；列表统一管理其左上锚点、尺寸和位置。
4. 设置 ItemSize、Spacing、Padding、Buffer。运行时修改后调用 Rebuild。
5. Item 预制体根节点挂业务 UIListItem 子类。模板使用预制体资源，不放在 Content 内。
6. 列表、Content 和 Item 的布局根节点使用单位缩放、无旋转；装饰变换放在子节点。


## 创建菜单与模板选择

通过层级右键菜单或顶部 GameObject 菜单选择 `★AlloyFramework★/UI/无限列表`。
自动创建 `list_New / Viewport / Content`，配置裁剪、纵向滚动和 400×100 的 Item 尺寸。
在已有 Canvas 下创建时沿用父级；否则新增 Canvas。交互使用项目已有 EventSystem。
整个创建操作支持一次撤销。

默认推荐在 Inspector 的“默认 Item 预制体”中拖入预制体资源，
其根节点需要挂载业务 UIListItem 子类，然后调用 `list.BindData(items)`。

需要动态选择模板时，先通过业务资源流程取得预制体，再调用 `list.Bind(prefab, items)`。
显式模板仅用于本次绑定，不修改 Inspector 中的默认引用；
后续 `BindData(items)` 会重新使用默认模板，`Refresh()` 则继续使用当前绑定模板。
列表不会自动加载或卸载模板资源，动态资源句柄仍由调用方管理。
直接引用会成为界面预制体的资源依赖，适合模板固定且生命周期一致的界面。
## 业务接口

- BindData(IList items)：使用 Inspector 配置的默认模板。
- Bind(UIListItem prefab, IList items)，或 Bind(GameObject prefab, IList items)：保留集合引用，回到起点。
- Refresh()：重新读取数量、刷新全部活动项，保留合法的滚动距离。变更集合后调用。
- Rebuild()：重算布局，保留可复用实例。视口尺寸变化时也会自动重建。
- ScrollToIndex(index, alignment, animated, duration)：Start / Center / End 对齐。
- ScrollToPos(position, animated, duration)：从顶部或左侧起算的本地 UI 距离，非归一化值。
- Clear(releasePool)：清除数据与显示，默认同时销毁缓存。

例如 `list.ScrollToIndex(49, EUIListAlignment.Center, true, 0.3f)`。
立即定位为默认行为，非正动画时间同样立即定位。动画使用非缩放时间，新请求、
拖拽、滚轮、重建、隐藏都会中止旧动画；所有终点受内容边界限制。
数据缩减时保持距离并限制到边界，不提供稳定 ID 锚点恢复。

## Item 生命周期

UIListItem 提供只读 Data、Index、RectTransform。
业务继承并实现 OnBind，在其中将 Data 转换为业务类型，完整覆盖展示状态。
BindData 不接收展示回调；内部 BindItem 负责解绑和更新 Data、Index，再调用 OnBind。
OnBind 是唯一的数据展示入口，列表批次事件仅用于生命周期通知与协调。

- OnInitialize：实例创建后一次性初始化；不依赖 Awake/Start 已执行。
- OnBind：Data 和 Index 已赋值；初始化和回收后的绑定均走此入口。
- OnBeforeDisplay：本批次数据及同步内部布局完成，提交显示前。
- OnUnbind：旧 Data、Index 仍可读取；取消异步操作并解除订阅。
- OnDispose：销毁前释放资源，执行一次。

刷新已有 Item 同样先解绑再绑定；仅更新位置不重新绑定。
点击监听使用命名方法并读取当前 Data，避免捕获历史索引。
异步图片必须在解绑时取消，并检查绑定版本，绑定完成不代表异步资源完成。

## 批次事件

顺序：补齐实例和初始化 → ItemsCreated（有新增时）→ 布局定位和绑定 →
ItemsBound → 激活并完成内部同步布局 → Item.OnBeforeDisplay →
BeforeDisplay → RefreshCompleted。

“所有 Item”指本次视口及缓冲区需要的实例，不是整个数据集合。
BeforeDisplay 不是每帧 GPU 或 Canvas 渲染回调，也不保证异步资源就绪。
回调处于同一同步更新内，禁止在回调里调用 BindData / Bind / Refresh / Rebuild / ScrollTo / Clear；
需要二次更新时由业务安排到后续帧。数据集合也不得在回调中修改。

上下文包含 List、Reason、CreatedCount、Items。Items 是只读实时视图，仅在同步回调内读取；
ItemsCreated 阶段尚未全部绑定数据。空数据更新也触发完成事件。
滚动但可见集合不变时不触发批次事件。生命周期异常会回收半绑定实例并暂停自动更新，
业务修复后可调用 Refresh 恢复。

所有实例直接创建并保留在 Content 下；回收时解绑并隐藏，复用时重新绑定并显示，不执行 SetParent。
空闲实例仍受缓存上限约束，超出上限时销毁。隐藏界面时解绑活动项，再次显示重新绑定；销毁时清理缓存。
若模板根节点为激活状态，首次 Instantiate 时 Unity 的 Awake/OnEnable 可能先于框架 OnInitialize 执行；
数据展示放在 OnBind 中，不依赖 Unity OnEnable 已取得业务数据。
模板资源由调用方持有，在列表所有实例释放后才可卸载。

## 扩展

RebuildLayout、GetItemRect、CollectVisibleIndices 为布局扩展点。
矩形坐标统一从内容左上角起算，向右、向下为正；索引查询允许非连续结果，
但必须合法且不重复。异形列表后续继承 UIInfiniteList 扩展，当前池只支持单模板。
## 自动绑定 UI 引用

节点使用 `list_` 前缀，例如 `list_Bag`。
通过现有 UI 引用生成工具生成后，会得到 `UIInfiniteList` 类型的 `ListBag` 属性，
对应私有字段为 `m_listBag`。节点必须挂有 `UIInfiniteList` 组件，否则生成器报告缺失组件。
普通 `ScrollRect` 继续使用 `scroll_` 前缀。
## 异形布局扩展

不等尺寸、多模板和自定义路径布局使用 UIIrregularList，见 [异形列表说明](UIIrregularList.md)。
