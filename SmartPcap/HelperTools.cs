using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace CommsLIB.SmartPcap
{
    internal class HelperTools
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        internal const int SIZE_BYTES = 64 * 1024;
        private static readonly double microsPerCycle = 1000000.0 / Stopwatch.Frequency;
        // Time, IP, Port, SizePayload
        public const int headerSize = 8 + 4 + 8 + 4;
        public const int idxIndexSize = 4 + 8;
        private static ArrayPool<byte> bytePool = ArrayPool<byte>.Shared;

        internal static int Long2Bytes(byte[] _buff, int _offset, long _value)
        {
            for (int i = 0; i < 8; i++)
                _buff[i + _offset] = (byte)(_value >> 8 * i);

            return 8;
        }

        internal static int Int32Bytes(byte[] _buff, int _offset, int _value)
        {
            for (int i = 0; i < 4; i++)
                _buff[i + _offset] = (byte)(_value >> 8 * i);

            return 4;
        }

        internal static int StringWithLength2Bytes(byte[] _buff, int _offset, string _value)
        {
            if (_buff.Length - _offset < 4 + _value.Length)
                return -1;

            // ASCII so byte count should be equal to chars
            int i = _offset;
            i += Int32Bytes(_buff, _offset, _value.Length);

            // Actual string
            i += Encoding.ASCII.GetBytes(_value, 0, _value.Length, _buff, i);

            return i - _offset;
        }

        internal static string Bytes2StringWithLength(Stream stream)
        {
            string s = "";
            byte[] buff = HelperTools.RentBuffer(256);

            if (stream.Read(buff, 0, 4) == 4)
            {
                int length = BitConverter.ToInt32(buff, 0);
                if (stream.Read(buff, 0, length) == length)
                {
                    s = Encoding.ASCII.GetString(buff, 0, length);
                }
            }

            return s;

            //int stringLength = BitConverter.ToInt32(_buff, _offset);
            //if (stringLength > 0 && stringLength < _buff.Length - _offset)
            //{
            //    s = Encoding.ASCII.GetString(_buff, _offset + 4, stringLength);
            //}

            //return s;
        }

        internal static int GetDeterministicHashCode(string str)
        {
            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        internal static long millisFromEpochNow()
        {
            return (long)(DateTime.Now - UnixEpoch).TotalMilliseconds;
        }

        internal static DateTime fromMillis(long millis)
        {
            TimeSpan time = TimeSpan.FromMilliseconds(millis);
            DateTime result = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            result = result.Add(time);

            return result;
        }

        internal static long GetLocalMicrosTime(long offset = 0)
        {
            return ((long)(Stopwatch.GetTimestamp() * microsPerCycle) - offset);
        }

        internal static byte[] RentBuffer(int _minimumSize)
        {
            return bytePool.Rent(_minimumSize);
        }

        internal static void ReturnBuffer(byte[] _buff)
        {
            bytePool.Return(_buff);
        }

        internal static ulong IPPort2Long(string _ip, int _port)
        {
            byte[] _buffer = new byte[8];

            ushort[] ipChunks = new ushort[4];
            string[] chunks = _ip.Split('.');
            if (chunks.Length == 4)
                for (int i = 0; i < 4; i++)
                    ipChunks[i] = ushort.Parse(chunks[i]);

            // IP 4
            for (int i = 0; i < 4; i++)
                _buffer[i] = (byte)ipChunks[i];

            // Port 4
            HelperTools.Int32Bytes(_buffer, 4, _port);

            return BitConverter.ToUInt64(_buffer, 0);
        }

        public static bool IsMulticast(string ip, out IPAddress adr)
        {
            bool bResult = false;
            if (IPAddress.TryParse(ip, out adr))
            {
                byte first = adr.GetAddressBytes()[0];
                if ((first & 0xF0) == 0xE0)
                    bResult = true;
            }

            return bResult;
        }
    }
}
