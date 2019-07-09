using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace CommsLIB.Base
{
    internal sealed class CircularBuffer
    {
        public byte[] elements = null;

        public volatile int capacity = 0;
        public volatile int writePos = 0;
        public volatile int readPos = 0;
        public volatile bool flipped = false;
        private SemaphoreSlim _semaphore;

        public CircularBuffer(int capacity)
        {
            this.capacity = capacity;
            this.elements = new byte[capacity];
            this. _semaphore = new SemaphoreSlim(0, int.MaxValue);
        }

        public void reset()
        {
            this.writePos = 0;
            this.readPos = 0;
            this.flipped = false;
            this._semaphore = new SemaphoreSlim(0, int.MaxValue);
        }

        private int available()
        {
            if (!flipped)
            {
                return writePos - readPos;
            }
            return capacity - readPos + writePos;
        }

        public int remainingCapacity()
        {
            //        if(!flipped){
            //            return capacity - writePos;
            //        }
            //        return readPos - writePos;

            if (!flipped)
            {
                return capacity - writePos + readPos;
            }
            return readPos - writePos;
        }

        public bool put(byte element)
        {
            bool result = false;

            lock(this)
            {
                if (!flipped)
                {
                    if (writePos == capacity)
                    {
                        writePos = 0;
                        flipped = true;

                        if (writePos < readPos)
                        {
                            elements[writePos++] = element;
                            result = true;
                        }
                        else
                        {
                            result = false;
                        }
                    }
                    else
                    {
                        elements[writePos++] = element;
                        result = true;
                    }
                }
                else
                {
                    if (writePos < readPos)
                    {
                        elements[writePos++] = element;
                        result = true;
                    }
                    else
                    {
                        result = false;
                    }
                }
                if (result)
                    _semaphore.Release(1);
            }
    
            return result;
        }

        public int put(byte[] newElements, int length)
        {
            int newElementsReadPos = 0;

            lock (this)
            {
                if (!flipped)
                {
                    //readPos lower than writePos - free sections are:
                    //1) from writePos to capacity
                    //2) from 0 to readPos

                    if (length <= capacity - writePos)
                    {
                        //new elements fit into top of elements array - copy directly
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
                            this.elements[this.writePos] = newElements[newElementsReadPos++];
                        }

                        //writing to bottom
                        this.writePos = 0;
                        this.flipped = true;
                        int endPos = Math.Min(this.readPos, length - newElementsReadPos);
                        for (; this.writePos < endPos; this.writePos++)
                        {
                            this.elements[writePos] = newElements[newElementsReadPos++];
                        }
                    }

                }
                else
                {
                    //readPos higher than writePos - free sections are:
                    //1) from writePos to readPos

                    int endPos = Math.Min(this.readPos, this.writePos + length);

                    for (; this.writePos < endPos; this.writePos++)
                    {
                        this.elements[this.writePos] = newElements[newElementsReadPos++];
                    }
                }
                _semaphore.Release(newElementsReadPos);
            }

            return newElementsReadPos;
        }


        public byte take()
        {
            byte result = 0;
            _semaphore.Wait();
            
            lock (this)
            {
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

    //    public int take(byte[] into, int length)
    //    {
    //        _semaphore.acquire(length);
    //	    lock.lock();
    	
    //        int intoWritePos = 0;
    //        if(!flipped){
    //        //writePos higher than readPos - available section is writePos - readPos
    //        int endPos = Math.min(this.writePos, this.readPos + length);
    //        for (; this.readPos < endPos; this.readPos++)
    //        {
    //            into[intoWritePos++] = this.elements[this.readPos];
    //        }
    //    } else {
    //        //readPos higher than writePos - available sections are
    //        //top + bottom of elements array

    //        if (length <= capacity - readPos)
    //        {
    //            //length is lower than the elements available at the top
    //            //of the elements array - copy directly
    //            for (; intoWritePos < length; intoWritePos++)
    //            {
    //                into[intoWritePos] = this.elements[this.readPos++];
    //            }
    //        }
    //        else
    //        {
    //            //length is higher than elements available at the top of the elements array
    //            //split copy into a copy from both top and bottom of elements array.

    //            //copy from top
    //            for (; this.readPos < capacity; this.readPos++)
    //            {
    //                into[intoWritePos++] = this.elements[this.readPos];
    //            }

    //            //copy from bottom
    //            this.readPos = 0;
    //            this.flipped = false;
    //            int endPos = Math.min(this.writePos, length - intoWritePos);
    //            for (; this.readPos < endPos; this.readPos++)
    //            {
    //                into[intoWritePos++] = this.elements[this.readPos];
    //            }
    //        }
    //    }
    //        lock.unlock();
    //        return intoWritePos;
    //}
    }
}
