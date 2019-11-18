using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CommsLIB.Base
{
    public class SpecialPipeStream : Stream, IDisposable
    {
        CircularByteBuffer internalBuffer;

        public SpecialPipeStream(int capacity, bool blocking)
        {
            internalBuffer = new CircularByteBuffer(capacity, blocking);

        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => internalBuffer.capacity;

        public override long Position {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override void Flush()
        {
            
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int  read = internalBuffer.take(buffer, offset, count);
            //Console.WriteLine($"Read {offset} {count} {read}");
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            int put = internalBuffer.put(buffer, offset, count);
            //Console.WriteLine($"Put {offset} {count} {put}");
        }

        public new void Dispose()
        {
            base.Dispose();
            internalBuffer.reset();
        }

    }
}
