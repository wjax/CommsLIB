using CommsLIB.Base;
using CommsLIB.Communications.FrameWrappers;
using CommsLIB.Helper;
using CommsLIB.SmartPcap;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CommsLIB.Communications
{
    public class TCPNETCommunicator<T> : CommunicatorBase<T>
    {
        #region logger
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        #endregion

        #region global defines
        private int RECEIVE_TIMEOUT = 4000;
        private const int CONNECTION_TIMEOUT = 5000;
        private const int SEND_TIMEOUT = 100; // Needed on linux as socket will not throw exception when send buffer full, instead blocks "forever"
        private int MINIMUM_SEND_GAP = 0;
        #endregion

        #region fields
        private bool disposedValue = false;
        private long LastTX = 0;

        private ICommsQueue messageQueu;
        private bool useCircular;

        private Task senderTask;
        private Task receiverTask;

        private volatile bool exit = false;
        private bool tcpClientProvided = false;

        private CommEquipmentObject<TcpClient> tcpEq;
        private FrameWrapperBase<T> frameWrapper;

        private byte[] rxBuffer = new byte[65536];
        private byte[] txBuffer = new byte[65536];

        private Timer dataRateTimer;
        private int bytesAccumulatorRX = 0;
        private int bytesAccumulatorTX = 0;
        #endregion

        public TCPNETCommunicator(FrameWrapperBase<T> _frameWrapper = null, bool circular = false) : base()
        {
            frameWrapper = _frameWrapper != null ? _frameWrapper : null;
            tcpClientProvided = false;
            useCircular = circular;
        }

        public TCPNETCommunicator(TcpClient client, FrameWrapperBase<T> _frameWrapper = null, bool circular = false) : base()
        {
            frameWrapper = _frameWrapper != null ? _frameWrapper : null;
            // Do stuff
            tcpClientProvided = true;
            var IP = (client.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
            var Port = (client.Client.RemoteEndPoint as IPEndPoint).Port;

            CommsUri = new ConnUri($"tcp://{IP}:{Port}");
            tcpEq = new CommEquipmentObject<TcpClient>("", CommsUri, client, false);

            useCircular = circular;
        }

        #region CommunicatorBase

        public override void Init(ConnUri uri, bool persistent, string ID, int inactivityMS, int _sendGap = 0)
        {
            if ((uri == null || !uri.IsValid) && !tcpClientProvided)
                return;

            this.ID = ID;
			messageQueu = useCircular ? (ICommsQueue)new CircularByteBuffer4Comms(65536) : (ICommsQueue)new BlockingByteQueue();
            MINIMUM_SEND_GAP = _sendGap;
            RECEIVE_TIMEOUT = inactivityMS;
            frameWrapper?.SetID(ID);
            State = STATE.STOP;

            CommsUri = uri ?? CommsUri;
            SetIPChunks(CommsUri.IP);

            if (!tcpClientProvided)
            {
                tcpEq = new CommEquipmentObject<TcpClient>(ID, uri, null, persistent);
                tcpEq.ID = ID;
            }
            else
            {
                tcpEq.ID = ID;
            }
        }

        public override void SendASync(byte[] serializedObject, int length)
        {
            messageQueu.Put(serializedObject, length);
        }

        public override bool SendSync(byte[] bytes, int offset, int length)
        {
            return Send2Equipment(bytes, offset, length, tcpEq);
        }

        public override void Start()
        {
            if (State == STATE.RUNNING)
                return;

            logger.Info("Start");
            exit = false;

            receiverTask = tcpClientProvided ? new Task(ReceiveCallback, TaskCreationOptions.LongRunning) : new Task(Connect2EquipmentCallback, TaskCreationOptions.LongRunning);
            senderTask = new Task(DoSendStart, TaskCreationOptions.LongRunning);

            senderTask.Start();
            receiverTask.Start();

            dataRateTimer = new Timer(OnDataRate, null, 1000, 1000);

            State = STATE.RUNNING;
        }

        public override async Task Stop()
        {
            logger.Info("Stop");
            exit = true;

            dataRateTimer.Dispose();

            messageQueu.Reset();
            tcpEq.ClientImpl?.Close();

            await senderTask;
            await receiverTask;

            State = STATE.STOP;
        }

        public override void SendSync(T Message)
        {
            byte[] buff = frameWrapper.Data2BytesSync(Message, out int count);
            if (count > 0)
                SendSync(buff, 0, count);
        }

        public override FrameWrapperBase<T> FrameWrapper { get => frameWrapper; }
        #endregion

        private void ClientDown()
        {
            if (tcpEq == null)
                return;

            logger.Info("ClientDown - " + tcpEq.ID);

            bytesAccumulatorRX = 0;
            bytesAccumulatorTX = 0;

            try
            {
                tcpEq.ClientImpl?.Close();
                tcpEq.ClientImpl?.Dispose();
            }
            catch (Exception e)
            {
                logger.Error(e, "ClientDown Exception");
            }
            finally
            {
                tcpEq.ClientImpl = null;
            }

            // Launch Event
            FireConnectionEvent(tcpEq.ID, tcpEq.ConnUri, false);
        }

        private void ClientUp(TcpClient o)
        {
            tcpEq.ClientImpl = o;

            bytesAccumulatorRX = 0;
            bytesAccumulatorTX = 0;

            // Launch Event
            FireConnectionEvent(tcpEq.ID, tcpEq.ConnUri, true);
        }

       
        private void DoSendStart()
        {
            long toWait = 0;
            LastTX = TimeTools.GetCoarseMillisNow();

            while (!exit)
            {
                try
                {
                    int read = messageQueu.Take(ref txBuffer, 0);

                    long now = TimeTools.GetCoarseMillisNow();
                    if (now - LastTX < MINIMUM_SEND_GAP)
                    {
                        toWait = MINIMUM_SEND_GAP - (now - LastTX);
                        Thread.Sleep((int)toWait);
                    }

                    Send2Equipment(txBuffer, 0, read, tcpEq);

                    LastTX = TimeTools.GetCoarseMillisNow();
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Exception in messageQueue");
                }
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private bool Send2Equipment(byte[] data, int offset, int length, CommEquipmentObject<TcpClient> o)
        {
            if (o == null || o.ClientImpl == null)
                return false;

            string ID = o.ID;
            TcpClient t = o.ClientImpl;

            try
            {
                int nBytes = t.Client.Send(data, offset, length, SocketFlags.None);

                bytesAccumulatorTX += nBytes;
                LastTX = TimeTools.GetCoarseMillisNow();
            }
            catch (Exception e)
            {
                logger.Error(e, "Error while sending TCPNet");
                // Client Down
                ClientDown();

                return false;
            }

            return true;
        }

        private void Connect2EquipmentCallback()
        {
            do
            {
                logger.Info("Waiting for new connection");
                IPEndPoint ipep = new IPEndPoint(IPAddress.Parse(tcpEq.ConnUri.IP), tcpEq.ConnUri.Port);

                // Blocks here for timeout
                using (TcpClient t = TimeOutSocketFactory.Connect(ipep, CONNECTION_TIMEOUT))
                {
                    if (t != null)
                    {
                        t.SendTimeout = SEND_TIMEOUT;
                        t.ReceiveTimeout = RECEIVE_TIMEOUT;

                        // Launch event and Add to Dictionary of valid connections
                        ClientUp(t);
                        int rx;

                        try
                        {
                            while ((rx = tcpEq.ClientImpl.Client.Receive(rxBuffer)) > 0)
                            {
                                // Update Accumulator
                                bytesAccumulatorRX += rx;
                                // Update RX Time
                                tcpEq.timeLastIncoming = TimeTools.GetCoarseMillisNow();

                                // RAW Data Event
                                FireDataEvent(CommsUri.IP,
                                                CommsUri.Port,
                                                HelperTools.GetLocalMicrosTime(),
                                                rxBuffer,
                                                0,
                                                rx,
                                                tcpEq.ID,
                                                IpChunks);

                                // Feed to FrameWrapper
                                frameWrapper?.AddBytes(rxBuffer, rx);
                            }
                        }
                        catch (Exception e)
                        {
                            logger.Error(e, "Error while receiving TCPNet");
                        }
                        finally
                        {
                            ClientDown();
                        }
                    }
                }

            } while (!exit && tcpEq.IsPersistent);
        }

        private void ReceiveCallback()
        {
            if (tcpEq.ClientImpl != null)
            {
                logger.Info("Receiving");

                tcpEq.ClientImpl.SendTimeout = SEND_TIMEOUT;
                tcpEq.ClientImpl.ReceiveTimeout = RECEIVE_TIMEOUT;

                // Launch event and Add to Dictionary of valid connections
                ClientUp(tcpEq.ClientImpl);
                int rx;

                try
                {
                    while ((rx = tcpEq.ClientImpl.Client.Receive(rxBuffer)) > 0 && !exit)
                    {
                        // Update Accumulator
                        bytesAccumulatorRX += rx;
                        // Update RX Time
                        tcpEq.timeLastIncoming = TimeTools.GetCoarseMillisNow();

                        // RAW Data Event
                        FireDataEvent(CommsUri.IP,
                                        CommsUri.Port,
                                        HelperTools.GetLocalMicrosTime(),
                                        rxBuffer,
                                        0,
                                        rx,
                                        tcpEq.ID,
                                        IpChunks);

                        // Feed to FrameWrapper
                        frameWrapper?.AddBytes(rxBuffer, rx);
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e, "Error while receiving TCPNet");
                }
                finally
                {
                    ClientDown();
                }
            }
        }

        private void OnDataRate(object state)
        {
            float dataRateMpbsRX = (bytesAccumulatorRX * 8f) / 1048576; // Mpbs
            float dataRateMpbsTX = (bytesAccumulatorTX * 8f) / 1048576; // Mpbs

            bytesAccumulatorRX = 0;
            bytesAccumulatorTX = 0;

            FireDataRateEvent(ID, dataRateMpbsRX, dataRateMpbsTX);
        }


        protected override async void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    await Stop();

                    (messageQueu as IDisposable).Dispose();
                    tcpEq.ClientImpl?.Dispose();
                    dataRateTimer?.Dispose();
                }

                messageQueu = null;
                tcpEq.ClientImpl = null;
                dataRateTimer = null;

                disposedValue = true;
            }

            base.Dispose(disposing);
        }
    }

}
