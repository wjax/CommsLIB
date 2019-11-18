using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace CommsLIB.Base
{
    public class CircularBuffer<T> : IEnumerable<T>
    {
        int _size;
        T[] _values = null;
        int _valuesIndex = 0;
        int _valueCount = 0;

        public CircularBuffer(int size)
        {
            _size = Math.Max(size, 1);
            _values = new T[_size];
        }

        public void Add(T newValue)
        {
            _values[_valuesIndex] = newValue;
            _valuesIndex++;
            _valuesIndex %= _size;

            if (_valueCount < _size)
                _valueCount++;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)_values).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<T>)_values).GetEnumerator();
        }

        public T Newest { get => _values[ (_valuesIndex - 1) < 0 ? _size-1 : (_valuesIndex - 1)]; }

        public T Oldest { get => _values[_valuesIndex]; }
    }
}
