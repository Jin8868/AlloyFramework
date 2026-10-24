using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace AlloyFramework.Editor
{
    internal sealed class ArtFontGlyph
    {
        internal ArtFontCharacter Source;
        internal uint Unicode;
        internal int Width;
        internal int Height;
        internal int Advance;
        internal int BearingX;
        internal int BearingY;
        internal RectInt Rect;
        internal Color32[] Pixels;
    }

    internal sealed class ArtFontBuild : IDisposable
    {
        internal readonly List<ArtFontGlyph> Glyphs = new List<ArtFontGlyph>();
        internal Texture2D Atlas;

        /// <summary>释放用于预览和导出的临时图集。</summary>
        public void Dispose()
        {
            if (Atlas != null)
            {
                Object.DestroyImmediate(Atlas);
                Atlas = null;
            }
        }
    }

    internal static class ArtFontBuilder
    {
        internal const string TMPSHADERNAME = "TextMeshPro/Bitmap Custom Atlas";

        internal static string ResolvePath(string path)
        {
            return Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));
        }

        internal static string GetFolderPath(string path)
        {
            string fullPath = ResolvePath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string assetsPath = Path.GetFullPath(Application.dataPath);
            if (!fullPath.StartsWith(assetsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("输出目录必须位于项目 Assets 的子文件夹中。");
            }

            return "Assets/" + fullPath.Substring(assetsPath.Length + 1).Replace('\\', '/');
        }

        internal static uint ParseUnicode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException("字符不能为空，请填写单个字符或 U+80DC 格式的码点。");
            }

            // 显式码点支持文件名中不能出现的标点，以及代理对字符。
            uint unicode;
            if (value.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            {
                if (!uint.TryParse(value.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out unicode))
                {
                    throw new InvalidOperationException($"无效码点：{value}");
                }
            }
            else
            {
                bool single = value.Length == 1 && !char.IsSurrogate(value[0]);
                bool pair = value.Length == 2 && char.IsSurrogatePair(value, 0);
                if (!single && !pair)
                {
                    throw new InvalidOperationException($"“{value}”必须是单个字符，不能把整词图片当成多个字形。");
                }

                unicode = (uint)char.ConvertToUtf32(value, 0);
            }

            if (unicode > 0x10FFFF || unicode >= 0xD800 && unicode <= 0xDFFF || unicode < 0x20)
            {
                throw new InvalidOperationException($"不支持的 Unicode 标量：{value}");
            }

            return unicode;
        }

        internal static int Scan(ArtFontProfile profile, bool replace)
        {
            if (string.IsNullOrWhiteSpace(profile.SourceFolder))
            {
                throw new InvalidOperationException("请先选择原始图片文件夹。");
            }

            string root = ResolvePath(profile.SourceFolder);
            if (!Directory.Exists(root))
            {
                throw new InvalidOperationException("图片文件夹不存在。");
            }

            string[] files = Directory.GetFiles(root, "*", profile.Recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var previous = new Dictionary<string, ArtFontCharacter>(StringComparer.OrdinalIgnoreCase);
            foreach (ArtFontCharacter character in profile.Characters)
            {
                previous[character.SourceFile] = character;
            }

            if (replace)
            {
                profile.Characters.Clear();
            }

            int added = 0;
            string generatedAtlas = ResolvePath(Path.Combine(profile.OutputFolder, profile.FontName + "Atlas.png"));
            foreach (string file in files)
            {
                string extension = Path.GetExtension(file);
                if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(Path.GetFullPath(file), generatedAtlas, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = file.Substring(root.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
                if (previous.TryGetValue(relative, out ArtFontCharacter existing))
                {
                    if (replace)
                    {
                        profile.Characters.Add(existing);
                    }

                    continue;
                }

                profile.Characters.Add(new ArtFontCharacter
                {
                    SourceFile = relative,
                    Character = Path.GetFileNameWithoutExtension(file)
                });
                added++;
            }

            return added;
        }

        internal static ArtFontBuild Prepare(ArtFontProfile profile)
        {
            // 空列表自动发现图片，不再要求额外点击扫描按钮。
            if (profile != null && profile.Characters.Count == 0)
            {
                Scan(profile, false);
            }

            ValidateSettings(profile);
            var build = new ArtFontBuild();
            try
            {
                // 先完整读取和校验，再生成图集；不改源图片导入设置。
                var unicodes = new HashSet<uint>();
                foreach (ArtFontCharacter character in profile.Characters)
                {
                    uint unicode;
                    try
                    {
                        unicode = ParseUnicode(character.Character);
                    }
                    catch (InvalidOperationException exception)
                    {
                        throw new InvalidOperationException(
                            $"图片 {character.SourceFile} 尚未配置有效字符。请修改字符列或使用批量映射。{exception.Message}");
                    }
                    if (unicode == 0x20 || !unicodes.Add(unicode))
                    {
                        throw new InvalidOperationException($"字符重复或占用自动空格：{character.Character}");
                    }

                    if (profile.Target == EArtFontTarget.Text && unicode > 0xFFFF)
                    {
                        throw new InvalidOperationException("Legacy Text 仅生成 BMP 字符；此字符请使用 TMP。");
                    }

                    build.Glyphs.Add(ReadGlyph(profile, character, unicode));
                }

                if (profile.AutomaticMetrics)
                {
                    // 默认直接使用图片像素度量，使用者无需填写字体内部的排版参数。
                    int ascent = 1;
                    int descent = 0;
                    foreach (ArtFontGlyph glyph in build.Glyphs)
                    {
                        ascent = Mathf.Max(ascent, glyph.BearingY);
                        descent = Mathf.Min(descent, glyph.BearingY - glyph.Height);
                    }

                    profile.PointSize = Mathf.Clamp(ascent - descent, 1, 512);
                    profile.Ascent = ascent;
                    profile.Descent = descent;
                    profile.LineHeight = ascent - descent;
                }

                build.Glyphs.Sort(CompareGlyphs);
                int size = 64;
                while (!TryPack(build.Glyphs, size, profile.Padding))
                {
                    size *= 2;
                    if (size > profile.MaxAtlasSize)
                    {
                        throw new InvalidOperationException("图集容量不足。请提高最大尺寸或拆分字体，不会缩小源图片。");
                    }
                }

                build.Atlas = CreateAtlas(build.Glyphs, size, profile.Padding, profile.Filter);
                return build;
            }
            catch
            {
                build.Dispose();
                throw;
            }
        }

        private static void ValidateSettings(ArtFontProfile profile)
        {
            if (profile == null || profile.Characters.Count == 0)
            {
                throw new InvalidOperationException("图片目录中未找到可用图片。支持 PNG、JPG、JPEG，请检查子目录选项。");
            }

            if (string.IsNullOrWhiteSpace(profile.SourceFolder))
            {
                throw new InvalidOperationException("请先选择原始图片文件夹。");
            }

            if (string.IsNullOrWhiteSpace(profile.FontName)
                || profile.FontName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || profile.FontName.EndsWith(".") || profile.FontName != profile.FontName.Trim())
            {
                throw new InvalidOperationException("字体名称必须是有效文件名，不能包含路径或首尾空格。");
            }

            if ((!profile.AutomaticMetrics && (profile.PointSize < 1 || profile.PointSize > 512
                    || profile.LineHeight < 1 || profile.Ascent <= profile.Descent)) || profile.SpaceAdvance < 1
                || profile.Padding < 1 || profile.Padding > 16 || profile.CellAdvance < 1
                || profile.MaxAtlasSize < 64 || profile.MaxAtlasSize > 4096
                || !Mathf.IsPowerOfTwo(profile.MaxAtlasSize))
            {
                throw new InvalidOperationException("字号、行高、基线范围、空格宽度或图集参数无效。");
            }

            GetFolderPath(profile.OutputFolder);
        }

        private static ArtFontGlyph ReadGlyph(ArtFontProfile profile, ArtFontCharacter character, uint unicode)
        {
            string root = ResolvePath(profile.SourceFolder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string file = Path.GetFullPath(Path.Combine(root, character.SourceFile));
            if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            {
                throw new InvalidOperationException($"源文件不存在或超出素材目录：{character.SourceFile}");
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(file), false))
                {
                    throw new InvalidOperationException($"不能读取图片：{character.SourceFile}");
                }

                if (texture.width + profile.Padding * 2 > profile.MaxAtlasSize
                    || texture.height + profile.Padding * 2 > profile.MaxAtlasSize)
                {
                    throw new InvalidOperationException($"图片超过图集最大尺寸：{character.SourceFile}");
                }

                int advance = character.Advance > 0
                    ? character.Advance
                    : (profile.Monospaced ? profile.CellAdvance : texture.width) + profile.Spacing;
                if (advance < 1 || character.Advance < 0)
                {
                    throw new InvalidOperationException($"字形步进必须大于零：{character.Character}");
                }

                return new ArtFontGlyph
                {
                    Source = character,
                    Unicode = unicode,
                    Width = texture.width,
                    Height = texture.height,
                    Advance = advance,
                    BearingX = character.OffsetX,
                    BearingY = texture.height + character.OffsetY,
                    Pixels = texture.GetPixels32()
                };
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static int CompareGlyphs(ArtFontGlyph left, ArtFontGlyph right)
        {
            int heightOrder = right.Height.CompareTo(left.Height);
            return heightOrder != 0 ? heightOrder : left.Unicode.CompareTo(right.Unicode);
        }

        private static bool TryPack(List<ArtFontGlyph> glyphs, int size, int padding)
        {
            int cursorX = 0;
            int cursorY = 0;
            int rowHeight = 0;
            foreach (ArtFontGlyph glyph in glyphs)
            {
                int width = glyph.Width + padding * 2;
                int height = glyph.Height + padding * 2;
                if (width > size || height > size)
                {
                    return false;
                }

                if (cursorX + width > size)
                {
                    cursorX = 0;
                    cursorY += rowHeight;
                    rowHeight = 0;
                }

                if (cursorY + height > size)
                {
                    return false;
                }

                glyph.Rect = new RectInt(cursorX + padding, cursorY + padding, glyph.Width, glyph.Height);
                cursorX += width;
                rowHeight = Mathf.Max(rowHeight, height);
            }

            return true;
        }

        private static Texture2D CreateAtlas(List<ArtFontGlyph> glyphs, int size, int padding, FilterMode filter)
        {
            var pixels = new Color32[size * size];
            foreach (ArtFontGlyph glyph in glyphs)
            {
                // 透明护边保留邻近 RGB；TMP 的额外采样区不会复制出一圈不透明像素。
                for (int offsetY = -padding; offsetY < glyph.Height + padding; offsetY++)
                {
                    int sourceY = Mathf.Clamp(offsetY, 0, glyph.Height - 1);
                    for (int offsetX = -padding; offsetX < glyph.Width + padding; offsetX++)
                    {
                        int sourceX = Mathf.Clamp(offsetX, 0, glyph.Width - 1);
                        int target = (glyph.Rect.y + offsetY) * size + glyph.Rect.x + offsetX;
                        Color32 color = glyph.Pixels[sourceY * glyph.Width + sourceX];
                        if (offsetX < 0 || offsetX >= glyph.Width || offsetY < 0 || offsetY >= glyph.Height)
                        {
                            color.a = 0;
                        }

                        pixels[target] = color;
                    }
                }
            }

            var atlas = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "艺术字临时图集",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp
            };
            atlas.SetPixels32(pixels);
            atlas.Apply(false, false);
            return atlas;
        }

        internal static ArtFontProfile Generate(ArtFontProfile settings, ArtFontProfile existing)
        {
            // 所有易失败的输入和打包操作先完成，再触碰持久化资产。
            using ArtFontBuild build = Prepare(settings);
            Shader shader = Shader.Find(settings.Target == EArtFontTarget.TMP ? TMPSHADERNAME : "UI/Default");
            if (shader == null)
            {
                throw new InvalidOperationException("未找到字体 shader，请先通过面板导入 TMP 基础资源。");
            }

            string folder = GetFolderPath(settings.OutputFolder);
            string prefix = folder + "/" + settings.FontName;
            string configPath = prefix + ".artfont.asset";
            string fontPath = prefix + (settings.Target == EArtFontTarget.Text ? ".fontsettings" : ".asset");
            string materialPath = prefix + "Material.mat";
            string atlasPath = prefix + "Atlas.png";
            string[] paths = { configPath, fontPath, materialPath, atlasPath };
            ValidateOwnership(settings, existing, paths);
            Directory.CreateDirectory(ResolvePath(folder));
            AssetDatabase.Refresh();

            // 记录原文件和 meta；异常时恢复原资产，保留已绑定的 GUID。
            using var transaction = new ArtFontAssetTransaction(paths);
            File.WriteAllBytes(ResolvePath(atlasPath), build.Atlas.EncodeToPNG());
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
            importer.textureType = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = settings.Filter;
            importer.maxTextureSize = settings.MaxAtlasSize;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            if (atlas == null || atlas.width != build.Atlas.width || atlas.height != build.Atlas.height)
            {
                throw new InvalidOperationException("图集被平台导入覆盖设置缩小，请清除该图集的平台尺寸覆盖后重试。");
            }

            Material material = LoadOrCreateMaterial(materialPath, shader);
            material.mainTexture = atlas;
            if (material.HasProperty("_FaceColor"))
            {
                material.SetColor("_FaceColor", Color.white);
                material.SetTexture("_FaceTex", Texture2D.whiteTexture);
            }
            else
            {
                material.color = Color.white;
            }
            Object font = settings.Target == EArtFontTarget.Text
                ? (Object)GenerateText(settings, build, fontPath, material)
                : GenerateTMP(settings, build, fontPath, material, atlas);

            ArtFontProfile profile = existing;
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<ArtFontProfile>();
                AssetDatabase.CreateAsset(profile, configPath);
            }

            EditorUtility.CopySerialized(settings, profile);
            // 主资产名称必须匹配去掉 .asset 扩展名后的文件名，避免 Unity 导入警告。
            profile.name = Path.GetFileNameWithoutExtension(configPath);
            profile.hideFlags = HideFlags.None;
            profile.OutputFolder = folder;
            profile.GeneratedFont = font;
            profile.GeneratedMaterial = material;
            profile.GeneratedAtlas = atlas;
            Save(material);
            Save(font);
            Save(profile);
            transaction.Commit();
            return profile;
        }

        private static void ValidateOwnership(ArtFontProfile settings, ArtFontProfile existing, string[] paths)
        {
            if (existing == null)
            {
                foreach (string path in paths)
                {
                    if (File.Exists(ResolvePath(path)) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                    {
                        throw new InvalidOperationException($"输出位置已有同名资产，请改名或切换到更新模式：{path}");
                    }
                }

                return;
            }

            if (existing.Target != settings.Target || AssetDatabase.GetAssetPath(existing) != paths[0])
            {
                throw new InvalidOperationException("更新时不能更改类型、输出目录或名称；请使用新建模式。");
            }

            Object[] outputs =
            {
                existing, existing.GeneratedFont, existing.GeneratedMaterial, existing.GeneratedAtlas
            };
            for (int index = 0; index < outputs.Length; index++)
            {
                if (outputs[index] == null || AssetDatabase.GetAssetPath(outputs[index]) != paths[index])
                {
                    throw new InvalidOperationException("产物被移动或删除，请恢复原路径后更新，或以新名称生成。");
                }
            }
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            return material;
        }

        private static Font GenerateText(ArtFontProfile settings, ArtFontBuild build, string path, Material material)
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (font == null)
            {
                font = new Font(settings.FontName);
                AssetDatabase.CreateAsset(font, path);
            }

            // 先提交字形，再统一配置原生字体的尺寸与基线。
            var characters = new List<CharacterInfo>();
            foreach (ArtFontGlyph glyph in build.Glyphs)
            {
                float size = build.Atlas.width;
                characters.Add(new CharacterInfo
                {
                    index = (int)glyph.Unicode,
                    size = settings.PointSize,
                    minX = glyph.BearingX,
                    maxX = glyph.BearingX + glyph.Width,
                    minY = glyph.BearingY - glyph.Height,
                    maxY = glyph.BearingY,
                    advance = glyph.Advance,
                    uvBottomLeft = new Vector2(glyph.Rect.xMin / size, glyph.Rect.yMin / size),
                    uvBottomRight = new Vector2(glyph.Rect.xMax / size, glyph.Rect.yMin / size),
                    uvTopLeft = new Vector2(glyph.Rect.xMin / size, glyph.Rect.yMax / size),
                    uvTopRight = new Vector2(glyph.Rect.xMax / size, glyph.Rect.yMax / size)
                });
            }

            characters.Add(new CharacterInfo { index = 32, advance = settings.SpaceAdvance });
            font.characterInfo = characters.ToArray();
            font.material = material;
            var serialized = new SerializedObject(font);
            SetInt(serialized, "m_FontSize", settings.PointSize);
            SetFloat(serialized, "m_PixelScale", 1f);
            SetFloat(serialized, "m_LineSpacing", settings.LineHeight);
            SetFloat(serialized, "m_Ascent", settings.Ascent);
            SetFloat(serialized, "m_Descent", settings.Descent);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return font;
        }

        private static TMP_FontAsset GenerateTMP(
            ArtFontProfile settings, ArtFontBuild build, string path, Material material, Texture2D atlas)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null)
            {
                font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            }

            // 仅在此处适配 TMP 内部序列化字段，避免空版本进入旧资产升级分支。
            var serialized = new SerializedObject(font);
            SetString(serialized, "m_Version", "1.1.0");
            SetInt(serialized, "m_AtlasRenderMode", (int)GlyphRenderMode.SMOOTH);
            SetInt(serialized, "m_AtlasWidth", atlas.width);
            SetInt(serialized, "m_AtlasHeight", atlas.height);
            SetInt(serialized, "m_AtlasPadding", 0);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            font.name = settings.FontName;
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.isMultiAtlasTexturesEnabled = false;
            font.atlasTextures = new[] { atlas };
            font.material = material;
            font.faceInfo = new FaceInfo
            {
                familyName = settings.FontName,
                styleName = "Regular",
                pointSize = settings.PointSize,
                scale = 1f,
                lineHeight = settings.LineHeight,
                ascentLine = settings.Ascent,
                capLine = settings.Ascent,
                meanLine = settings.Ascent * 0.5f,
                baseline = 0,
                descentLine = settings.Descent,
                tabWidth = settings.SpaceAdvance * 4
            };
            font.glyphTable.Clear();
            font.characterTable.Clear();
            foreach (ArtFontGlyph source in build.Glyphs)
            {
                var glyph = new Glyph(source.Unicode + 1,
                    new GlyphMetrics(source.Width, source.Height, source.BearingX, source.BearingY, source.Advance),
                    new GlyphRect(source.Rect.x, source.Rect.y, source.Width, source.Height), 1, 0);
                font.glyphTable.Add(glyph);
                font.characterTable.Add(new TMP_Character(source.Unicode, glyph));
            }

            var space = new Glyph(33, new GlyphMetrics(0, 0, 0, 0, settings.SpaceAdvance), new GlyphRect(), 1, 0);
            font.glyphTable.Add(space);
            font.characterTable.Add(new TMP_Character(32, space));
            font.fallbackFontAssetTable?.Clear();
            font.ReadFontAssetDefinition();
            if (!AssetDatabase.Contains(font))
            {
                AssetDatabase.CreateAsset(font, path);
            }

            return font;
        }

        private static SerializedProperty RequireProperty(SerializedObject target, string name)
        {
            return target.FindProperty(name)
                ?? throw new InvalidOperationException($"当前 Unity/TMP 版本缺少字体字段：{name}");
        }

        private static void SetInt(SerializedObject target, string name, int value)
        {
            SerializedProperty property = RequireProperty(target, name);
            if (property.propertyType == SerializedPropertyType.Float)
            {
                property.floatValue = value;
            }
            else
            {
                property.intValue = value;
            }
        }

        private static void SetFloat(SerializedObject target, string name, float value)
        {
            SerializedProperty property = RequireProperty(target, name);
            if (property.propertyType == SerializedPropertyType.Integer)
            {
                property.intValue = Mathf.RoundToInt(value);
            }
            else
            {
                property.floatValue = value;
            }
        }

        private static void SetString(SerializedObject target, string name, string value)
        {
            RequireProperty(target, name).stringValue = value;
        }

        private static void Save(Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}
