using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Org.Openfeed.Client {
    /// <summary>
    /// When an instance of this struct is awaited it schedules the continuation
    /// to run on the tread pool.
    /// </summary>
    public struct ContinueOnThreadPool : INotifyCompletion {
        private static WaitCallback? _waitCallbackDelegate;

        public bool IsCompleted => false;

        public void GetResult() { }

        public void OnCompleted(Action continuation) {
            if (_waitCallbackDelegate == null) _waitCallbackDelegate = OnWaitCallback;
            ThreadPool.QueueUserWorkItem(_waitCallbackDelegate, continuation);
        }

        private static void OnWaitCallback(object obj) => ((Action)obj)();

        public ContinueOnThreadPool GetAwaiter() => default;

        public static ContinueOnThreadPool Instance => default;
    }

    /// <summary>
    /// Given a <see cref="CancellationToken"/> provides a <see cref="Task"/> that's cancelled when the token
    /// is cancelled.
    /// </summary>
    struct CancellationAwaiter : IDisposable {
        private readonly CancellationTokenRegistration _registration;
        private static Action<object>? _onCancellationDelegate;

        public readonly Task Task;

        public CancellationAwaiter(CancellationToken ct, bool useSynchronizationContext) {
            if (_onCancellationDelegate == null) _onCancellationDelegate = OnCancellation;

            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _registration = ct.Register(_onCancellationDelegate, source, useSynchronizationContext);
            Task = source.Task;
        }

        public void Dispose() => _registration.Dispose();

        private static void OnCancellation(object state) =>
            ((TaskCompletionSource<int>)state).SetCanceled();
    }
}
