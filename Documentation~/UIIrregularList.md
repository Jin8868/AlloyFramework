# 异形无限列表

UIIrregularList 继承 UIInfiniteList，保留 BindData、Refresh、Rebuild、ScrollToIndex、
ScrollToPos 和批次生命周期。UIListItem.OnBind 仍是唯一的数据展示入口。

## 创建与接入

1. 使用 GameObject/★AlloyFramework★/UI/异形无限列表。
2. 自动生成 rlist_New / Viewport / Content，并挂载 UISequentialListLayout。
3. 在 Inspector 配置默认 Item 预制体，调用 BindData(items)。
4. 自定义布局组件挂在列表同一节点，并拖入“布局行为”。每个列表独占一个布局实例。
5. 自动绑定前缀 rlist_ 对应 UIIrregularList，例如 rlist_Messages 生成 RlistMessages。
6. 模板编号 0 使用默认模板；额外模板使用唯一正整数编号在 Inspector 登记。
   即使全部数据使用额外模板，BindData 仍要求配置默认模板。

所有 Item 直接创建并保留在 Content 下，回收仅解绑和隐藏，不执行 SetParent。
模板分别缓存，空闲缓存上限按所有模板合计。资源引用生命周期沿用普通无限列表。

## 不等高、不等宽

业务继承 UISequentialListLayout，一般只需覆盖 GetItemSettings：
返回当前数据的 Size、TemplateKey 和 SpacingAfter。布局算法无需业务重写。

下面是纵向列表的配置示例，整型数据用于演示区分有标题项，实际改为自己的数据类型：

```csharp
protected override UIListItemSettings GetItemSettings(
    object data, int index, UIListLayoutContext context)
{
    // 带标题项预留更大的高度，内部标题显示由业务 Item.OnBind 负责。
    bool hasTitle = data is int value && value % 5 == 0;
    float width = Mathf.Max(1f, context.ViewportSize.x - context.Padding.horizontal);
    float height = hasTitle ? 160f : 100f;
    return new UIListItemSettings(new Vector2(width, height), 0, context.Spacing);
}
```

同一个模板可通过 OnBind 显示或隐藏标题；不同结构可返回不同模板编号。
横向排列同理根据各项宽度累计位置。
顺序布局重建为 O(N)，可见查询采用二分查找，为 O(log N + K)，K 为可见及缓冲项数。
查询不需要先创建全部 Item。

## 完全自定义行为

继承 UIListLayoutBehaviour，按业务实现：

| 方法 | 职责 |
| --- | --- |
| Rebuild(context) | 根据数据、视口、方向和默认配置建立缓存 |
| GetContentSize() | 完整逻辑内容尺寸，主轴决定滚动范围 |
| CollectVisibleItems(start, end, scrollPosition, results) | 输出合法且不重复的可见数据索引，允许非连续结果 |
| GetItemLayout(index, scrollPosition) | 返回矩形、旋转和缩放 |
| GetTemplateKey(index) | 默认返回 0；模板选择不应随滚动改变 |
| GetScrollPosition(index, alignment, currentPosition) | 计算索引定位的逻辑距离 |
| ReportItemSize(index, size) | 可选；记录待处理尺寸，不立即修改当前布局缓存 |
| Clear() | 清理布局持有的数据和测量缓存 |

框架先设置 Context 再调用 Rebuild，业务不需要直接调用内部 Prepare。
矩形从左上角起算，向右、向下为正。Item 使用左上 pivot，旋转绕该点发生。
使用 new UIListItemLayout(rect) 获得无旋转、单位缩放的默认结果。

固定异形排列保持 ChangesWithScroll = false，返回 Content 坐标。
环形或折线路径运动将 ChangesWithScroll 设为 true。
如果返回视口坐标，同时将 UsesViewportCoordinates 设为 true，
框架会补偿 Content 自身的位移，避免重复移动。
路径业务自行实现距离与路径参数的映射、可见查询和索引定位，
GetContentSize 与 GetScrollPosition 必须采用一致的逻辑距离单位。

仅位置变化时不会再次调用 OnBind，也不会重复触发整批显示事件。
布局方法不应创建实例、修改源数据或重入列表接口。
当前提供位置、尺寸、旋转与缩放；颜色和透明度仍由业务 Item 管理。

## 实际尺寸上报

在内部内容完成排版后调用：

```csharp
list.ReportItemSize(item, measuredSize, capturedBindingVersion);
```

测量开始时保存 item.BindingVersion，异步结束时传回原值。
不要在异步完成之后才读取版本；回收或复用后的旧结果会被拒绝。
可在 OnBeforeDisplay 内测量并上报；应测量内部内容，而非仅上报框架刚设置的根尺寸。
尺寸不包含局部缩放。框架不会自动全量测量隐藏项。

顺序布局缓存测量；约 0.1 UI 单位内的变化不会触发新布局。
上报合并到下一次自动更新，不在 Item 回调内重入 Rebuild。
同数量重建时保持首个实际可见项的索引及主轴偏移，缓解上方尺寸变化导致的跳动。
这是索引锚点，不是稳定业务 ID：数据增删时保持距离并限制到边界，
相同数量的排序变化仍按原索引保持位置。

BindData、Refresh、数据数量、视口尺寸或滚动轴变化会清除旧测量；
Rebuild 可以保留测量。路径布局默认关闭锚点保持，仅在逻辑位置与主轴一致时
覆盖 PreserveScrollAnchor。

## 使用范围

当前为单轴、有边界滚动。环形排列不等于首尾无限循环：
同一数据索引同时只对应一个活动实例，不支持重复数据槽位。
圆环和折线路径由业务行为实现，本次提供扩展契约和默认顺序实现。

Content 不使用 LayoutGroup 或 ContentSizeFitter；Item 内部可以使用布局组件。
根尺寸由列表管理，自动排版放在内部节点。数据变化调用 Refresh，
布局配置变化调用 Rebuild，业务展示继续放在 OnBind。