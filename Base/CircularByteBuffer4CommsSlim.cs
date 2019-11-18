using CommsLIB.Helper;
using System;
using System.Threading;

namespace CommsLIB.Base
{
    internal sealed class CircularByteBuffer4CommsSlim : IDisposable
    {
        private const int SIZE = 4;
        public byte[] elements = null;

        public volatile int capacity = 0;
        public volatile int writePos = 0;
        public volatile int readPos = 0;
        public volatile bool flipped = false;

        private CancellationTokenSource cancelSource;
        private CancellationToken cancelToken;

        private SemaphoreSlim _semaphore;

        public CircularByteBuffer4CommsSlim(int capacity)
        {
            this.capacity = capacity;
            this.elements = new byte[capacity];
            this._semaphore = new SemaphoreSlim(0, int.MaxValue);

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

        private int RemainingCapacity()
        {
            if (!flipped)
            {
                return capacity - writePos + readPos;
            }
            return readPos - writePos;
        }

        public int Put(byte[] newElements, int length)
        {
            int newElementsReadPos = 0;
            int sizeReadPos = 0;
            // write length into first 4 bytes
            int actualLength = length + SIZE;

            lock (this)
            {
                // Check enought space
                if (actualLength > RemainingCapacity())
                {
                    Console.WriteLine("No space left");
                    return 0;

                }

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
                        // Copy Array and update indexes
                        Array.Copy(newElements, newElementsReadPos, this.elements, this.writePos, length);
                        this.writePos += length;
                        newElementsReadPos += length;
                        //for (; newElementsReadPos < length; newElementsReadPos++)
                        //{
                        //    this.elements[this.writePos++] = newElements[newElementsReadPos];
                        //}
                    }
                    else
                    {
                        //new elements must be divided between top and bottom of elements array
                        // SIZE will be copied by for loop
                        // SIZE does not fit in top part
                        if (this.writePos + SIZE > capacity)
                        {
                            //writing to top
                            for (; this.writePos < capacity; this.writePos++)
                            {
                                this.elements[this.writePos] = (byte)(length >> 8 * sizeReadPos++);
                            }

                            //writing to bottom. Flipped
                            this.writePos = 0;
                            this.flipped = true;

                            //writing to botton remaining SIZE
                            for (; sizeReadPos < SIZE; this.writePos++)
                            {
                                this.elements[this.writePos] = (byte)(length >> 8 * sizeReadPos++);
                            }

                            // Copy data by ArrayCopy
                            // Copy Array and update indexes
                            Array.Copy(newElements, newElementsReadPos, this.elements, this.writePos, length);
                            this.writePos += length;
                            newElementsReadPos += length;

                        }
                        else
                        {
                            // SIZE Fits in top
                            int endPos = this.writePos + SIZE;
                            //writing to top
                            for (; this.writePos < endPos; this.writePos++)
                            {
                                this.elements[this.writePos] = (byte)(length >> 8 * sizeReadPos++);
                            }

                            // Copy Array and update indexes
                            int copylengthTop = capacity - this.writePos;
                            Array.Copy(newElements, newElementsReadPos, this.elements, this.writePos, copylengthTop);
                            this.writePos = 0;
                            this.flipped = true;
                            newElementsReadPos += copylengthTop;

                            int copylengthDown = length - copylengthTop;
                            Array.Copy(newElements, newElementsReadPos, this.elements, this.writePos, copylengthDown);
                            this.writePos += copylengthDown;
                            newElementsReadPos += copylengthDown;
                        }

                        ////writing to top
                        //for (; this.writePos < capacity; this.writePos++)
                        //{
                        //    this.elements[this.writePos] = sizeReadPos <= 3 ? (byte)(length >> 8 * sizeReadPos++) : newElements[newElementsReadPos++];
                        //}

                        ////writing to bottom
                        //this.writePos = 0;
                        //this.flipped = true;
                        //int endPos = Math.Min(this.readPos, length - newElementsReadPos + SIZE - sizeReadPos);
                        //for (; this.writePos < endPos; this.writePos++)
                        //{
                        //    this.elements[writePos] = sizeReadPos <= 3 ? (byte)(length >> 8 * sizeReadPos++) : newElements[newElementsReadPos++];
                        //}
                    }

                }
                else
                {
                    //readPos higher than writePos - free sections are:
                    //1) from writePos to readPos
                    // Size Header
                    ByteTools.Int32Bytes(this.elements, this.writePos, length);
                    this.writePos += SIZE;
                    // Actual data
                    // Copy Array and update indexes
                    Array.Copy(newElements, newElementsReadPos, this.elements, this.writePos, length);
                    this.writePos += length;
                    newElementsReadPos += length;

                    //for (; this.writePos < endPos; this.writePos++)
                    //{
                    //    this.elements[this.writePos] = sizeReadPos <= 3 ? (byte)(length >> 8 * sizeReadPos++) : newElements[newElementsReadPos++];
                    //}
                }
                _semaphore.Release(1);
            }

            return newElementsReadPos;
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

        public int Take(byte[] into, int offset)
        {
            int frameSize = 0;
            int frameSizePos = 0;
            int intoWritePos = offset;

            _semaphore.Wait(cancelToken);

            lock (this)
            {
                if (!flipped)
                {
                    //writePos higher than readPos - available section is writePos - readPos
                    // First read frameSize. 
                    int endPosSize = this.readPos + SIZE;
                    for (; this.readPos < endPosSize; this.readPos++, frameSizePos++)
                    {
                        frameSize |= this.elements[this.readPos] << 8 * frameSizePos;
                    }

                    // Actual data
                    // Copy Array and update indexes
                    Array.Copy(this.elements, this.readPos, into, intoWritePos, frameSize);
                    this.readPos += frameSize;
                    intoWritePos += frameSize;
                    //int endPos = this.readPos + frameSize;
                    //for (; this.readPos < endPos; this.readPos++)
                    //{
                    //    into[intoWritePos++] = this.elements[this.readPos];
                    //}
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
                        Array.Copy(this.elements, this.readPos, into, intoWritePos, frameSize);
                        this.readPos += frameSize;
                        intoWritePos += frameSize;
                        //for (; intoWritePos < frameSize; intoWritePos++)
                        //{
                        //    into[intoWritePos] = this.elements[this.readPos++];
                        //}
                    }
                    else
                    {
                        //length is higher than elements available at the top of the elements array
                        //split copy into a copy from both top and bottom of elements array.

                        //copy from top
                        int endPosTop = capacity - this.readPos;
                        Array.Copy(this.elements, this.readPos, into, intoWritePos, endPosTop);
                        this.readPos = 0;
                        this.flipped = false;
                        intoWritePos += endPosTop;
                        //for (; this.readPos < capacity; this.readPos++)
                        //{
                        //    into[intoWritePos++] = this.elements[this.readPos];
                        //}

                        //copy from bottom

                        int endPosdown = frameSize - endPosTop;
                        Array.Copy(this.elements, this.readPos, into, intoWritePos, endPosdown);
                        this.readPos += endPosdown;
                        intoWritePos += endPosdown;
                        //for (; this.readPos < endPos; this.readPos++)
                        //{
                        //    into[intoWritePos++] = this.elements[this.readPos];
                        //}
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
