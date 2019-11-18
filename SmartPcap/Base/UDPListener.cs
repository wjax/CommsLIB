using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CommsLIB.SmartPcap.Base
{
    public class UDPListener: IDisposable
    {
        private Socket socket;
        private byte[] buffer;

        private string networkIp = "225.25.1.10";
        private int networkPort = 1234;
        private bool isMulticast = false;
        private string netcard = "";
        private ushort[] ipChunks = new ushort[4];
        string ID = "";

        public delegate void DataReadyDelegate(string ip, int port, long time, byte[] buff, int rawDataOffset, int rawDataSize, string ID, ushort[] ipChunks);
        public event DataReadyDelegate DataReadyEvent;

        private Task runningJob;
        private CancellationTokenSource cancelSource;
        private CancellationToken cancelToken;

        public UDPListener(string _ID, string _ip, int _port, string _netcard = "")
        {
            ID = _ID;
            networkIp = _ip;
            networkPort = _port;
            isMulticast = HelperTools.IsMulticast(_ip, out IPAddress ipAdrr);
            netcard = _netcard;

            string[] chunks = _ip.Split('.');
            if (chunks.Length == 4)
                for (int i = 0; i < 4; i++)
                    ipChunks[i] = ushort.Parse(chunks[i]);
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

            socket.ReceiveBufferSize = 65535;
            socket.SendBufferSize = 65535; // default is 8192. Make it as large as possible for large RTP packets which are not fragmented

            if (string.IsNullOrEmpty(netcard))
                socket.Bind(new IPEndPoint(IPAddress.Any, networkPort));
            else
                socket.Bind(new IPEndPoint(IPAddress.Parse(netcard), networkPort));

            if (isMulticast)
                JoinMulticastOnSteroids(socket, networkIp);

            cancelSource = new CancellationTokenSource();
            cancelToken = cancelSource.Token;
            runningJob = new Task(() => RunReceiverProcessCallback(socket, cancelToken), cancelToken, TaskCreationOptions.LongRunning);
            runningJob.Start();

        }

        public Task Stop()
        {
            if (cancelToken.CanBeCanceled)
            {
                cancelSource.Cancel();
                socket.Close();
                socket = null;
                return runningJob;
            }

            return null;
        }

        private void RunReceiverProcessCallback(object state, CancellationToken token)
        {
            buffer = HelperTools.RentBuffer(HelperTools.SIZE_BYTES);
            Socket socket = (Socket)state;
            //EndPoint e = new IPEndPoint(IPAddress.Any, networkPort);
            long time;
            int n_bytes;
            while (!cancelToken.IsCancellationRequested)
            {
                if (socket != null)
                {
                    try
                    {
                        n_bytes = socket.Receive(buffer, 0, buffer.Length, SocketFlags.None);
                        time = HelperTools.GetLocalMicrosTime();
                        if (n_bytes > 0)
                        {
                            // Fire Event
                            DataReadyEvent?.Invoke(networkIp, networkPort, time, buffer, 0, n_bytes, ID, ipChunks);
                        }
                    }
                    catch (Exception ee) { }
                }
            }
            HelperTools.ReturnBuffer(buffer);
        }

        private void JoinMulticastOnSteroids(Socket s, string multicastIP)
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nics)
            {
                IPInterfaceProperties ip_properties = adapter.GetIPProperties();

                foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
                {
                    try
                    {
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(System.Net.IPAddress.Parse(multicastIP), ip.Address));
                    }
                    catch (Exception) { }
                }
                //}
            }
        }

        public void Dispose()
        {
            cancelSource.Dispose();
        }
    }
}
