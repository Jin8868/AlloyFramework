using System.Collections.Generic;
using AlloyFramework.UI;

namespace AlloyFramework.GM
{
    /// <summary>GM 预制体在框架 UI 系统中的界面定义。</summary>
    internal static class GMUIDefinitions
    {
        internal static readonly UIDefinition<GMEntryView, GMEntryController, GMUISession> Entry =
            new UIDefinitionBuilder<GMEntryView, GMEntryController, GMUISession>("GMEntry")
                .Location("Prefabs/Framework/GM/GMEntry")
                .Layer(UILayer.SYSTEM)
                .Layout(UILayoutMode.Overlay)
                .Input(UIInputMode.Block)
                .Cache(UICacheMode.DestroyOnClose)
                .OpenMode(UIOpenMode.SingleBringToFront)
                .Build();

        internal static readonly UIDefinition<GMPanel, GMPanelController, IReadOnlyList<GMCommand>> Panel =
            new UIDefinitionBuilder<GMPanel, GMPanelController, IReadOnlyList<GMCommand>>("GMPanelUI")
                .Location("Prefabs/Framework/GM/GMPanelUI")
                .Layer(UILayer.SYSTEM)
                .Layout(UILayoutMode.FullScreen)
                .Input(UIInputMode.Block)
                .Cache(UICacheMode.DestroyOnClose)
                .OpenMode(UIOpenMode.SingleBringToFront)
                .Build();
    }
}
