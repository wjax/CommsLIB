using CommsLIB.Helper;
using System;
using System.Threading;

namespace CommsLIB.Base
{
    internal sealed class CircularByteBuffer4Comms : IDisposable, ICommsQueue
    {
        #region logger
        //private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        #endregion

        private const int SIZE = 4;
        public byte[] elements = null;

        public volatile int capacity = 0;
        public volatile int writePos = 0;
        public volatile int readPos = 0;
        public volatile bool flipped = false;

        private CancellationTokenSource cancelSource;
        private CancellationToken cancelToken;

        private SemaphoreSlim _semaphore;

        public CircularByteBuffer4Comms(int capacity)
        {
            this.capacity = capacity;
            this.elements = new byte[capacity];
            this. _semaphore = new SemaphoreSlim(0, int.MaxValue);

            cancelSource = new CancellationTokenSource();
            cancelToken = cancelSource.Token;
        }

        public void Reset()
        {
            lock (this)
            {
                UnBlock();

                this.writePos = 0;
                this.readPos = 0;
                this.flipped = false;
                this._semaphore = new SemaphoreSlim(0, int.MaxValue);

                cancelSource = new CancellationTokenSource();
                cancelToken = cancelSource.Token;
            }
            
        }

        private int Available()
        {
            if (!flipped)
            {
                return writePos - readPos;
            }
            return capacity - readPos + writePos;
        }

        public int RemainingCapacity()
        {
            if (!flipped)
            {
                return capacity - writePos + readPos;
            }
            return readPos - writePos;
        }

        //public bool put(byte element)
        //{
        //    bool result = false;

        //    lock(this)
        //    {
        //        if (!flipped)
        //        {
        //            if (writePos == capacity)
        //            {
        //                writePos = 0;
        //                flipped = true;

        //                if (writePos < readPos)
        //                {
        //                    elements[writePos++] = element;
        //                    result = true;
        //                }
        //                else
        //                {
        //                    result = false;
        //                }
        //            }
        //            else
        //            {
        //                elements[writePos++] = element;
        //                result = true;
        //            }
        //        }
        //        else
        //        {
        //            if (writePos < readPos)
        //            {
        //                elements[writePos++] = element;
        //                result = true;
        //            }
        //            else
        //            {
        //                result = false;
        //            }
        //        }
        //        if (result)
        //            _semaphore.Release(1);
        //    }
    
        //    return result;
        //}

        public int Put(byte[] newElements, int length)
        {
            int newElementsReadPos = 0;
            int sizeReadPos = 0;
            // write length into first 4 bytes
            int actualLength = length + SIZE;

            lock (this)
            {
                if (RemainingCapacity() < length)
                    return 0;

                //logger.Trace("Put. remCap = " + remainingCapacity());
                if (!flipped)
                {
                    //readPos lower than writePos - free sections are:
                    //1) from writePos to capacity
                    //2) from 0 to readPos

                    if (actualLength <= capacity - writePos)
                    {
                        //new elements fit into top of elements array - copy directly
                        // Size Header
                        ByteTools.Int32Bytes(this.elements, this.writePos, length);
                        this.writePos += SIZE;
                        // Actual data
                        for (; newElementsReadPos < length; newElementsReadPos++)
                        {
                            this.elements[this.writePos++] = newElements[newElementsReadPos];
                        }
                    }
                    else
                    {
                        //new elements must be divided between top and bottom of elements array

                        //writing to top
                        for (; this.writePos < capacity; this.writePos++)
                        {
                            this.elements[this.writePos] = sizeReadPos <= 3 ? (byte)(length >> 8 * sizeReadPos++) : newElements[newElementsReadPos++];
                        }

                        //writing to bottom
                        this.writePos = 0;
                        this.flipped = true;
                        int endPos = Math.Min(this.readPos, length - newElementsReadPos + SIZE - sizeReadPos);
                        for (; this.writePos < endPos; this.writePos++)
                        {
                            this.elements[writePos] = sizeReadPos <= 3 ? (byte)(length >> 8 * sizeReadPos++) : newElements[newElementsReadPos++];
                        }
                    }

                }
                else
                {
                    //readPos higher than writePos - free sections are:
                    //1) from writePos to readPos

                    int endPos = Math.Min(this.readPos, this.writePos + length + SIZE);

                    for (; this.writePos < endPos; this.writePos++)
                    {
                        this.elements[this.writePos] = sizeReadPos <= 3 ? (byte)(length >> 8 * sizeReadPos++) : newElements[newElementsReadPos++];
                    }
                }
                //_semaphore.Release(newElementsReadPos);
                _semaphore.Release(1);
            }

            return newElementsReadPos;
        }


