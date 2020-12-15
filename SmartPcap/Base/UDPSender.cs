using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CommsLIB.SmartPcap.Base
{
    public class UDPSender : IDisposable
    {
        private Socket socket;

        public delegate void DataRateDelegate(string ID, float MbpsTX);
        public event DataRateDelegate DataRateEvent;

        private const int SIO_UDP_CONNRESET = -1744830452;
        private byte[] byteTrue = { 0x00, 0x00, 0x00, 0x01 };

        private string networkIp = "225.25.1.10";
        private int networkPort = 1234;
        private bool isMulticast = false;
        private string netcard = "";
        int ttl = 0;
        int portOffset;
        string ID;

        private EndPoint peer;

        private Timer dataRateTimer;
        private int bytesAccumulatorTX = 0;

        public UDPSender(string _id, string _ip, int _port, bool onlyLocal, string _netcard = "")
        {
            ID = _id;
            networkIp = _ip;
            networkPort = _port;
            isMulticast = HelperTools.IsMulticast(_ip, out IPAddress ipAdrr);
            netcard = _netcard;

            if (onlyLocal)
                ttl = 0;
            else
                ttl = 255;

            dataRateTimer = new Timer(OnDataRate, null, 1000, 1000);
        }

        //public UDPSender(ulong _ipport, bool onlyLocal, string _netcard = "")
        //{
        //    int[] ip =
        //    {
        //        (byte)(_ipport),(byte)(_ipport >> 8), (byte)(_ipport >> 16), (byte)(_ipport >> 24)
        //    };

        //    if (onlyLocal)
        //        ttl = 0;
        //    else
        //        ttl = 25;

        //    netcard = _netcard;
        //    networkIp = String.Format("{0}.{1}.{2}.{3}", ip[0], ip[1], ip[2], ip[3]);
        //    networkPort = (byte)(_ipport >> 32) | ((byte)(_ipport >> 40) << 8) | ((byte)(_ipport >> 48) << 16) | ((byte)(_ipport >> 56) << 24);

        //    isMulticast = HelperTools.IsMulticast(networkIp, out IPAddress ipAdrr);
        //}

        private void OnDataRate(object state)
        {
            float dataRateMpbsTX = (bytesAccumulatorTX * 8f) / 1048576; // Mpbs
            bytesAccumulatorTX = 0;

            DataRateEvent?.Invoke(ID, dataRateMpbsTX);
        }

        public void Start()
        {
            if (socket != null)
            {
                socket.Close();
                socket = null;
            }

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket,
                                SocketOptionName.ReuseAddress, true);

            socket.ReceiveBufferSize = 100 * 1024;
            socket.SendBufferSize = 65535; // default is 8192. Make it as large as possible for large RTP packets which are not fragmented

            if (string.IsNullOrEmpty(netcard))
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            else
                socket.Bind(new IPEndPoint(IPAddress.Parse(netcard), 0));

            if (isMulticast)
                socket.Ttl = (short)ttl;
            //socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.MulticastTimeToLive, ttl);

            socket.IOControl(SIO_UDP_CONNRESET, byteTrue, null);

            peer = new IPEndPoint(IPAddress.Parse(networkIp), networkPort);
        }

        public bool Send(byte[] buffer, int offset, int count)
        {
            try
            {
                int nBytes = socket.SendTo(buffer, offset, count, SocketFlags.None, peer);
                bytesAccumulatorTX += nBytes;

                return true;

            } catch(SocketException)
            {
                return false;
            }
        }

        public void Close()
        {
            socket?.Close();
            socket?.Dispose();
            socket = null;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Close();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~UDPSender()
        // {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

    }
}
