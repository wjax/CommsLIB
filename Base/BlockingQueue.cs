using System;
using System.Collections.Generic;
using System.Threading;

namespace CommsLIB.Base
{
    internal sealed class BlockingQueue<T> : IDisposable
    {
        private Queue<T> _queue = new Queue<T>();
        private SemaphoreSlim _semaphore = new SemaphoreSlim(0, int.MaxValue);
        private CancellationTokenSource cancelSource = new CancellationTokenSource();
        private CancellationToken cancelToken;

        public BlockingQueue()
        {
            cancelSource = new CancellationTokenSource();
            cancelToken = cancelSource.Token;
        }

        public void Enqueue(T data)
        {
            if (data == null) throw new ArgumentNullException();
            lock (_queue) _queue.Enqueue(data);
            _semaphore.Release();
        }

        public T Dequeue()
        {
            _semaphore.Wait(cancelToken);
            lock (_queue) return _queue.Dequeue();
        }

        public void Dispose()
        {
            if (_semaphore != null && cancelToken.CanBeCanceled)
            {
                cancelSource.Cancel();
            }

            _semaphore?.Dispose();
            _semaphore = null;
            _queue?.Clear();
            _queue = null;

        }

        public void UnBlock()
        {
            if (_semaphore != null && cancelToken.CanBeCanceled)
            {
                cancelSource.Cancel();
            }
        }
    }
}