        public byte Take()
        {
            byte result = 0;
            _semaphore.Wait(cancelToken);

            lock (this)
            {
                //logger.Trace("Take. remCap = " + remainingCapacity());
                if (!flipped)
                {
                    if (readPos < writePos)
                    {
                        result = elements[readPos++];
                    }
                }
                else
                {
                    if (readPos == capacity)
                    {
                        readPos = 0;
                        flipped = false;

                        if (readPos < writePos)
                        {
                            result = elements[readPos++];
                        }
                    }
                    else
                    {
                        result = elements[readPos++];
                    }
                }
            }
            return result;
        }

        private byte TakeNonLock()
        {
            byte result = 0;

            if (!flipped)
            {
                if (readPos < writePos)
                {
                    result = elements[readPos++];
                }
            }
            else
            {
                if (readPos == capacity)
                {
                    readPos = 0;
                    flipped = false;

                    if (readPos < writePos)
                    {
                        result = elements[readPos++];
                    }
                }
                else
                {
                    result = elements[readPos++];
                }
            }

            return result;
        }

        public int Take(ref byte[] into, int offset)
        {
            _semaphore.Wait(cancelToken);

            int frameSize = 0;
            int frameSizePos = 0;
            int intoWritePos = offset;

            lock (this)
            {
                //logger.Trace("Take. remCap = " + remainingCapacity());
                if (!flipped)
                {
                    //writePos higher than readPos - available section is writePos - readPos
                    // First read frameSize
                    int endPosSize = Math.Min(this.writePos, this.readPos + SIZE);
                    for (; this.readPos < endPosSize; this.readPos++, frameSizePos++)
                    {
                        frameSize |= this.elements[this.readPos] << 8*frameSizePos;
                    }

                    //Dirty check
                    if (frameSize > into.Length - offset)
                        throw new IndexOutOfRangeException();

                    int endPos = Math.Min(this.writePos, this.readPos + frameSize);
                    for (; this.readPos < endPos; this.readPos++)
                    {
                        into[intoWritePos++] = this.elements[this.readPos];
                    }
                }
                else
                {
                    //readPos higher than writePos - available sections are
                    //top + bottom of elements array

                    // Get 4 bytes for the length of this frame
                    for (; frameSizePos < SIZE; frameSizePos++)
                    {
                        frameSize |= TakeNonLock() << 8 * frameSizePos;
                    }

                    if (frameSize <= capacity - readPos)
                    {
                        //length is lower than the elements available at the top
                        //of the elements array - copy directly
                        for (; intoWritePos < frameSize; intoWritePos++)
                        {
                            into[intoWritePos] = this.elements[this.readPos++];
                        }
                    }
                    else
                    {
                        //length is higher than elements available at the top of the elements array
                        //split copy into a copy from both top and bottom of elements array.

                        //copy from top
                        for (; this.readPos < capacity; this.readPos++)
                        {
                            into[intoWritePos++] = this.elements[this.readPos];
                        }

                        //copy from bottom
                        this.readPos = 0;
                        this.flipped = false;
                        int endPos = Math.Min(this.writePos, frameSize - intoWritePos);
                        for (; this.readPos < endPos; this.readPos++)
                        {
                            into[intoWritePos++] = this.elements[this.readPos];
                        }
                    }
                }
            }


            return intoWritePos - offset;
        }

        public void Dispose()
        {
            if (_semaphore != null && cancelToken.CanBeCanceled)
            {
                cancelSource.Cancel();
            }

            _semaphore?.Dispose();
            _semaphore = null;

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
