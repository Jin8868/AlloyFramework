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

    public enum UIBackgroundMode { None, Dim, Blur }

    public enum UIInputMode { PassThrough, Block, CloseOnOutside }

    public enum UICacheMode { DestroyOnClose, HideOnClose, Resident }

    public enum UIOpenMode { SingleRefresh, SingleReject, SingleBringToFront, Multiple }

    public enum UINavigationMode { None, Push }
}