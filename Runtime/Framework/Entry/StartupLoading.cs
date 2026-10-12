using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework
{
    /// <summary>统一启动界面的实际执行阶段。</summary>
    public enum EStartupLoadingStage
    {
        WaitingForLoading,
        CheckingRelease,
        PreparingPackages,
        DownloadingContent,
        PreparingGameCode,
        InitializingSystems,
        ReadyForBusiness,
        InitializingBusiness,
        LoadingScene,
        OpeningHome,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>统一启动界面的控制权。</summary>
    public enum EStartupLoadingOwner
    {
        Framework,
        Business,
        Closed
    }

    /// <summary>供 AOT 展示组件读取的不可变启动快照。</summary>
    public sealed class StartupLoadingSnapshot
    {
        internal StartupLoadingSnapshot(
            int round, EStartupLoadingOwner owner, EStartupLoadingStage stage, string message,
            float stageProgress, float overallProgress, string packageName, long currentBytes, long totalBytes)
        {
            Round = round;
            Owner = owner;
            Stage = stage;
            Message = message;
            StageProgress = stageProgress;
            OverallProgress = overallProgress;
            PackageName = packageName;
            CurrentBytes = currentBytes;
            TotalBytes = totalBytes;
        }

        public int Round { get; }
        public EStartupLoadingOwner Owner { get; }
        public EStartupLoadingStage Stage { get; }
        public string Message { get; }
        public float StageProgress { get; }
        public float OverallProgress { get; }
        public string PackageName { get; }
        public long CurrentBytes { get; }
        public long TotalBytes { get; }
        public bool IsVisible => Owner != EStartupLoadingOwner.Closed || Stage == EStartupLoadingStage.Failed;
    }

    /// <summary>启动失败详情；内部异常只写日志，展示使用可信映射文案。</summary>
    public sealed class StartupLoadingError
    {
        internal StartupLoadingError(int round, string stepName, string message, Exception exception)
        {
            Round = round;
            StepName = stepName;
            Message = message;
            Exception = exception;
            Code = exception.GetBaseException().GetType().Name;
        }

        public int Round { get; }
        public string StepName { get; }
        public string Message { get; }
        public string Code { get; }
        public Exception Exception { get; }
        public bool CanRetry => false;
    }

    /// <summary>不依赖业务程序集、资源包和完整 UI 系统的展示契约。</summary>
    public interface IStartupLoadingPresenter
    {
        bool IsReady { get; }

        /// <summary>显示最新快照及本轮唯一错误。</summary>
        /// <param name="snapshot">最新启动状态。</param>
        /// <param name="error">失败详情；正常启动时为空。</param>
        void Render(StartupLoadingSnapshot snapshot, StartupLoadingError error);

        /// <summary>释放持久化展示对象及输入组件。</summary>
        void Release();
    }

    /// <summary>业务持有的本轮 Loading 控制权；旧会话不能更新新一轮。</summary>
    public interface IStartupLoadingSession : IDisposable
    {
        /// <summary>开始下一个真实业务步骤。</summary>
        /// <param name="stage">实际执行阶段。</param>
        /// <param name="message">步骤提示。</param>
        void BeginStep(EStartupLoadingStage stage, string message);

        /// <summary>报告当前步骤的真实进度。</summary>
        /// <param name="progress">零到一之间的步骤进度。</param>
        void Report(float progress);

        /// <summary>完成当前步骤并累计其权重。</summary>
        void CompleteStep();

        /// <summary>冻结进度并显示当前步骤错误。</summary>
        /// <param name="exception">保留完整日志的原始异常。</param>
        void Fail(Exception exception);

        /// <summary>关闭同一个 Loading；重复关闭安全。</summary>
        void Close();
    }

    /// <summary>集中管理启动快照、步骤权重、控制权与唯一错误。</summary>
    public static class StartupLoading
    {
        private const float FRAMEWORKMINIMUM = 0.01f; // 框架阶段显示从百分之一开始。
        private const float FRAMEWORKMAXIMUM = 0.99f; // 入口准备成功前不能显示百分之百。
        private const float READINESSTIMEOUT = 15f; // 展示缺失时给出诊断，避免无限等待。
        private const float PACKAGEPREPARATIONWEIGHT = 0.3f; // 包初始化、版本与清单占当前步骤的固定份额。
        private const float PACKAGEDOWNLOADWEIGHT = 0.4f; // 各包下载使用固定份额，不因发现新包而倒退。
        private static IStartupLoadingPresenter m_presenter; // 唯一 AOT 展示对象。
        private static StartupLoadingSnapshot m_snapshot; // 注册时立即重放的最新状态。
        private static StartupLoadingError m_error; // 本轮去重后的唯一错误。
        private static int m_round; // 单调增长，隔离旧异步回调。
        private static int m_stepCount; // 当前控制方的固定步骤总数。
        private static int m_completedSteps; // 已真实完成的步骤数。
        private static bool m_stepActive; // 当前是否有尚未完成的步骤。
        private static bool m_businessReady; // 框架已明确交出控制权。
        private static string m_stepName; // 当前逻辑步骤名称。
        private static double m_stepStartedAt; // 当前逻辑步骤的真实开始时间。
        private static CancellationToken m_cancellationToken; // 拒绝退出后的旧进度回调。

        public static StartupLoadingSnapshot Snapshot => m_snapshot;
        public static StartupLoadingError Error => m_error;

        /// <summary>注册唯一展示对象并同步已发生的状态。</summary>
        /// <param name="presenter">随主包内置的展示对象。</param>
        /// <exception cref="ArgumentNullException">展示对象为空。</exception>
        /// <exception cref="InvalidOperationException">已有不同的展示对象。</exception>
        public static void Register(IStartupLoadingPresenter presenter)
        {
            if (presenter == null) throw new ArgumentNullException(nameof(presenter));
            if (m_presenter != null && !ReferenceEquals(m_presenter, presenter))
                throw new InvalidOperationException("只能注册一个启动 Loading。");
            m_presenter = presenter;
            Render();
        }

        /// <summary>解除被销毁的展示对象，避免静态引用残留。</summary>
        /// <param name="presenter">需要解除的展示对象。</param>
        public static void Unregister(IStartupLoadingPresenter presenter)
        {
            if (ReferenceEquals(m_presenter, presenter)) m_presenter = null;
        }

        /// <summary>取得本轮业务控制权并显式重置为零进度。</summary>
        /// <param name="stepCount">业务真实步骤总数，各步骤使用相同权重。</param>
        /// <returns>仅对本轮有效的业务会话。</returns>
        /// <exception cref="ArgumentOutOfRangeException">步骤总数不是正数。</exception>
        /// <exception cref="InvalidOperationException">框架尚未交接或已经接管。</exception>
        public static IStartupLoadingSession TakeOver(int stepCount)
        {
            if (stepCount <= 0) throw new ArgumentOutOfRangeException(nameof(stepCount));
            if (!m_businessReady || !CanUpdate() || m_snapshot.Owner != EStartupLoadingOwner.Framework)
                throw new InvalidOperationException("框架尚未交出启动 Loading 控制权，或业务已接管。");
            m_stepCount = stepCount;
            m_completedSteps = 0;
            m_stepActive = false;
            Publish(EStartupLoadingOwner.Business, EStartupLoadingStage.InitializingBusiness,
                "准备业务启动", 0f, 0f);
            return new BusinessSession(m_round);
        }

        /// <summary>更新真实资源子阶段，不改变逻辑步骤的累计权重。</summary>
        /// <param name="stage">资源或代码子阶段。</param>
        /// <param name="message">可信的阶段提示。</param>
        /// <param name="progress">当前逻辑步骤内的进度。</param>
        /// <param name="packageName">当前资源包；其他阶段可为空。</param>
        /// <param name="currentBytes">已下载字节数。</param>
        /// <param name="totalBytes">真实总字节数；未知时为零。</param>
        public static void ReportOperation(
            EStartupLoadingStage stage, string message, float progress = 0f,
            string packageName = null, long currentBytes = 0, long totalBytes = 0)
        {
            if (!CanUpdate() || !m_stepActive ||
                (m_snapshot.Owner == EStartupLoadingOwner.Framework && m_businessReady)) return;
            float fraction = Normalize(progress);
            float overall = CalculateProgress(fraction);
            Publish(m_snapshot.Owner, stage, message, fraction, overall, packageName, currentBytes, totalBytes);
        }

        /// <summary>按集中配置的子阶段权重报告资源包准备或下载。</summary>
        /// <param name="stage">包准备或下载阶段。</param>
        /// <param name="message">实际子步骤提示。</param>
        /// <param name="packageName">当前包名称。</param>
        /// <param name="progress">当前子阶段进度。</param>
        /// <param name="currentBytes">已下载字节数。</param>
        /// <param name="totalBytes">真实总字节数；未知时为零。</param>
        public static void ReportPackage(
            EStartupLoadingStage stage, string message, string packageName, float progress = 0f,
            long currentBytes = 0, long totalBytes = 0)
        {
            float fraction = Normalize(progress);
            float weightedProgress = stage == EStartupLoadingStage.DownloadingContent
                ? PACKAGEPREPARATIONWEIGHT + PACKAGEDOWNLOADWEIGHT * fraction
                : PACKAGEPREPARATIONWEIGHT * fraction;
            ReportOperation(stage, message, weightedProgress, packageName, currentBytes, totalBytes);
        }

        internal static async UniTask BeginAsync(CancellationToken cancellationToken)
        {
            m_round++;
            m_error = null;
            m_businessReady = false;
            m_stepActive = false;
            m_stepCount = 1;
            m_completedSteps = 0;
            m_cancellationToken = cancellationToken;
            Publish(EStartupLoadingOwner.Framework, EStartupLoadingStage.WaitingForLoading,
                "准备启动", 0f, FRAMEWORKMINIMUM);

            // 异步让出主线程，使 Boot 的 Awake 和绘制能够完成。
            float startedAt = Time.realtimeSinceStartup;
            while (m_presenter == null || !m_presenter.IsReady)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartup - startedAt >= READINESSTIMEOUT)
                    throw new TimeoutException("启动 Loading 未就绪，请检查 Boot 场景及内置展示组件。");
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            FrameworkBootstrap.DestroyStartupScreen();
        }

        internal static void ConfigureSteps(int stepCount)
        {
            m_stepCount = Math.Max(1, stepCount);
        }

        internal static void BeginFrameworkStep(EStartupLoadingStage stage, string message)
        {
            if (!CanUpdate() || m_snapshot.Owner != EStartupLoadingOwner.Framework || m_businessReady) return;
            BeginStep(stage, message);
        }

        internal static void CompleteFrameworkStep()
        {
            if (!CanUpdate() || m_snapshot.Owner != EStartupLoadingOwner.Framework || m_businessReady) return;
            CompleteStep();
        }

        internal static void HandOver()
        {
            m_businessReady = true;
            m_stepActive = false;
            Publish(EStartupLoadingOwner.Framework, EStartupLoadingStage.ReadyForBusiness, "准备完成", 1f, 1f);
        }

        internal static void Fail(Exception exception)
        {
            // 多层捕获只记录一个错误；取消后的真实异常仍需保留诊断。
            if (m_snapshot == null || m_error != null) return;
            string name = m_snapshot.Message ?? m_stepName ?? "准备启动";
            string reason = GetSafeReason(exception);
            m_error = new StartupLoadingError(m_round, name, $"{name}失败：{reason}", exception);
            if (m_stepActive)
                AlloyDebug.Error($"[StartupLoading] {name} 失败，耗时 " +
                    $"{(Time.realtimeSinceStartupAsDouble - m_stepStartedAt) * 1000d:F1} ms");
            m_stepActive = false;
            AlloyDebug.Error(exception);
            Publish(m_snapshot.Owner, EStartupLoadingStage.Failed, name,
                m_snapshot.StageProgress, m_snapshot.OverallProgress,
                m_snapshot.PackageName, m_snapshot.CurrentBytes, m_snapshot.TotalBytes);
        }

        internal static void Cancel()
        {
            if (m_snapshot == null || m_error != null) return;
            m_stepActive = false;
            Publish(EStartupLoadingOwner.Closed, EStartupLoadingStage.Cancelled,
                "启动已取消", m_snapshot.StageProgress, m_snapshot.OverallProgress);
            Release();
        }

        internal static void Release()
        {
            // 失败清理保留 AOT 弹窗，直到用户真正退出。
            if (m_error != null) return;
            var presenter = m_presenter;
            m_presenter = null;
            presenter?.Release();
        }

        private static void BeginStep(EStartupLoadingStage stage, string message)
        {
            if (m_stepActive || m_completedSteps >= m_stepCount)
                throw new InvalidOperationException("启动步骤未完成或超出配置的步骤总数。");
            m_stepName = message;
            m_stepStartedAt = Time.realtimeSinceStartupAsDouble;
            m_stepActive = true;
            AlloyDebug.Log($"[StartupLoading] 开始：{message}");
            ReportOperation(stage, message);
        }

        private static void CompleteStep()
        {
            if (!m_stepActive) return;
            AlloyDebug.Log($"[StartupLoading] 完成：{m_stepName}，耗时 " +
                $"{(Time.realtimeSinceStartupAsDouble - m_stepStartedAt) * 1000d:F1} ms");
            ReportOperation(m_snapshot.Stage, m_stepName, 1f);
            m_completedSteps++;
            m_stepActive = false;
        }

        private static bool CanUpdate()
        {
            return m_snapshot != null && m_error == null && !m_cancellationToken.IsCancellationRequested &&
                m_snapshot.Owner != EStartupLoadingOwner.Closed;
        }

        private static float CalculateProgress(float fraction)
        {
            float progress = (m_completedSteps + fraction) / m_stepCount;
            if (m_snapshot.Owner == EStartupLoadingOwner.Framework)
                progress = Mathf.Lerp(FRAMEWORKMINIMUM, FRAMEWORKMAXIMUM, progress);
            return Mathf.Max(m_snapshot.OverallProgress, progress);
        }

        private static float Normalize(float progress)
        {
            return float.IsNaN(progress) || float.IsInfinity(progress) ? 0f : Mathf.Clamp01(progress);
        }

        private static void Publish(
            EStartupLoadingOwner owner, EStartupLoadingStage stage, string message, float stageProgress,
            float overallProgress, string packageName = null, long currentBytes = 0, long totalBytes = 0)
        {
            m_snapshot = new StartupLoadingSnapshot(m_round, owner, stage, message,
                stageProgress, overallProgress, packageName, currentBytes, totalBytes);
            Render();
        }

        private static void Render()
        {
            // 展示故障不能替换原始启动异常，完整日志始终保留。
            try { m_presenter?.Render(m_snapshot, m_error); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private static string GetSafeReason(Exception exception)
        {
            string message = exception.GetBaseException().Message ?? string.Empty;
            if (exception.GetBaseException() is TimeoutException ||
                message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 || message.Contains("超时"))
                return "请求超时，请检查网络后重新启动游戏。";
            if (message.Contains("404")) return "所需文件不存在，请联系运营人员后重新启动游戏。";
            if (message.Contains("基线") || message.Contains("不匹配"))
                return "发布内容与当前主包不兼容，请安装匹配的主包。";
            if (message.Contains("哈希") || message.Contains("SHA256"))
                return "文件完整性校验失败，请重新启动游戏。";
            if (message.Contains("主包身份缺失")) return "主包身份缺失，请重新构建并安装完整主包。";
            if (message.Contains("资源包") && message.Contains("缺少"))
                return "发布内容缺少当前主包必需的资源包，请联系运营人员。";
            if (message.Contains("重复") || message.Contains("非法"))
                return "启动配置或发布清单无效，请检查配置后重新启动游戏。";
            if (message.Contains("清单") || message.IndexOf("manifest", StringComparison.OrdinalIgnoreCase) >= 0)
                return "资源清单不可用，请检查发布文件后重新启动游戏。";
            if (message.Contains("尚未就绪")) return "游戏首页未完成打开，请查看日志并重新启动游戏。";
            if (message.IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0 || message.Contains("连接"))
                return "无法连接服务器，请检查网络后重新启动游戏。";
            return "当前步骤无法完成，请查看日志并重新启动游戏。";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // 即使关闭域重载，也解除上一轮的静态持有关系。
            m_presenter = null;
            m_snapshot = null;
            m_error = null;
            m_round++;
            m_businessReady = false;
            m_stepActive = false;
            m_stepCount = 0;
            m_completedSteps = 0;
            m_stepName = null;
            m_stepStartedAt = 0d;
            m_cancellationToken = default;
        }

        private sealed class BusinessSession : IStartupLoadingSession
        {
            private readonly int m_sessionRound; // 会话所属的启动轮次。
            private bool m_disposed; // 释放后拒绝所有回调。

            internal BusinessSession(int round) { m_sessionRound = round; }

            /// <summary>开始一个业务步骤。</summary>
            /// <param name="stage">业务阶段。</param>
            /// <param name="message">步骤提示。</param>
            /// <exception cref="InvalidOperationException">上一业务步骤未完成或步骤总数已用尽。</exception>
            public void BeginStep(EStartupLoadingStage stage, string message)
            {
                if (IsCurrent()) StartupLoading.BeginStep(stage, message);
            }

            /// <summary>报告当前步骤进度。</summary>
            /// <param name="progress">零到一的真实进度。</param>
            public void Report(float progress)
            {
                if (IsCurrent()) ReportOperation(m_snapshot.Stage, m_stepName, progress);
            }

            /// <summary>完成当前业务步骤。</summary>
            public void CompleteStep() { if (IsCurrent()) StartupLoading.CompleteStep(); }

            /// <summary>记录当前步骤错误。</summary>
            /// <param name="exception">原始异常。</param>
            public void Fail(Exception exception)
            {
                if (!m_disposed && m_sessionRound == m_round) StartupLoading.Fail(exception);
            }

            /// <summary>关闭统一 Loading。</summary>
            public void Close()
            {
                if (!IsCurrent()) return;
                m_stepActive = false;
                Publish(EStartupLoadingOwner.Closed, EStartupLoadingStage.Completed,
                    "业务启动完成", 1f, m_snapshot.OverallProgress);
                Release();
            }

            /// <summary>解除业务持有关系；失败时保留错误展示。</summary>
            public void Dispose() { Close(); m_disposed = true; }

            private bool IsCurrent()
            {
                return !m_disposed && m_sessionRound == m_round && CanUpdate() &&
                    m_snapshot.Owner == EStartupLoadingOwner.Business;
            }
        }
    }
}
