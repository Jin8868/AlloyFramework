using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AlloyFramework.Audio
{
    /// <summary>为音频交付提供增量 SHA256，Windows 优先使用系统 CNG 实现。</summary>
    internal sealed class AudioContentHash : IDisposable
    {
        private readonly SHA256 m_managedHash; // 非 Windows 或系统接口不可用时的完整校验实现。
        private bool m_disposed; // 防止重复释放原生句柄。
        private bool m_completed; // 每个实例只计算一个文件的摘要。
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        private IntPtr m_algorithm; // 当前文件使用的 CNG 算法提供器。
        private IntPtr m_hash; // 由 CNG 分配内部状态并负责释放的增量哈希句柄。
#endif

        internal string Implementation { get; }

        internal AudioContentHash()
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            try
            {
                // 由系统分配哈希工作区，避免托管 SHA256 在 Mono 下逐块执行压缩运算。
                CheckStatus(BCryptOpenAlgorithmProvider(out m_algorithm, "SHA256", null, 0));
                CheckStatus(BCryptCreateHash(
                    m_algorithm, out m_hash, IntPtr.Zero, 0, IntPtr.Zero, 0, 0));
                Implementation = "Windows CNG SHA256 (bcrypt.dll)";
                return;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException || exception is EntryPointNotFoundException ||
                exception is CryptographicException || exception is PlatformNotSupportedException)
            {
                // 仅初始化失败允许回退，计算过程中失败必须中止交付，不能跳过校验。
                ReleaseNative();
                m_managedHash = SHA256.Create();
                Implementation = m_managedHash.GetType().FullName + "，CNG 初始化回退：" + exception.Message;
            }
#else
            m_managedHash = SHA256.Create();
            Implementation = m_managedHash.GetType().FullName;
#endif
        }

        internal void Append(byte[] buffer, int count)
        {
            ValidateActive();
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (m_hash != IntPtr.Zero)
            {
                CheckStatus(BCryptHashData(m_hash, buffer, count, 0));
                return;
            }
#endif
            m_managedHash.TransformBlock(buffer, 0, count, buffer, 0);
        }

        internal byte[] Complete()
        {
            ValidateActive();
            m_completed = true;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (m_hash != IntPtr.Zero)
            {
                byte[] digest = new byte[32];
                CheckStatus(BCryptFinishHash(m_hash, digest, digest.Length, 0));
                return digest;
            }
#endif
            m_managedHash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return m_managedHash.Hash;
        }

        /// <summary>释放当前文件的哈希句柄及托管实现。</summary>
        public void Dispose()
        {
            if (m_disposed) { return; }
            m_disposed = true;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            ReleaseNative();
#endif
            m_managedHash?.Dispose();
        }

        private void ValidateActive()
        {
            if (m_disposed) { throw new ObjectDisposedException(nameof(AudioContentHash)); }
            if (m_completed) { throw new InvalidOperationException("音频 SHA256 摘要已完成。"); }
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        private void ReleaseNative()
        {
            // 先销毁哈希工作区，再关闭算法提供器，取消和异常同样经过此路径。
            if (m_hash != IntPtr.Zero)
            {
                BCryptDestroyHash(m_hash);
                m_hash = IntPtr.Zero;
            }
            if (m_algorithm != IntPtr.Zero)
            {
                BCryptCloseAlgorithmProvider(m_algorithm, 0);
                m_algorithm = IntPtr.Zero;
            }
        }

        private static void CheckStatus(int status)
        {
            if (status < 0)
            {
                throw new CryptographicException($"Windows CNG SHA256 失败，NTSTATUS 0x{status:X8}。");
            }
        }

        [DllImport("bcrypt.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int BCryptOpenAlgorithmProvider(
            out IntPtr algorithm, string algorithmID, string implementation, int flags);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptCreateHash(
            IntPtr algorithm, out IntPtr hash, IntPtr hashObject, int hashObjectLength,
            IntPtr secret, int secretLength, int flags);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptHashData(IntPtr hash, [In] byte[] input, int inputLength, int flags);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptFinishHash(IntPtr hash, [Out] byte[] output, int outputLength, int flags);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptDestroyHash(IntPtr hash);

        [DllImport("bcrypt.dll", ExactSpelling = true)]
        private static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm, int flags);
#endif
    }
}
