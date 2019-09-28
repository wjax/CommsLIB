using CommsLIB.Communications;
using CommsLIB.Communications.FrameWrappers;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ConfTestClient.Comms
{
    public class TCPSmartServer<T , U> : IDisposable where T : FrameWrapperBase<U>, new()
    {
        #region logger
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        #endregion

        #region consts
        private const int RCV_BUFFER_SIZE = 8192;
        #endregion

        #region members
        private TcpListener server = null;
        private int ListeningPort;

        private Dictionary<string, CommunicatorBase<U>> ClientList = new Dictionary<string, CommunicatorBase<U>>();
        private object lockerClientList = new object();

        public event DataReadyEventHandler DataReadyEvent;
        public delegate void DataReadyEventHandler(string ip, int port, long time, byte[] bytes, int offset, int length, string ID, ushort[] ipChunks);

        public delegate void FrameReadyDelegate(U message, string ID);
        public event FrameReadyDelegate FrameReadyEvent;

        public delegate void ConnectionStateDelegate(string SourceID,  bool connected);
        public event ConnectionStateDelegate ConnectionStateEvent;

        private Task listenTask;
        private CancellationTokenSource cancelListenSource;
        private CancellationToken cancelListenToken;

        private Task senderTask;
        private CancellationTokenSource cancelSenderSource;
        private CancellationToken cancelSenderToken;

        #endregion

        #region fields
        private bool UseCircularBuffers = false;
        #endregion

        public TCPSmartServer(int _port, bool _useCircularBuffers=false)
        {
            ListeningPort = _port;
            UseCircularBuffers = _useCircularBuffers;
        }

        public void start()
        {
            stop();

            server = new TcpListener(IPAddress.Any, ListeningPort);
            server.Start();

            cancelListenSource = new CancellationTokenSource();
            cancelListenToken = cancelListenSource.Token;
            listenTask = new Task(() => DoListenForClients(server, cancelListenToken), cancelListenToken, TaskCreationOptions.LongRunning);
            listenTask.Start();
        }

        public void stop()
        {
            if (cancelListenToken.CanBeCanceled)
            {
                cancelListenSource.Cancel();
                server.Stop();
                server = null;
                listenTask.Wait();
            }
        }

        public void Dispose()
        {
            
        }

        private static string GetIDFromSocket(Socket s)
        {
            return (s.RemoteEndPoint as IPEndPoint).Address.ToString() + ":" + (s.RemoteEndPoint as IPEndPoint).Port.ToString();
        }

        private void DoListenForClients(object state, CancellationToken token)
        {
            TcpListener _server = (state as TcpListener);

            while (!cancelListenToken.IsCancellationRequested)
            {
                logger.Info("Waiting for a connection... ");

                // Perform a blocking call to accept requests.
                TcpClient tcpClient = _server.AcceptTcpClient();
                // Get ID
                string id = GetIDFromSocket(tcpClient.Client);
                // Create Framewrapper
                var framewrapper = new T();
                // Create TCPNetCommunicator
                CommunicatorBase<U> communicator;
                if (UseCircularBuffers)
                    communicator = new TCPNETCommunicatorv2<U>(tcpClient, framewrapper);
                else
                    communicator = new TCPNETCommunicator<U>(tcpClient, framewrapper);

                // Add to dict 
                lock (lockerClientList)
                    ClientList.Add(id, communicator);

                // Subscribe to events
                communicator.ConnectionStateEvent += OnCommunicatorConnection;
                communicator.DataReadyEvent += OnCommunicatorData;
                framewrapper.FrameAvailableEvent += OnFrameReady;

                communicator.init(null, false, id, 0);
                framewrapper.Start();
                communicator.start();


            }
        }

        private void OnFrameReady(string ID, U payload)
        {
            // Raise
            FrameReadyEvent?.Invoke(payload, ID);
        }

        private void OnCommunicatorData(string ip, int port, long time, byte[] bytes, int offset, int length, string ID, ushort[] ipChunks)
        {
            // Not used normally
            DataReadyEvent?.Invoke(ip, port, time, bytes, offset, length, ID, ipChunks);
        }

        private void OnCommunicatorConnection(string ID, CommsLIB.Base.ConnUri uri, bool connected)
        {
            // Raise
            ConnectionStateEvent?.Invoke(ID, connected);

            // Remove if disconnected
            if (!connected)
                lock (lockerClientList)
                {
                    var communicator = ClientList[ID];
                    communicator.Dispose();
                    ClientList.Remove(ID);
                }
                    
        }

        public void Send2All(U data)
        {
            //string[] keys = null;

            //lock (lockerClientList)
            //{
            //    keys = new string[ClientList.Keys.Count];
            //    ClientList.Keys.CopyTo(keys, 0);
            //}

            //for (int i = keys.Length - 1; i >= 0; i--)
            //{
            //    //byte[] buffer = (ClientList[keys[i]] as IHasFrameWrapper<U>).GetFrameWrapper().Data2BytesSync(data, out int count);
            //    ClientList[keys[i]]?.sendSync(data);
            //}
            lock (lockerClientList)
            {
                foreach (KeyValuePair<string, CommunicatorBase<U>> kv in ClientList)
                    kv.Value.sendSync(data);
            }
        }

        public void Send2All(byte[] buff, int size)
        {
            //string[] keys = null;

            //lock (lockerClientList)
            //{
            //    keys = new string[ClientList.Keys.Count];
            //    ClientList.Keys.CopyTo(keys,0);
            //}

            //for (int i= keys.Length-1; i>=0;i--)
            //{
            //   ClientList[keys[i]].sendASync(buff, size);
            //}
            lock (lockerClientList)
            {
                foreach (KeyValuePair<string, CommunicatorBase<U>> kv in ClientList)
                    kv.Value.sendASync(buff, size);
            }
        }
    }
}
