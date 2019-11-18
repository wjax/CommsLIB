using System;
using System.Collections.Generic;
using System.Text;

namespace CommsLIB.Helper
{
    public class MovingAverage
    {
        int _size;
        double[] _values = null;
        int _valuesIndex = 0;
        int _valueCount = 0;
        double _sum = 0;

        public double Average { get; private set; }

        public MovingAverage(int size)
        {
            _size = Math.Max(size, 1);
            _values = new double[_size];
            
        }

        public double Add(double newValue)
        {
            // calculate new value to add to sum by subtracting the
            // value that is replaced from the new value;
            double oldValue = _values[_valuesIndex];

            _sum = _sum + newValue - oldValue; 

            _values[_valuesIndex++] = newValue;
            _valuesIndex %= _size;

            if (_valueCount < _size)
                _valueCount++;

            return (Average = _sum / _valueCount);
        }
    }
}
