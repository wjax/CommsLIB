namespace CommsLIB.Helper
{
    public class ByteTools
    {
        internal static void Int32Bytes(byte[] _buff, int _offset, int _value)
        {
            for (int i = 0; i < 4; i++)
                _buff[i + _offset] = (byte)(_value >> 8 * i);
        }
    }
}
