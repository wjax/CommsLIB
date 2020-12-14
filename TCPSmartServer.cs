using CommsLIB.Communications;
using CommsLIB.Communications.FrameWrappers;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CommsLIB.Communications
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
        private string ListeningIP;

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

        public TCPSmartServer(int _port, string _ip = null, bool _useCircularBuffers=false)
        {
            ListeningPort = _port;
            ListeningIP = _ip;
            UseCircularBuffers = _useCircularBuffers;
        }

        public void Start()
        {
            Stop();

            server = new TcpListener(string.IsNullOrEmpty(ListeningIP) ? IPAddress.Any : IPAddress.Parse(ListeningIP), ListeningPort);
            server.Start();

            cancelListenSource = new CancellationTokenSource();
            cancelListenToken = cancelListenSource.Token;
            listenTask = new Task(() => DoListenForClients(server, cancelListenToken), cancelListenToken, TaskCreationOptions.LongRunning);
            listenTask.Start();
        }

        public void Stop()
        {
            if (cancelListenToken.CanBeCanceled)
            {
                cancelListenSource.Cancel();
                server.Stop();
                server = null;
                listenTask.Wait();
            }
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
                CommunicatorBase<U> communicator = new TCPNETCommunicator<U>(tcpClient, framewrapper, UseCircularBuffers);

                // Add to dict 
                lock (lockerClientList)
                {
                    ClientList.Add(id, communicator);
                }

                // Subscribe to events
                communicator.ConnectionStateEvent += OnCommunicatorConnection;
                communicator.DataReadyEvent += OnCommunicatorData;
                framewrapper.FrameAvailableEvent += OnFrameReady;

                communicator.Init(null, false, id, 0);
                framewrapper.Start();
                communicator.Start();
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
                    if (ClientList.TryGetValue(ID, out CommunicatorBase<U> communicator))
                    {
                        communicator.Dispose();
                        ClientList.Remove(ID);
                    }
                }    
        }

        public void Send2All(U data)
        {
            lock (lockerClientList)
            {
                foreach (KeyValuePair<string, CommunicatorBase<U>> kv in ClientList)
                    kv.Value.SendSync(data);
            }
        }

        /// <summary>
        /// Use only with Circular Buffer
        /// </summary>
        /// <param name="data"></param>
        public void Send2AllAsync(U data)
        {
            if (!UseCircularBuffers)
                throw new Exception("Cant use Send2AllAsync in this mode. Please use Circular Buffer");

            lock (lockerClientList)
            {
                foreach (KeyValuePair<string, CommunicatorBase<U>> kv in ClientList)
                    kv.Value.SendASync(data);
            }
        }

        public void Send2All(byte[] buff, int size)
        {
            lock (lockerClientList)
            {
                foreach (KeyValuePair<string, CommunicatorBase<U>> kv in ClientList)
                    kv.Value.SendASync(buff, size);
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        // TODO
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~TCPSmartServer()
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
