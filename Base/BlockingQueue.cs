using System;
using System.Collections.Generic;
using System.Threading;

namespace CommsLIB.Base
{
    internal sealed class BlockingQueue<T> : IDisposable
    {
        private Queue<T> _queue = new Queue<T>();
        private SemaphoreSlim _semaphore;
        private CancellationTokenSource cancelSource;
        private CancellationToken cancelToken;

        public BlockingQueue()
        {
            _semaphore = new SemaphoreSlim(0, int.MaxValue);
            cancelSource = new CancellationTokenSource();
            cancelToken = cancelSource.Token;
        }

        public void Enqueue(T data)
        {
            if (data == null) throw new ArgumentNullException();
            lock (this) _queue.Enqueue(data);
            _semaphore.Release();
        }

        public T Dequeue()
        {
            _semaphore.Wait(cancelToken);
            lock (this) return _queue.Dequeue();
        }

        public void Dispose()
        {
            UnBlock();

            _semaphore?.Dispose();
            _semaphore = null;
            _queue?.Clear();
            _queue = null;

        }

        public void reset()
        {
            lock (this)
            {
                UnBlock();

                _semaphore = new SemaphoreSlim(0, int.MaxValue);
                cancelSource = new CancellationTokenSource();
                cancelToken = cancelSource.Token;
            }
           
        }

        private void UnBlock()
        {
            if (_semaphore != null && cancelToken.CanBeCanceled)
            {
                cancelSource.Cancel();
            }
        }
    }
}
