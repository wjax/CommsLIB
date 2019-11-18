using System;
using System.Collections.Generic;
using System.Threading;

namespace CommsLIB.Base
{
    internal sealed class BlockingByteQueue : IDisposable, ICommsQueue
    {
        private Queue<byte[]> _queue = new Queue<byte[]>();
        private SemaphoreSlim _semaphore;
        private CancellationTokenSource cancelSource;
        private CancellationToken cancelToken;

        public BlockingByteQueue()
        {
            _semaphore = new SemaphoreSlim(0, int.MaxValue);
            cancelSource = new CancellationTokenSource();
            cancelToken = cancelSource.Token;
        }

        public int Put(byte[] data, int length)
        {
            if (data == null) throw new ArgumentNullException();
            if (data.Length != length) throw new ArgumentException("Collection does not support Length");
            lock (this) _queue.Enqueue(data);
            _semaphore.Release();

            return data.Length;
        }

        public int Take(ref byte[] bufferOut, int offset)
        {
            if (offset != 0) throw new ArgumentException("Collection does not support Offset");
            _semaphore.Wait(cancelToken);
            lock (this) bufferOut = _queue.Dequeue();

            return bufferOut.Length;
        }

        public void Dispose()
        {
            UnBlock();

            _semaphore?.Dispose();
            _semaphore = null;
            _queue?.Clear();
            _queue = null;

        }

        public void Reset()
        {
            lock (this)
            {
                UnBlock();

                _semaphore = new SemaphoreSlim(0, int.MaxValue);
                cancelSource = new CancellationTokenSource();
                cancelToken = cancelSource.Token;

                _queue.Clear();
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
