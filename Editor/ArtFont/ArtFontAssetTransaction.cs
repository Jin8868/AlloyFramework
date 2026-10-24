using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    internal sealed class ArtFontAssetTransaction : IDisposable
    {
        private readonly Dictionary<string, byte[]> m_files = new Dictionary<string, byte[]>(); // 原资产与 meta 快照。
        private readonly string[] m_paths; // 本次生成拥有的输出路径。
        private bool m_committed; // 产物是否已全部保存。

        internal ArtFontAssetTransaction(string[] paths)
        {
            m_paths = paths;
            foreach (string path in paths)
            {
                Capture(path);
                Capture(path + ".meta");
            }
        }

        internal void Commit()
        {
            m_committed = true;
        }

        /// <summary>失败时恢复生成前的资产文件与引用标识。</summary>
        public void Dispose()
        {
            if (m_committed)
            {
                return;
            }

            // 只回滚本次声明拥有的文件，绝不删除目录或其他字体资产。
            try
            {
                foreach (KeyValuePair<string, byte[]> snapshot in m_files)
                {
                    string path = ArtFontBuilder.ResolvePath(snapshot.Key);
                    if (snapshot.Value != null)
                    {
                        File.WriteAllBytes(path, snapshot.Value);
                    }
                    else if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (string path in m_paths)
                {
                    if (File.Exists(ArtFontBuilder.ResolvePath(path)))
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("艺术字资产回滚失败，请检查文件权限：" + exception);
            }
        }

        private void Capture(string path)
        {
            string fullPath = ArtFontBuilder.ResolvePath(path);
            m_files.Add(path, File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null);
        }
    }
}
