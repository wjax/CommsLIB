using System;
using System.Net;
using System.Net.Sockets;

namespace CommsLIB.SmartPcap.Base
{
    public class UDPSender
    {
        private Socket socket;

        private string networkIp = "225.25.1.10";
        private int networkPort = 1234;
        private bool isMulticast = false;
        private string netcard = "";
        int ttl = 0;
        int portOffset;

        private EndPoint peer;

        public UDPSender(string _ip, int _port, string _netcard = "")
        {
            networkIp = _ip;
            networkPort = _port;
            isMulticast = HelperTools.IsMulticast(_ip, out IPAddress ipAdrr);
            netcard = _netcard;
        }

        public UDPSender(ulong _ipport, bool onlyLocal, string _netcard = "")
        {
            int[] ip =
            {
                (byte)(_ipport),(byte)(_ipport >> 8), (byte)(_ipport >> 16), (byte)(_ipport >> 24)
            };

            if (onlyLocal)
                ttl = 0;
            else
                ttl = 25;

            netcard = _netcard;
            networkIp = String.Format("{0}.{1}.{2}.{3}", ip[0], ip[1], ip[2], ip[3]);
            networkPort = (byte)(_ipport >> 32) | ((byte)(_ipport >> 40) << 8) | ((byte)(_ipport >> 48) << 16) | ((byte)(_ipport >> 56) << 24);

            isMulticast = HelperTools.IsMulticast(networkIp, out IPAddress ipAdrr);
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

            peer = new IPEndPoint(IPAddress.Parse(networkIp), networkPort);
        }

        public void Send(byte[] buffer, int offset, int count)
        {
            socket?.SendTo(buffer, offset, count, SocketFlags.None, peer);
        }

        public void Close()
        {
            socket?.Close();
            socket?.Dispose();
            socket = null;
        }

    }
}
