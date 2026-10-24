using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlloyFramework.Editor
{
    internal enum EArtFontTarget
    {
        Text,
        TMP
    }

    /// <summary>
    /// 保存艺术字生成参数与产物引用，供编辑器重新生成时恢复配置。
    /// </summary>
    public sealed class ArtFontProfile : ScriptableObject
    {
        [SerializeField] internal EArtFontTarget Target;
        [SerializeField] internal string FontName = "ArtFont";
        [SerializeField] internal string SourceFolder = "";
        [SerializeField] internal string OutputFolder = "Assets/ArtFonts";
        [SerializeField] internal bool AutomaticMetrics = true;
        [SerializeField] internal int PointSize = 64;
        [SerializeField] internal int LineHeight = 72;
        [SerializeField] internal int Ascent = 64;
        [SerializeField] internal int Descent;
        [SerializeField] internal int Spacing;
        [SerializeField] internal bool Monospaced;
        [SerializeField] internal int CellAdvance = 64;
        [SerializeField] internal int SpaceAdvance = 24;
        [SerializeField] internal int Padding = 2;
        // 保留旧版配置字段；自动图集搜索不再读取此值。
        [SerializeField, HideInInspector] internal int MaxAtlasSize = 2048;
        [SerializeField] internal FilterMode Filter = FilterMode.Bilinear;
        [SerializeField] internal bool Recursive;
        [SerializeField] internal string PreviewText = "0123456789";
        [SerializeField] internal List<ArtFontCharacter> Characters = new List<ArtFontCharacter>();
        [SerializeField] internal UnityEngine.Object GeneratedFont;
        [SerializeField] internal Material GeneratedMaterial;
        [SerializeField] internal Texture2D GeneratedAtlas;
    }

    [Serializable]
    internal sealed class ArtFontCharacter
    {
        [SerializeField] internal string SourceFile = "";
        [SerializeField] internal string Character = "";
        [SerializeField] internal int OffsetX;
        [SerializeField] internal int OffsetY;
        [SerializeField] internal int Advance;
    }
}
