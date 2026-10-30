using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    /// <summary>
    /// 分析所选源图片并批量设置移动平台 ASTC 覆盖格式。
    /// </summary>
    public sealed class TextureAnalysisWindow : EditorWindow
    {
        private static readonly string[] m_platforms = { "Android", "iPhone" }; // Unity 平台覆盖名称。
        private static readonly string[] m_platformLabels = { "Android", "iOS" }; // 平台显示名称。
        private static readonly string[] m_formatLabels = { "ASTC 8×8", "ASTC 6×6", "ASTC 5×5", "ASTC 4×4" }; // 候选格式。
        private static readonly int[] m_blockSizes = { 8, 6, 5, 4 }; // 候选格式块尺寸。
        private static readonly TextureImporterFormat[] m_formats = // 与显示顺序一致的导入格式。
        {
            TextureImporterFormat.ASTC_8x8, TextureImporterFormat.ASTC_6x6,
            TextureImporterFormat.ASTC_5x5, TextureImporterFormat.ASTC_4x4
        };
        private readonly List<TextureAnalysisRow> m_rows = new List<TextureAnalysisRow>(); // 当前分析结果。
        [SerializeField] private DefaultAsset m_folder; // 用户拖入的项目资源文件夹。
        private GUIStyle m_scoreStyle; // 彩色评分标签样式。
        private GUIStyle m_nameStyle; // 文件名样式。
        private GUIStyle m_pathStyle; // 次要路径样式。
        private Vector2 m_scroll; // 列表滚动位置。
        private int m_platform; // 当前目标平台索引。
        private int m_uniformFormat = 1; // 批量统一设置使用的格式索引。
        private float m_low = 0.015f; // 低复杂度经验阈值。
        private float m_medium = 0.04f; // 中复杂度经验阈值。
        private float m_high = 0.08f; // 高复杂度经验阈值。
        private string m_message = "将 Project 中的文件夹拖入分析文件夹栏，然后点击分析。"; // 最近操作结果。

        [MenuItem("★AlloyFramework★/图片分析与压缩")]
        private static void Open()
        {
            GetWindow<TextureAnalysisWindow>("图片分析与压缩").minSize = new Vector2(1050, 420);
        }

        private void OnEnable()
        {
            // 偏好按项目隔离，不将项目阈值写入共享框架包。
            string key = "Alloy.TextureAnalysis." + Application.dataPath;
            m_low = EditorPrefs.GetFloat(key + ".Low", 0.015f);
            m_medium = EditorPrefs.GetFloat(key + ".Medium", 0.04f);
            m_high = EditorPrefs.GetFloat(key + ".High", 0.08f);
            m_platform = Mathf.Clamp(EditorPrefs.GetInt(key + ".Platform", 0), 0, 1);
        }

        private void OnDisable()
        {
            string key = "Alloy.TextureAnalysis." + Application.dataPath;
            if (ValidThresholds())
            {
                EditorPrefs.SetFloat(key + ".Low", m_low);
                EditorPrefs.SetFloat(key + ".Medium", m_medium);
                EditorPrefs.SetFloat(key + ".High", m_high);
            }

            EditorPrefs.SetInt(key + ".Platform", m_platform);
        }

        private void OnGUI()
        {
            GUILayout.Space(8);
            DrawFolderField();
            bool valid = ValidThresholds();
            if (!valid)
            {
                m_low = 0.015f;
                m_medium = 0.04f;
                m_high = 0.08f;
            }

            // 分析与写入分开，先允许用户检查并调整每张图片的候选格式。
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(m_rows.Count == 0))
            {
                if (GUILayout.Button("全选", GUILayout.Width(64), GUILayout.Height(26)))
                {
                    foreach (TextureAnalysisRow row in m_rows) row.Selected = row.Valid;
                }
                if (GUILayout.Button("全不选", GUILayout.Width(64), GUILayout.Height(26)))
                {
                    foreach (TextureAnalysisRow row in m_rows) row.Selected = false;
                }
            }
            GUILayout.Space(14);
            m_platform = GUILayout.Toolbar(m_platform, m_platformLabels, GUILayout.Width(180), GUILayout.Height(26));
            GUILayout.FlexibleSpace();
            GUILayout.Label(m_rows.Count + " 张", EditorStyles.miniLabel);
            GUILayout.Space(12);
            using (new EditorGUI.DisabledScope(!IsValidFolder(m_folder)))
            {
                if (DrawActionButton("分析", new Color(0.35f, 0.85f, 0.45f))) AnalyzeSelection();
            }
            using (new EditorGUI.DisabledScope(m_rows.Count == 0))
            {
                if (DrawActionButton("应用逐项格式", new Color(1f, 0.4f, 0.4f))) ApplySelected();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            m_uniformFormat = EditorGUILayout.Popup(m_uniformFormat, m_formatLabels, GUILayout.Width(140));
            using (new EditorGUI.DisabledScope(m_rows.Count == 0))
            {
                if (DrawActionButton("统一设置", new Color(1f, 0.4f, 0.4f))) ApplyUniformFormat();
                if (DrawActionButton("按推荐设置", new Color(1f, 0.4f, 0.4f))) ApplyRecommendedFormats();
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            foreach (TextureAnalysisRow row in m_rows) DrawRow(row);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField(m_message, EditorStyles.miniLabel);
        }

        private bool ValidThresholds()
        {
            return m_low > 0 && m_low < m_medium && m_medium < m_high && m_high <= 1;
        }

        private static bool DrawActionButton(string label, Color color)
        {
            // 只为当前按钮着色，避免污染平台选项与列表控件。
            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool clicked = GUILayout.Button(label, GUILayout.Width(100), GUILayout.Height(26));
            GUI.backgroundColor = previousColor;
            return clicked;
        }

        private void DrawFolderField()
        {
            // ObjectField 支持从 Project 拖入文件夹，过滤普通资源与包目录。
            DefaultAsset folder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("分析文件夹", "拖入 Assets 下的文件夹，将递归扫描全部纹理资源。"),
                m_folder, typeof(DefaultAsset), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (folder == m_folder) return;
            if (folder != null && !IsValidFolder(folder))
            {
                m_message = "请拖入 Assets 目录或其子文件夹。";
                return;
            }

            // 更换目录后清空旧结果，避免将上次扫描误认为新目录的结果。
            m_folder = folder;
            m_rows.Clear();
            m_message = folder == null ? "请拖入分析文件夹。" : "已选择文件夹，点击分析开始递归扫描。";
        }

        private static bool IsValidFolder(DefaultAsset folder)
        {
            if (folder == null) return false;
            string path = AssetDatabase.GetAssetPath(folder);
            return (path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal))
                && AssetDatabase.IsValidFolder(path);
        }

        private void DrawScore(float rawScore)
        {
            // 百分制只改变展示单位，不改变底层复杂度和格式推荐算法。
            float score = Mathf.Round(Mathf.Clamp01(rawScore) * 1000f) / 10f;
            Color color;
            if (score <= 60f) color = new Color(0.35f, 0.82f, 0.45f);
            else if (score < 80f) color = new Color(1f, 0.86f, 0.25f);
            else if (score < 90f) color = new Color(1f, 0.6f, 0.2f);
            else color = new Color(1f, 0.35f, 0.35f);

            if (m_scoreStyle == null)
            {
                m_scoreStyle = new GUIStyle(EditorStyles.boldLabel);
                m_scoreStyle.alignment = TextAnchor.MiddleCenter;
                m_scoreStyle.normal.textColor = Color.black;
            }

            Rect rect = GUILayoutUtility.GetRect(65, EditorGUIUtility.singleLineHeight, GUILayout.Width(65));
            EditorGUI.DrawRect(rect, color);
            GUI.Label(rect, score.ToString("F1") + " 分", m_scoreStyle);
        }

        private void DrawRow(TextureAnalysisRow row)
        {
            // 卡片突出缩略图与文件名，长路径裁剪显示并在悬停时提供全文。
            if (m_nameStyle == null)
            {
                m_nameStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
                m_pathStyle = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
            }

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.MinHeight(84));
            using (new EditorGUI.DisabledScope(!row.Valid))
            {
                row.Selected = EditorGUILayout.Toggle(row.Selected, GUILayout.Width(20), GUILayout.Height(76));
            }
            DrawThumbnail(row);
            GUILayout.Space(8);
            // 所有行共享信息列宽度，评分保持居中，格式列贴近右侧。
            float informationWidth = Mathf.Max(180f, position.width * 0.5f - 155f);
            EditorGUILayout.BeginVertical(GUILayout.Width(informationWidth));
            GUILayout.Space(6);
            if (GUILayout.Button(new GUIContent(Path.GetFileName(row.Path), row.Path), m_nameStyle))
            {
                EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(row.Path));
            }
            GUILayout.Label(new GUIContent(row.Path, row.Path), m_pathStyle);
            if (row.Valid)
                GUILayout.Label(row.Width + " × " + row.Height, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(row.Note))
                GUILayout.Label(new GUIContent(row.Note, row.Note), m_pathStyle);
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical(GUILayout.Width(65));
            GUILayout.Space(26);
            if (row.Valid)
            {
                DrawScore(row.Score);
            }
            else
            {
                GUILayout.Label("—", GUILayout.Width(65));
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(GUILayout.Width(150));
            GUILayout.Space(10);
            if (row.Valid)
            {
                GUILayout.Label(new GUIContent(row.Current[m_platform], "当前平台格式"), EditorStyles.miniLabel);
                row.FormatIndex = EditorGUILayout.Popup(row.FormatIndex, m_formatLabels, GUILayout.Width(140));
                long bytes = TextureCompressionPolicy.EstimateBytes(
                    row.Width, row.Height, m_blockSizes[row.FormatIndex], row.Mipmaps);
                GUILayout.Label(new GUIContent(EditorUtility.FormatBytes(bytes),
                    "按原图尺寸和现有 Mipmap 设置估算，包含块对齐；实际导入缩放和图集会改变存储量。"),
                    GUILayout.Width(90));
            }
            else
            {
                GUILayout.Label("不可设置", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawThumbnail(TextureAnalysisRow row)
        {
            // 使用编辑器缓存的预览，避免每次重绘重新解码原图。
            Rect rect = GUILayoutUtility.GetRect(76, 76, GUILayout.Width(76), GUILayout.Height(76));
            if (row.Asset == null) row.Asset = AssetDatabase.LoadMainAssetAtPath(row.Path);
            Texture preview = row.Asset == null ? null : AssetPreview.GetAssetPreview(row.Asset);
            if (preview == null && row.Asset != null)
            {
                preview = AssetPreview.GetMiniThumbnail(row.Asset);
                if (AssetPreview.IsLoadingAssetPreview(row.Asset.GetInstanceID())) Repaint();
            }

            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));
            if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
            if (GUI.Button(rect, new GUIContent("", row.Path), GUIStyle.none))
                EditorGUIUtility.PingObject(row.Asset);
        }

        private void AnalyzeSelection()
        {
            if (!IsValidFolder(m_folder)) return;

            // 按窗口指定的目录递归扫描，不依赖 Project 当前选中的对象。
            string folderPath = AssetDatabase.GetAssetPath(m_folder);
            HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture", new[] { folderPath }))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            List<string> ordered = new List<string>(paths);
            ordered.Sort(StringComparer.Ordinal);
            m_rows.Clear();
            try
            {
                foreach (string path in ordered)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("分析图片", path,
                        m_rows.Count / (float)Math.Max(1, ordered.Count))) break;
                    TextureAnalysisRow row = new TextureAnalysisRow { Path = path };
                    m_rows.Add(row);
                    try
                    {
                        AnalyzeRow(row);
                    }
                    catch (Exception exception)
                    {
                        row.Valid = false;
                        row.Selected = false;
                        row.Note = "跳过：" + exception.Message;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                m_message = "已分析 " + m_rows.Count + " / " + ordered.Count + " 张";
            }
        }

        private static TextureImporter ValidateImporter(string path)
        {
            // 仅修改项目资源，包内资源及非二维导入器不参与批量设置。
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("仅支持 Assets 目录内的图片。");
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureShape != TextureImporterShape.Texture2D)
                throw new InvalidOperationException("需要二维 TextureImporter 资源。");
            if (importer.textureType != TextureImporterType.Default
                && importer.textureType != TextureImporterType.Sprite
                && importer.textureType != TextureImporterType.NormalMap)
                throw new InvalidOperationException("仅支持默认纹理、Sprite 或法线贴图。");
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                throw new InvalidOperationException("当前仅支持 PNG/JPG 源文件，避免分析已压缩的导入结果。");
            return importer;
        }

        private void AnalyzeRow(TextureAnalysisRow row)
        {
            TextureImporter importer = ValidateImporter(row.Path);
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                // 从源文件解码，保留原始分辨率，不切换导入设置或触发重导入。
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(row.Path), false))
                    throw new InvalidOperationException("源图片解码失败。");
                row.Width = source.width;
                row.Height = source.height;
                Color32[] pixels = source.GetPixels32();
                bool normal = importer.textureType == TextureImporterType.NormalMap;
                bool dataTexture = normal || !importer.sRGBTexture;
                bool useAlpha = !dataTexture && importer.alphaSource != TextureImporterAlphaSource.None;
                if (useAlpha && importer.alphaSource == TextureImporterAlphaSource.FromGrayScale)
                {
                    // 匹配从灰度生成透明度的导入语义。
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        Color32 pixel = pixels[index];
                        pixel.a = (byte)((pixel.r + pixel.g + pixel.b) / 3);
                        pixels[index] = pixel;
                    }
                }

                row.Score = TextureComplexityAnalyzer.Analyze(pixels, row.Width, row.Height, useAlpha);
                row.Mipmaps = importer.mipmapEnabled;
                row.DependencyHash = AssetDatabase.GetAssetDependencyHash(row.Path);
                row.Valid = true;
                row.Selected = !dataTexture;
                row.Note = dataTexture ? "数据/法线贴图：当前只统计 RGB 梯度，需人工选择格式；未自动勾选。" : "";
                UpdateCurrent(row, importer);
                Recommend(row);
            }
            finally
            {
                DestroyImmediate(source);
            }
        }

        private void RecommendAll()
        {
            foreach (TextureAnalysisRow row in m_rows)
            {
                if (row.Valid) Recommend(row);
            }
        }

        private void Recommend(TextureAnalysisRow row)
        {
            TextureImporterFormat format = TextureCompressionPolicy.Recommend(row.Score, m_low, m_medium, m_high);
            row.FormatIndex = Array.IndexOf(m_formats, format);
        }

        private static void UpdateCurrent(TextureAnalysisRow row, TextureImporter importer)
        {
            for (int index = 0; index < m_platforms.Length; index++)
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(m_platforms[index]);
                row.Current[index] = settings.overridden ? settings.format.ToString() : "继承默认设置";
            }
        }

        private void ApplySelected()
        {
            int applied = 0;
            int failed = 0;
            try
            {
                for (int index = 0; index < m_rows.Count; index++)
                {
                    TextureAnalysisRow row = m_rows[index];
                    if (!row.Valid || !row.Selected) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("设置 ASTC 格式", row.Path,
                        index / (float)m_rows.Count)) break;
                    try
                    {
                        // 阻止将旧分析结果应用到已经改变的源文件或导入设置。
                        TextureImporter importer = ValidateImporter(row.Path);
                        if (row.DependencyHash != AssetDatabase.GetAssetDependencyHash(row.Path))
                            throw new InvalidOperationException("资源已变化，请重新分析后应用。");
                        if (!AssetDatabase.IsOpenForEdit(row.Path + ".meta"))
                            throw new InvalidOperationException("资源 meta 不可编辑，请先签出或解除只读。");

                        TextureImporterPlatformSettings previous = importer.GetPlatformTextureSettings(
                            m_platforms[m_platform]);
                        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(
                            m_platforms[m_platform]);
                        if (!settings.overridden)
                        {
                            // 首次启用覆盖时继承默认参数，避免使用休眠覆盖中的旧尺寸设置。
                            settings = importer.GetDefaultPlatformTextureSettings();
                            settings.name = m_platforms[m_platform];
                        }

                        settings.overridden = true;
                        settings.format = m_formats[row.FormatIndex];
                        try
                        {
                            importer.SetPlatformTextureSettings(settings);
                            importer.SaveAndReimport();
                        }
                        catch
                        {
                            // 单项失败时尽力恢复该平台原设置，其他资源继续处理。
                            importer.SetPlatformTextureSettings(previous);
                            importer.SaveAndReimport();
                            throw;
                        }

                        row.DependencyHash = AssetDatabase.GetAssetDependencyHash(row.Path);
                        UpdateCurrent(row, importer);
                        row.Note = "已应用到 " + m_platformLabels[m_platform] + "。";
                        applied++;
                    }
                    catch (Exception exception)
                    {
                        row.Note = "设置失败：" + exception.Message;
                        failed++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                m_message = "应用完成或已取消：成功 " + applied + " 项，失败 " + failed + " 项。";
            }
        }

        private void ApplyUniformFormat()
        {
            // 仅调整已勾选的有效行，沿用统一的写入校验与失败恢复流程。
            foreach (TextureAnalysisRow row in m_rows)
            {
                if (row.Valid && row.Selected) row.FormatIndex = m_uniformFormat;
            }

            ApplySelected();
        }

        private void ApplyRecommendedFormats()
        {
            // 每张图独立计算候选格式，不覆盖未勾选行的人工选择。
            foreach (TextureAnalysisRow row in m_rows)
            {
                if (row.Valid && row.Selected) Recommend(row);
            }

            ApplySelected();
        }

        private sealed class TextureAnalysisRow
        {
            internal UnityEngine.Object Asset;
            internal string Path;
            internal bool Valid;
            internal bool Selected;
            internal int Width;
            internal int Height;
            internal float Score;
            internal int FormatIndex;
            internal bool Mipmaps;
            internal string Note;
            internal Hash128 DependencyHash;
            internal readonly string[] Current = new string[2];
        }
    }
}
