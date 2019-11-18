using CommsLIB.Base;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CommsLIB.Communications.FrameWrappers
{
    public abstract class AsyncFrameWrapper<T> : FrameWrapperBase<T>
    {
        private int CAPACITY = 65536;
        private Task parseDataTask;
        private bool exit = false;

        private CircularByteBuffer circularBuffer;

        public AsyncFrameWrapper(int _circularBufferCapacity, bool _useThreadPool4Event) 
            :base(_useThreadPool4Event)
        {
            CAPACITY = _circularBufferCapacity;
        }

        public override void AddBytes(byte[] bytes, int length)
        {
            circularBuffer?.put(bytes, length);
        }

        public override void Start()
        {
            Stop();

            exit = false;
            parseDataTask = new Task(() => ParseDataCallback());
            parseDataTask.Start();
        }

        public override void Stop()
        {
            exit = true;
            parseDataTask?.Wait();
            parseDataTask?.Dispose();
        }

        public void ParseDataCallback()
        {
            while (!exit)
            {
                try
                {

                }
                finally
                {

                }
            }
        }

        public abstract void ParseByte();
    }
}
