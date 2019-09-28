using CommsLIB.Base;
using CommsLIB.Communications.FrameWrappers;
using System;
using System.Threading.Tasks;

namespace CommsLIB.Communications
{
    public static class CommunicatorFactory
    {
        public static CommunicatorBase<T> CreateCommunicator<T>(ConnUri uri, FrameWrapperBase<T> frameWrapper, bool circular = false)
        {
            CommunicatorBase<T> c = null ;
            switch (uri.UriType)
            {
                case ConnUri.TYPE.TCP:
                    if (circular)
                        c = new TCPNETCommunicatorv2<T>(frameWrapper);
                    else
                        c = new TCPNETCommunicator<T>(frameWrapper);
                    break;
                case ConnUri.TYPE.UDP:
                    if (circular)
                        c = new UDPNETCommunicatorv2<T>(frameWrapper);
                    else
                        c = new UDPNETCommunicator<T>(frameWrapper);
                    break;
                case ConnUri.TYPE.SERIAL:
                    c = new SERIALCommunicator<T>(frameWrapper);
                    break;

            }

            return c;
        }
    }

    public abstract class CommunicatorBase<T> : IDisposable
    {
        public event DataReadyEventHandler DataReadyEvent;
        public delegate void DataReadyEventHandler(string ip, int port, long time, byte[] bytes, int offset, int length , string ID, ushort[] ipChunks);

        public delegate void ConnectionStateDelegate(string ID, ConnUri uri, bool connected);
        public event ConnectionStateDelegate ConnectionStateEvent;

        //private FrameWrapperBase<T> frameWrapper;

        public enum STATE
        {
            RUNNING,
            STOP
        }
        public STATE State;

        public ConnUri CommsUri;
        public ushort[] ipChunks = new ushort[4];

        //public virtual FrameWrapperBase<T> GetFrameWrapper()
        //{
        //    return frameWrapper;
        //}

        public abstract void init(ConnUri uri, bool persistent, string ID, int inactivityMS, int sendGAP = 0);
        public abstract void start();
        public abstract Task stop();
        public abstract void sendASync(byte[] bytes, int length);
        public abstract void sendSync(byte[] bytes, int offset, int length);
        public abstract void sendSync(T protoBufMessage);
        public virtual void FireDataEvent(string ip, int port, long time, byte[] bytes, int offset, int length, string ID, ushort[] ipChunks = null)
        {
            DataReadyEvent?.Invoke(ip, port, time, bytes, offset, length, ID, ipChunks);
        }

        public virtual void FireConnectionEvent(string ID, ConnUri uri, bool connected)
        {
            ConnectionStateEvent?.Invoke(ID, uri, connected);
        }

        protected virtual void SetIPChunks(string _ip)
        {
            string[] chunks = _ip.Split('.');
            if (chunks.Length == 4)
                for (int i = 0; i < 4; i++)
                    ipChunks[i] = ushort.Parse(chunks[i]);
        }

        #region IDisposable Support
        protected bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    UnsubscribeEventHandlers();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~CommunicatorBase() {
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

        public void UnsubscribeEventHandlers()
        {
            if (DataReadyEvent != null)
                foreach (var d in DataReadyEvent.GetInvocationList())
                    DataReadyEvent -= (d as DataReadyEventHandler);

            if (ConnectionStateEvent != null)
                foreach (var d in ConnectionStateEvent.GetInvocationList())
                    ConnectionStateEvent -= (d as ConnectionStateDelegate);
        }
    }
}
