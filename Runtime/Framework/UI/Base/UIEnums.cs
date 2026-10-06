namespace AlloyFramework.UI
{
    public enum UILayer
    {
        HUD = 0,
        WINDOW = 100,
        POPUP = 200,
        STORY = 300,
        GUIDE = 400,
        TOAST = 500,
        LOADING = 600,
        SYSTEM = 700
    }

    public enum UIState
    {
        None,
        Loading,
        Preparing,
        Opening,
        Active,
        Paused,
        Closing,
        Cached,
        Disposed
    }

    public enum UILayoutMode { FullScreen, Window, Overlay }

    /// <summary>界面的框架背景效果，黑色遮罩由具体预制体提供。</summary>
    public enum UIBackgroundMode
    {
        /// <summary>不启用框架背景效果。</summary>
        None = 0,
        /// <summary>模糊界面背后的画面；保留原序列化值。</summary>
        Blur = 2
    }

    public enum UIInputMode { PassThrough, Block, CloseOnOutside }

    public enum UICacheMode { DestroyOnClose, HideOnClose, Resident }

    public enum UIOpenMode { SingleRefresh, SingleReject, SingleBringToFront, Multiple }

    public enum UINavigationMode { None, Push }
}