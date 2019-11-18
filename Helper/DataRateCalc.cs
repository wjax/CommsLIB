using CommsLIB.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace CommsLIB.Helper
{
    public class DataRateCalc
    {
        //long _lastTime = long.MinValue;
        //MovingAverage mBPerSecAverage;
        CircularBuffer<int> data;
        CircularBuffer<long> times;

        public DataRateCalc(int size)
        {
            //mBPerSecAverage = new MovingAverage(size);
            data = new CircularBuffer<int>(size);
            times = new CircularBuffer<long>(size);
        }

        /// <summary>
        /// Add new chunk of bytes and its time of reception
        /// </summary>
        /// <param name="bytes">bytes received/processed</param>
        /// <param name="time">time in microseconds for this chunk</param>
        /// <returns></returns>
        public void Add(int bytes, long time)
        {
            // First Time
            //if (_lastTime == long.MinValue)
            //{
            //    _lastTime = time;
            //    return 0f;
            //}

            //// Make calculation
            //var diffTime = time - _lastTime;
            //double mbPerSec = ((((double)bytes / (double)diffTime))*1000_000d)/1048576;
            //_lastTime = time;

            //Console.WriteLine($"Rate added: {mbPerSec} {bytes} {diffTime}");

            // Average
            //return mBPerSecAverage.Add(mbPerSec);

            data.Add(bytes);
            times.Add(time);
        }

        public float Average { get {
                int sum = 0;
                foreach (int d in data)
                    sum += d;

                long diff = times.Newest - times.Oldest;
                return ((sum * 1000_000f) / diff)/ 1048576;
            } }
    }
}
