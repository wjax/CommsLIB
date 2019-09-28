using CommsLIB.Base;
using CommsLIB.Communications.FrameWrappers;
using MessagePack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CommsLIB.Communications.FrameWrappers.MessagePack
{
    public class MessagePackFrameWrapper<T> : SyncFrameWrapper<T>
    {
        private SpecialPipeStream pipeStreamReader;
        private MemoryStream memoryStreamTX;
        private Task readerTask;

        private volatile bool exit = false;

        T message;

        public MessagePackFrameWrapper() : base(false)
        {
            pipeStreamReader = new SpecialPipeStream(1024*1024*1024, true);
            memoryStreamTX = new MemoryStream(8192);
        }

        public override void AddBytes(byte[] bytes, int length)
        {
            pipeStreamReader.Write(bytes, 0, length);
        }

        public override byte[] Data2BytesSync(T data, out int count)
        {
            memoryStreamTX.Seek(0, SeekOrigin.Begin);
            MessagePackSerializer.Typeless.Serialize(memoryStreamTX, data);
            count = (int)memoryStreamTX.Position;

            return memoryStreamTX.GetBuffer();
        }

        public override void Start()
        {
            readerTask = new Task(()=> {

                try
                {
                    while ((message = (T)MessagePackSerializer.Typeless.Deserialize(pipeStreamReader, true)) != null && !exit)
                    {
                        FireEvent(message);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception HORRRR");
                }
                
            });
            readerTask.Start();
        }

        public override void Stop()
        {
            exit = true;
            pipeStreamReader.Dispose();
        }
    }
}
