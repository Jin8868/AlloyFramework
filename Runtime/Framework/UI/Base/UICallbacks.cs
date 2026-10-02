using System;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.UI
{
    internal static class UICallbacks
    {
        public static void Run(Func<UniTask> taskFactory, Action<Exception> onCompleted)
        {
            try { Run(taskFactory(), onCompleted); }
            catch (Exception exception)
            {
                if (onCompleted != null) onCompleted(exception);
                else AlloyDebug.Error(exception);
            }
        }

        public static void Run(UniTask task, Action<Exception> onCompleted)
        {
            CompleteAsync(task, onCompleted).Forget();
        }

        public static void Run<T>(UniTask<T> task, Action<T, Exception> onCompleted)
        {
            CompleteAsync(task, onCompleted).Forget();
        }

        private static async UniTask CompleteAsync(UniTask task, Action<Exception> onCompleted)
        {
            Exception error = null;
            try { await task; }
            catch (Exception exception) { error = exception; }
            if (onCompleted != null) onCompleted(error);
            else if (error != null) AlloyDebug.Error(error);
        }

        private static async UniTask CompleteAsync<T>(UniTask<T> task, Action<T, Exception> onCompleted)
        {
            T value = default;
            Exception error = null;
            try { value = await task; }
            catch (Exception exception) { error = exception; }
            if (onCompleted != null) onCompleted(value, error);
            else if (error != null) AlloyDebug.Error(error);
        }
    }
}
