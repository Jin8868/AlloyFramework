using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AlloyFramework.Editor
{
    /// <summary>
    /// 提供艺术字字体制作、字符配置、图集预览与已有资产更新面板。
    /// </summary>
    public sealed class ArtFontWindow : EditorWindow
    {
        private const string DRAFTKEY = "AlloyFramework.ArtFont.Draft"; // 当前编辑草稿的会话键。
        private static readonly string[] m_targetLabels = { "UGUI Text", "TextMeshPro" }; // 字体类型显示名称。
        private static readonly string[] m_modeLabels = { "新建艺术字", "更新已有字体" }; // 操作模式显示名称。
        private static readonly int[] m_atlasSizes = { 256, 512, 1024, 2048, 4096 }; // 允许的单页图集上限。
        [SerializeField] private int m_mode; // 当前新建或更新模式。
        [SerializeField] private ArtFontProfile m_existing; // 更新模式选中的持久化配置。
        [SerializeField] private string m_search = ""; // 字符列表过滤条件。
        [SerializeField] private string m_batchCharacters = "1234567890"; // 按图片列表顺序批量指定的字符。
        [SerializeField] private bool m_showAdvancedTypography; // 是否展开字体高级度量。
        [SerializeField] private float m_previewScale = 1; // 预览的设计像素缩放。
        private ArtFontProfile m_settings; // 与持久化配置隔离的编辑草稿。
        private ArtFontProfile m_lastGenerated; // 最近成功生成的资产配置。
        private ArtFontBuild m_preview; // 当前校验成功的临时图集。
        private Vector2 m_settingsScroll; // 设置区域滚动位置。
        private Vector2 m_characterScroll; // 字符列表滚动位置。
        private Vector2 m_previewScroll; // 样例文字滚动位置。
        private string m_message = "拖入图片文件夹即可扫描，支持 PNG/JPG；文件名可使用单字或 U+80DC。"; // 操作反馈。
        private MessageType m_messageType = MessageType.Info; // 操作反馈级别。
        private GUIStyle m_titleStyle; // 标题样式缓存。
        private GUIStyle m_sectionStyle; // 分区标题样式缓存。

        [MenuItem("★AlloyFramework★/UI/艺术字字体生成", false, 120)]
        private static void Open()
        {
            var window = GetWindow<ArtFontWindow>("艺术字工坊");
            window.minSize = new Vector2(980, 700);
            window.Show();
        }

        [UnityEditor.Callbacks.OnOpenAsset]
        private static bool OpenProfile(int instanceID, int line)
        {
            if (!(EditorUtility.InstanceIDToObject(instanceID) is ArtFontProfile profile))
            {
                return false;
            }

            var window = GetWindow<ArtFontWindow>("艺术字工坊");
            window.minSize = new Vector2(980, 700);
            window.m_mode = 1;
            window.LoadProfile(profile);
            window.Show();
            return true;
        }

        private void OnEnable()
        {
            m_settings = CreateInstance<ArtFontProfile>();
            string draft = SessionState.GetString(DRAFTKEY, "");
            if (!string.IsNullOrEmpty(draft))
            {
                JsonUtility.FromJsonOverwrite(draft, m_settings);
            }

            m_settings.hideFlags = HideFlags.HideAndDontSave;
            m_lastGenerated = m_existing;
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
            ClearPreview();
            if (m_settings != null)
            {
                SessionState.SetString(DRAFTKEY, JsonUtility.ToJson(m_settings));
                DestroyImmediate(m_settings);
            }
        }

        private void OnGUI()
        {
            if (m_settings == null)
            {
                return;
            }

            EnsureStyles();
            DrawHeader();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                DrawSettings();
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawCharacters();
                    if (EditorGUI.EndChangeCheck())
                    {
                        ClearPreview();
                    }

                    DrawPreview();
                }
            }

            DrawFooter();
        }

        private void EnsureStyles()
        {
            if (m_titleStyle != null)
            {
                return;
            }

            m_titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 };
            m_sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        }

        private void DrawHeader()
        {
            Rect accent = GUILayoutUtility.GetRect(0, 4, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(accent, new Color(0.22f, 0.68f, 0.85f));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("艺术字工坊", m_titleStyle);
                GUILayout.Label("图片 → 字体资产 → 替换 Font，业务代码保持不变", EditorStyles.miniLabel);
                GUILayout.Space(6);
                int mode = GUILayout.Toolbar(m_mode, m_modeLabels, GUILayout.Height(28));
                if (mode != m_mode)
                {
                    m_mode = mode;
                    m_existing = null;
                    m_lastGenerated = null;
                    ClearPreview();
                }
            }
        }

        private void DrawSettings()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(340)))
            {
                m_settingsScroll = EditorGUILayout.BeginScrollView(m_settingsScroll);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label("01  字体与路径", m_sectionStyle);
                    if (m_mode == 1)
                    {
                        var profile = (ArtFontProfile)EditorGUILayout.ObjectField(
                            "已有生成配置", m_existing, typeof(ArtFontProfile), false);
                        if (profile != m_existing)
                        {
                            LoadProfile(profile);
                        }

                        EditorGUILayout.HelpBox("选择 .artfont.asset；更新保留 Font、材质和图集的 GUID。",
                            MessageType.None);
                    }

                    using (new EditorGUI.DisabledScope(m_mode == 1))
                    {
                        m_settings.Target = (EArtFontTarget)GUILayout.Toolbar(
                            (int)m_settings.Target, m_targetLabels, GUILayout.Height(25));
                        m_settings.FontName = EditorGUILayout.TextField("字体名称", m_settings.FontName);
                        m_settings.OutputFolder = DrawFolder("输出文件夹", m_settings.OutputFolder, true);
                    }

                    string sourceFolder = DrawFolder("原始图片文件夹", m_settings.SourceFolder, false);
                    if (!string.Equals(sourceFolder, m_settings.SourceFolder, StringComparison.Ordinal))
                    {
                        m_settings.SourceFolder = sourceFolder;
                        Scan(true);
                    }

                    GUILayout.Label("路径框支持拖入 Project 或系统文件管理器中的文件夹。",
                        EditorStyles.wordWrappedMiniLabel);
                    if (m_settings.Target == EArtFontTarget.TMP
                        && Shader.Find(ArtFontBuilder.TMPSHADERNAME) == null)
                    {
                        EditorGUILayout.HelpBox("尚未导入 TMP 官方位图 shader，请先导入基础资源。", MessageType.Warning);
                        if (GUILayout.Button("导入 TMP 基础资源"))
                        {
                            ImportTMPResources();
                        }
                    }
                    bool recursive = EditorGUILayout.Toggle("包含子文件夹", m_settings.Recursive);
                    if (recursive != m_settings.Recursive)
                    {
                        m_settings.Recursive = recursive;
                        Scan(true);
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("扫描并追加字符", GUILayout.Height(27)))
                        {
                            Scan(false);
                        }

                        if (GUILayout.Button("重新扫描", GUILayout.Height(27)))
                        {
                            Scan(true);
                        }
                    }

                    GUILayout.Label("追加保留已有行；重新扫描移除已不存在的图片，保留匹配行的手动参数。",
                        EditorStyles.wordWrappedMiniLabel);
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label("02  图片排列", m_sectionStyle);
                    GUILayout.Label("默认按原图尺寸显示、底边对齐；字号和行高自动计算。",
                        EditorStyles.wordWrappedMiniLabel);
                    m_settings.Spacing = EditorGUILayout.IntField("额外字距", m_settings.Spacing);
                    m_settings.Monospaced = EditorGUILayout.Toggle("等宽步进", m_settings.Monospaced);
                    if (m_settings.Monospaced)
                    {
                        m_settings.CellAdvance = EditorGUILayout.IntField("等宽宽度", m_settings.CellAdvance);
                    }

                    m_showAdvancedTypography = EditorGUILayout.Foldout(
                        m_showAdvancedTypography, "高级排版（可选）", true);
                    if (m_showAdvancedTypography)
                    {
                        m_settings.AutomaticMetrics = EditorGUILayout.Toggle(
                            "按图片自动计算", m_settings.AutomaticMetrics);
                        using (new EditorGUI.DisabledScope(m_settings.AutomaticMetrics))
                        {
                            m_settings.PointSize = EditorGUILayout.IntField("设计字号", m_settings.PointSize);
                            m_settings.LineHeight = EditorGUILayout.IntField("行高", m_settings.LineHeight);
                            m_settings.Ascent = EditorGUILayout.IntField("上升线", m_settings.Ascent);
                            m_settings.Descent = EditorGUILayout.IntField("下降线", m_settings.Descent);
                        }

                        m_settings.SpaceAdvance = EditorGUILayout.IntField("空格宽度", m_settings.SpaceAdvance);
                        EditorGUILayout.HelpBox("UV 决定取图区域；度量决定四边形大小、基线与字符步进。通常无需修改。",
                            MessageType.None);
                    }
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label("03  图集设置", m_sectionStyle);
                    m_settings.MaxAtlasSize = EditorGUILayout.IntPopup("最大尺寸", m_settings.MaxAtlasSize,
                        new[] { "256", "512", "1024", "2048", "4096" }, m_atlasSizes);
                    m_settings.Padding = EditorGUILayout.IntSlider("边缘扩展", m_settings.Padding, 1, 16);
                    m_settings.Filter = EditorGUILayout.Toggle("像素风格 / Point", m_settings.Filter == FilterMode.Point)
                        ? FilterMode.Point
                        : FilterMode.Bilinear;
                    GUILayout.Label("自动选择可容纳的最小单页尺寸；保留原图尺寸，不旋转、不缩图。",
                        EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private static string DrawFolder(string label, string value, bool output)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                string result = EditorGUILayout.DelayedTextField(value);
                Rect pathRect = GUILayoutUtility.GetLastRect();
                result = HandleFolderDrop(pathRect, result, output);
                if (GUILayout.Button("选择", GUILayout.Width(44)))
                {
                    string selected = EditorUtility.OpenFolderPanel(label, Application.dataPath, "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        if (output)
                        {
                            try
                            {
                                result = NormalizeFolder(selected, true);
                            }
                            catch (Exception exception)
                            {
                                EditorUtility.DisplayDialog("目录不可用", exception.Message, "知道了");
                            }
                        }
                        else
                        {
                            result = NormalizeFolder(selected, false);
                        }
                    }
                }

                return result;
            }
        }

        private static string NormalizeFolder(string path, bool output)
        {
            string fullPath = ArtFontBuilder.ResolvePath(path);
            if (!Directory.Exists(fullPath))
            {
                throw new InvalidOperationException("请拖入文件夹，而不是图片或其他文件。");
            }

            if (output)
            {
                return ArtFontBuilder.GetFolderPath(fullPath);
            }

            string relative = FileUtil.GetProjectRelativePath(fullPath.Replace('\\', '/'));
            return string.IsNullOrEmpty(relative) ? fullPath : relative;
        }

        private static string HandleFolderDrop(Rect area, string value, bool output)
        {
            Event current = Event.current;
            if (!GUI.enabled || !area.Contains(current.mousePosition)
                || current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
            {
                return value;
            }

            // 接受 Project 文件夹资产和系统文件管理器路径，拒绝多选和普通文件。
            string path = null;
            if (DragAndDrop.paths.Length == 1)
            {
                path = DragAndDrop.paths[0];
            }
            else if (DragAndDrop.paths.Length == 0 && DragAndDrop.objectReferences.Length == 1)
            {
                path = AssetDatabase.GetAssetPath(DragAndDrop.objectReferences[0]);
            }

            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    throw new InvalidOperationException("请每次拖入一个文件夹。");
                }

                string folder = NormalizeFolder(path, output);
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    GUI.FocusControl(null);
                    GUI.changed = true;
                    value = folder;
                }
            }
            catch (Exception exception)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                if (current.type == EventType.DragPerform)
                {
                    EditorUtility.DisplayDialog("目录不可用", exception.Message, "知道了");
                }
            }

            current.Use();
            return value;
        }

        private void DrawCharacters()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"04  字符配置 · {m_settings.Characters.Count} 个", m_sectionStyle);
                    GUILayout.FlexibleSpace();
                    m_search = EditorGUILayout.TextField(m_search, EditorStyles.toolbarSearchField,
                        GUILayout.Width(160));
                }

                GUILayout.Label("字符支持单字或 U+XXXX；步进为 0 时使用全局规则。", EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    m_batchCharacters = EditorGUILayout.TextField("按顺序批量映射", m_batchCharacters);
                    if (GUILayout.Button("应用到全部行", GUILayout.Width(100)))
                    {
                        ApplyBatchCharacters();
                    }
                }

                GUILayout.Label("按下方全部图片顺序逐字对应，不受搜索过滤影响。", EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label("图片文件", GUILayout.MinWidth(100));
                    GUILayout.Label("字符 / 码点", GUILayout.Width(90));
                    GUILayout.Label("偏移 X", GUILayout.Width(52));
                    GUILayout.Label("偏移 Y", GUILayout.Width(52));
                    GUILayout.Label("步进", GUILayout.Width(52));
                    GUILayout.Space(25);
                }

                m_characterScroll = EditorGUILayout.BeginScrollView(m_characterScroll,
                    GUILayout.MinHeight(110), GUILayout.MaxHeight(235));
                int removeIndex = -1;
                for (int index = 0; index < m_settings.Characters.Count; index++)
                {
                    ArtFontCharacter character = m_settings.Characters[index];
                    if (!string.IsNullOrEmpty(m_search)
                        && character.SourceFile.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) < 0
                        && character.Character.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(new GUIContent(character.SourceFile, character.SourceFile),
                            EditorStyles.miniLabel, GUILayout.MinWidth(100));
                        character.Character = EditorGUILayout.TextField(character.Character, GUILayout.Width(90));
                        character.OffsetX = EditorGUILayout.IntField(character.OffsetX, GUILayout.Width(52));
                        character.OffsetY = EditorGUILayout.IntField(character.OffsetY, GUILayout.Width(52));
                        character.Advance = EditorGUILayout.IntField(character.Advance, GUILayout.Width(52));
                        if (GUILayout.Button("×", GUILayout.Width(23)))
                        {
                            removeIndex = index;
                        }
                    }
                }

                if (removeIndex >= 0)
                {
                    m_settings.Characters.RemoveAt(removeIndex);
                    ClearPreview();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPreview()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("05  字形排版与图集预览", m_sectionStyle);
                m_settings.PreviewText = EditorGUILayout.TextField("示例内容", m_settings.PreviewText);
                m_previewScale = EditorGUILayout.Slider("预览缩放", m_previewScale, 0.25f, 2);
                if (m_preview == null)
                {
                    EditorGUILayout.HelpBox("点击“校验并预览”读取图片并检查映射。修改生成参数后需重新校验。",
                        MessageType.Info);
                    GUILayout.FlexibleSpace();
                    return;
                }

                int size = m_preview.Atlas.width;
                GUILayout.Label($"{size} × {size} · RGBA32 · 约 {size * size * 4f / 1048576f:0.##} MiB",
                    EditorStyles.miniLabel);
                DrawSample();
                Rect atlasRect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(atlasRect, new Color(0.12f, 0.13f, 0.15f));
                GUI.DrawTexture(atlasRect, m_preview.Atlas, ScaleMode.ScaleToFit, true);
                GUILayout.Label("青色线为基线；预览按字形配置绘制，最终显示以原生组件为准。",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawSample()
        {
            string sample = m_settings.PreviewText ?? "";
            float width = Mathf.Max(600, sample.Length * m_settings.PointSize * m_previewScale + 40);
            float height = Mathf.Max(120, (sample.Split('\n').Length + 1) * m_settings.LineHeight * m_previewScale);
            m_previewScroll = EditorGUILayout.BeginScrollView(m_previewScroll, GUILayout.Height(145));
            Rect area = GUILayoutUtility.GetRect(width, height);
            EditorGUI.DrawRect(area, new Color(0.17f, 0.18f, 0.20f));
            GUI.BeginClip(area);
            float cursor = 16;
            float baseline = 12 + m_settings.Ascent * m_previewScale;
            var lookup = new Dictionary<uint, ArtFontGlyph>();
            foreach (ArtFontGlyph glyph in m_preview.Glyphs)
            {
                lookup.Add(glyph.Unicode, glyph);
            }

            EditorGUI.DrawRect(new Rect(0, baseline, width, 1), new Color(0.22f, 0.68f, 0.85f, 0.6f));
            for (int index = 0; index < sample.Length; index++)
            {
                if (sample[index] == '\n')
                {
                    cursor = 16;
                    baseline += m_settings.LineHeight * m_previewScale;
                    EditorGUI.DrawRect(new Rect(0, baseline, width, 1), new Color(0.22f, 0.68f, 0.85f, 0.6f));
                    continue;
                }

                if (sample[index] == '\r')
                {
                    continue;
                }

                bool pair = char.IsSurrogatePair(sample, index);
                uint unicode = pair ? (uint)char.ConvertToUtf32(sample, index++) : sample[index];
                if (unicode == 32)
                {
                    cursor += m_settings.SpaceAdvance * m_previewScale;
                    continue;
                }

                if (!lookup.TryGetValue(unicode, out ArtFontGlyph glyph))
                {
                    GUI.Label(new Rect(cursor, baseline - 20, 70, 22), $"缺:{unicode:X}");
                    cursor += m_settings.PointSize * m_previewScale;
                    continue;
                }

                var rect = new Rect(cursor + glyph.BearingX * m_previewScale,
                    baseline - glyph.BearingY * m_previewScale,
                    glyph.Width * m_previewScale, glyph.Height * m_previewScale);
                float size = m_preview.Atlas.width;
                GUI.DrawTextureWithTexCoords(rect, m_preview.Atlas,
                    new Rect(glyph.Rect.x / size, glyph.Rect.y / size, glyph.Width / size, glyph.Height / size));
                cursor += glyph.Advance * m_previewScale;
            }

            GUI.EndClip();
            EditorGUILayout.EndScrollView();
        }

        private void DrawFooter()
        {
            EditorGUILayout.HelpBox(m_message, m_messageType);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("校验并预览", GUILayout.Height(34), GUILayout.Width(145)))
                    {
                        PreparePreview();
                    }

                    Color previous = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.40f, 0.80f, 0.96f);
                    using (new EditorGUI.DisabledScope(m_mode == 1 && m_existing == null))
                    {
                        if (GUILayout.Button(m_mode == 0 ? "一键生成字体" : "重新生成 · 保留引用",
                            GUILayout.Height(34)))
                        {
                            Generate();
                        }
                    }

                    GUI.backgroundColor = previous;
                    using (new EditorGUI.DisabledScope(m_lastGenerated == null))
                    {
                        if (GUILayout.Button("定位产物", GUILayout.Height(34), GUILayout.Width(85)))
                        {
                            EditorGUIUtility.PingObject(m_lastGenerated.GeneratedFont);
                        }

                        if (GUILayout.Button("应用到选中组件", GUILayout.Height(34), GUILayout.Width(130)))
                        {
                            ApplySelection();
                        }
                    }
                }
            }
        }

        private void LoadProfile(ArtFontProfile profile)
        {
            m_existing = profile;
            m_lastGenerated = profile;
            if (profile != null)
            {
                EditorUtility.CopySerialized(profile, m_settings);
                m_settings.hideFlags = HideFlags.HideAndDontSave;
            }

            ClearPreview();
        }

        private void Scan(bool replace)
        {
            try
            {
                int added = ArtFontBuilder.Scan(m_settings, replace);
                SetMessage(m_settings.Characters.Count > 0
                    ? $"发现 {m_settings.Characters.Count} 张图片，新增 {added} 张。请检查字符列或按顺序批量映射。"
                    : "目录中未找到 PNG/JPG/JPEG 图片，请检查路径或勾选包含子文件夹。",
                    m_settings.Characters.Count > 0 ? MessageType.Info : MessageType.Warning);
                ClearPreview();
            }
            catch (Exception exception)
            {
                SetMessage(exception.Message, MessageType.Error);
            }
        }

        private void ApplyBatchCharacters()
        {
            try
            {
                if (m_settings.Characters.Count == 0)
                {
                    ArtFontBuilder.Scan(m_settings, false);
                }

                // 先验证整个序列，失败时不修改任何已有映射。
                var characters = new List<string>();
                var unicodes = new HashSet<uint>();
                for (int index = 0; index < m_batchCharacters.Length; index++)
                {
                    int length = char.IsSurrogatePair(m_batchCharacters, index) ? 2 : 1;
                    string character = m_batchCharacters.Substring(index, length);
                    uint unicode = ArtFontBuilder.ParseUnicode(character);
                    if (unicode == 32 || !unicodes.Add(unicode))
                    {
                        throw new InvalidOperationException("批量字符不能重复或包含空格。");
                    }

                    characters.Add(character);
                    index += length - 1;
                }

                if (characters.Count == 0 || characters.Count != m_settings.Characters.Count)
                {
                    throw new InvalidOperationException(
                        $"填写了 {characters.Count} 个字符，但列表有 {m_settings.Characters.Count} 张图片，数量必须一致。");
                }

                for (int index = 0; index < characters.Count; index++)
                {
                    m_settings.Characters[index].Character = characters[index];
                }

                ClearPreview();
                SetMessage("已按图片列表顺序设置字符，可校验预览或直接生成。", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetMessage(exception.Message, MessageType.Error);
            }
        }

        private void ImportTMPResources()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_FontAsset).Assembly);
            string path = package == null ? "" : Path.Combine(package.resolvedPath,
                "Package Resources/TMP Essential Resources.unitypackage");
            if (!File.Exists(path))
            {
                SetMessage("未找到 TMP 基础资源包，请使用 Window / TextMeshPro / Import TMP Essential Resources。",
                    MessageType.Error);
                return;
            }

            AssetDatabase.ImportPackage(path, true);
        }

        private void PreparePreview()
        {
            ClearPreview();
            try
            {
                m_preview = ArtFontBuilder.Prepare(m_settings);
                SetMessage("字符映射与图集容量校验通过。请检查基线、字距及示例内容中的缺字提示。", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetMessage(exception.Message, MessageType.Error);
            }
        }

        private void Generate()
        {
            try
            {
                EditorUtility.DisplayProgressBar("艺术字字体生成", "正在校验图片、生成图集与字体资产……", 0.3f);
                ArtFontProfile generated = ArtFontBuilder.Generate(m_settings, m_mode == 1 ? m_existing : null);
                m_mode = 1;
                LoadProfile(generated);
                PreparePreview();
                SetMessage("生成完成。替换组件 Font / Font Asset 即可使用，业务继续设置 .text。", MessageType.Info);
                EditorGUIUtility.PingObject(generated.GeneratedFont);
            }
            catch (Exception exception)
            {
                SetMessage(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ApplySelection()
        {
            int count = 0;
            foreach (GameObject selected in Selection.gameObjects)
            {
                if (m_lastGenerated.Target == EArtFontTarget.Text && selected.TryGetComponent(out Text text))
                {
                    Undo.RecordObject(text, "应用艺术字字体");
                    text.font = (Font)m_lastGenerated.GeneratedFont;
                    text.material = m_lastGenerated.GeneratedMaterial;
                    text.fontSize = m_lastGenerated.PointSize;
                    text.fontStyle = FontStyle.Normal;
                    text.resizeTextForBestFit = false;
                    text.supportRichText = false;
                    text.color = Color.white;
                    SaveComponent(text);
                    count++;
                }
                else if (m_lastGenerated.Target == EArtFontTarget.TMP
                    && selected.TryGetComponent(out TextMeshProUGUI tmp))
                {
                    Undo.RecordObject(tmp, "应用艺术字字体");
                    tmp.font = (TMP_FontAsset)m_lastGenerated.GeneratedFont;
                    tmp.fontSharedMaterial = m_lastGenerated.GeneratedMaterial;
                    tmp.fontSize = m_lastGenerated.PointSize;
                    tmp.fontStyle = FontStyles.Normal;
                    tmp.enableAutoSizing = false;
                    tmp.extraPadding = false;
                    tmp.richText = false;
                    tmp.enableVertexGradient = false;
                    tmp.color = Color.white;
                    SaveComponent(tmp);
                    count++;
                }
            }

            SetMessage(count > 0 ? $"已应用到 {count} 个选中组件，可用撤销恢复。"
                : "请在 Hierarchy 或预制体模式中选中匹配类型的 Text / TextMeshProUGUI。",
                count > 0 ? MessageType.Info : MessageType.Warning);
        }

        private static void SaveComponent(Component component)
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            if (component.gameObject.scene.IsValid())
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            }
        }

        private void SetMessage(string message, MessageType type)
        {
            m_message = message;
            m_messageType = type;
            Repaint();
        }

        private void ClearPreview()
        {
            m_preview?.Dispose();
            m_preview = null;
        }
    }
}
