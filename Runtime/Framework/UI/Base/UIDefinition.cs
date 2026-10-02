using System;
using AlloyFramework.UI;

namespace AlloyFramework.UI
{
    public readonly struct UIEmptyData { }

    public abstract class UIDefinition
    {
        protected UIDefinition(string uiName, string location, string packageName, UILayer layer,
            UILayoutMode layoutMode, UIBackgroundMode backgroundMode, UIInputMode inputMode,
            UICacheMode cacheMode, UIOpenMode openMode, UINavigationMode navigationMode,
            bool pauseCovered)
        {
            if (string.IsNullOrWhiteSpace(uiName)) throw new ArgumentException("UI name is required.", nameof(uiName));
            if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("UI location is required.", nameof(location));
            UIName = uiName;
            Location = location;
            PackageName = string.IsNullOrWhiteSpace(packageName) ? ResourceSettings.DefaultPackageName : packageName;
            Layer = layer;
            LayoutMode = layoutMode;
            BackgroundMode = backgroundMode;
            InputMode = inputMode;
            CacheMode = cacheMode;
            OpenMode = openMode;
            NavigationMode = navigationMode;
            PauseCovered = pauseCovered;
        }

        public string UIName { get; }
        public string Location { get; }
        public string PackageName { get; }
        public UILayer Layer { get; }
        public UILayoutMode LayoutMode { get; }
        public UIBackgroundMode BackgroundMode { get; }
        public UIInputMode InputMode { get; }
        public UICacheMode CacheMode { get; }
        public UIOpenMode OpenMode { get; }
        public UINavigationMode NavigationMode { get; }
        public bool PauseCovered { get; }
        internal abstract Type ViewType { get; }
        internal abstract Type ControllerType { get; }
        internal abstract Type DataType { get; }
        internal abstract UIController CreateController();
    }
}

namespace AlloyFramework.UI
{
    public class UIDefinition<TView, TController, TData> : UIDefinition
        where TView : UIView
        where TController : UIController<TView, TData>, new()
    {
        internal UIDefinition(string uiName, string location, string packageName, UILayer layer,
            UILayoutMode layoutMode, UIBackgroundMode backgroundMode, UIInputMode inputMode,
            UICacheMode cacheMode, UIOpenMode openMode, UINavigationMode navigationMode, bool pauseCovered)
            : base(uiName, location, packageName, layer, layoutMode, backgroundMode, inputMode,
                cacheMode, openMode, navigationMode, pauseCovered) { }

        internal override Type ViewType => typeof(TView);
        internal override Type ControllerType => typeof(TController);
        internal override Type DataType => typeof(TData);
        internal override UIController CreateController() => new TController();
    }

    public sealed class UIDefinition<TView, TController> : UIDefinition<TView, TController, UIEmptyData>
        where TView : UIView
        where TController : UIController<TView, UIEmptyData>, new()
    {
        internal UIDefinition(string uiName, string location, string packageName, UILayer layer,
            UILayoutMode layoutMode, UIBackgroundMode backgroundMode, UIInputMode inputMode,
            UICacheMode cacheMode, UIOpenMode openMode, UINavigationMode navigationMode, bool pauseCovered)
            : base(uiName, location, packageName, layer, layoutMode, backgroundMode, inputMode,
                cacheMode, openMode, navigationMode, pauseCovered) { }
    }

    public class UIDefinitionBuilder<TView, TController, TData>
        where TView : UIView
        where TController : UIController<TView, TData>, new()
    {
        protected readonly string Name;
        protected string AssetLocation;
        protected string AssetPackage = ResourceSettings.DefaultPackageName;
        protected UILayer DisplayLayer = UILayer.WINDOW;
        protected UILayoutMode DisplayLayout = UILayoutMode.Window;
        protected UIBackgroundMode DisplayBackground = UIBackgroundMode.None;
        protected UIInputMode DisplayInput = UIInputMode.Block;
        protected UICacheMode DisplayCache = UICacheMode.DestroyOnClose;
        protected UIOpenMode DisplayOpen = UIOpenMode.SingleRefresh;
        protected UINavigationMode DisplayNavigation = UINavigationMode.None;
        protected bool ShouldPauseCovered;

        public UIDefinitionBuilder(string uiName) => Name = uiName;
        public UIDefinitionBuilder<TView, TController, TData> Location(string value) { AssetLocation = value; return this; }
        public UIDefinitionBuilder<TView, TController, TData> Package(string value) { AssetPackage = value; return this; }
        public UIDefinitionBuilder<TView, TController, TData> Layer(UILayer value) { DisplayLayer = value; return this; }
        public UIDefinitionBuilder<TView, TController, TData> Layout(UILayoutMode value) { DisplayLayout = value; return this; }
        public UIDefinitionBuilder<TView, TController, TData> Background(UIBackgroundMode value) { DisplayBackground = value; return this; }
        public UIDefinitionBuilder<TView, TController, TData> Input(UIInputMode value) { DisplayInput = value; return this; }
        public UIDefinitionBuilder<TView, TController, TData> Cache(UICacheMode value) { DisplayCache = value; return this; }
        public UIDefinitionBuilder<TView, TController, TData> OpenMode(UIOpenMode value) { DisplayOpen = value; return this; }
        public UIDefinitionBuilder<TView, TController, TData> Navigation(UINavigationMode value) { DisplayNavigation = value; return this; }
        public UIDefinitionBuilder<TView, TController, TData> PauseCovered(bool value) { ShouldPauseCovered = value; return this; }

        public UIDefinition<TView, TController, TData> Build() =>
            new UIDefinition<TView, TController, TData>(Name, AssetLocation, AssetPackage,
                DisplayLayer, DisplayLayout, DisplayBackground, DisplayInput, DisplayCache,
                DisplayOpen, DisplayNavigation, ShouldPauseCovered);
    }

    public sealed class UIDefinitionBuilder<TView, TController> :
        UIDefinitionBuilder<TView, TController, UIEmptyData>
        where TView : UIView
        where TController : UIController<TView, UIEmptyData>, new()
    {
        public UIDefinitionBuilder(string uiName) : base(uiName) { }

        public new UIDefinitionBuilder<TView, TController> Location(string value) { base.Location(value); return this; }
        public new UIDefinitionBuilder<TView, TController> Package(string value) { base.Package(value); return this; }
        public new UIDefinitionBuilder<TView, TController> Layer(UILayer value) { base.Layer(value); return this; }
        public new UIDefinitionBuilder<TView, TController> Layout(UILayoutMode value) { base.Layout(value); return this; }
        public new UIDefinitionBuilder<TView, TController> Background(UIBackgroundMode value) { base.Background(value); return this; }
        public new UIDefinitionBuilder<TView, TController> Input(UIInputMode value) { base.Input(value); return this; }
        public new UIDefinitionBuilder<TView, TController> Cache(UICacheMode value) { base.Cache(value); return this; }
        public new UIDefinitionBuilder<TView, TController> OpenMode(UIOpenMode value) { base.OpenMode(value); return this; }
        public new UIDefinitionBuilder<TView, TController> Navigation(UINavigationMode value) { base.Navigation(value); return this; }
        public new UIDefinitionBuilder<TView, TController> PauseCovered(bool value) { base.PauseCovered(value); return this; }

        public new UIDefinition<TView, TController> Build() =>
            new UIDefinition<TView, TController>(Name, AssetLocation, AssetPackage,
                DisplayLayer, DisplayLayout, DisplayBackground, DisplayInput, DisplayCache,
                DisplayOpen, DisplayNavigation, ShouldPauseCovered);
    }
}
