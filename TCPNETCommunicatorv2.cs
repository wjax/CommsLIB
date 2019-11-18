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
    public class TCPNETCommunicatorv2<T> : CommunicatorBase<T>
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

        private CircularByteBuffer4Comms messageCircularBuffer;

        private Task senderTask;
        private Task receiverTask;
        private volatile bool exit = false;

        private bool tcpClientProvided = false;

        private CommEquipmentObject<TcpClient> tcpEq;
        private FrameWrapperBase<T> frameWrapper;

        private byte[] rxBuffer = new byte[65536];
        private byte[] txBuffer = new byte[65536];

        private Timer dataRateTimer;
        private int bytesAccumulator = 0;
        #endregion


        public TCPNETCommunicatorv2(FrameWrapperBase<T> _frameWrapper = null) : base()
        {
            frameWrapper = _frameWrapper != null ? _frameWrapper : null;
            tcpClientProvided = false;
        }

        public TCPNETCommunicatorv2(TcpClient client, FrameWrapperBase<T> _frameWrapper = null) : base()
        {
            frameWrapper = _frameWrapper != null ? _frameWrapper : null;
            // Do stuff
            tcpClientProvided = true;
            var IP = (client.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
            var Port = (client.Client.RemoteEndPoint as IPEndPoint).Port;

            CommsUri = new ConnUri($"tcp://{IP}:{Port}");

            tcpEq = new CommEquipmentObject<TcpClient>("", CommsUri, client, false);
        }

        #region CommunicatorBase

        public override void Init(ConnUri uri, bool persistent, string ID, int inactivityMS, int _sendGap = 0)
        {
            if ((uri == null || !uri.IsValid) && !tcpClientProvided)
                return;

            messageCircularBuffer = new CircularByteBuffer4Comms(65536);

            MINIMUM_SEND_GAP = _sendGap;
            RECEIVE_TIMEOUT = inactivityMS;
            frameWrapper?.SetID(ID);

            CommsUri = uri ?? CommsUri;
            SetIPChunks(CommsUri.IP);

            if (!tcpClientProvided)
            {
                tcpEq = new CommEquipmentObject<TcpClient>(ID, uri, null, persistent);
                tcpEq.ID = ID;
                receiverTask = new Task(Connect2EquipmentCallback, TaskCreationOptions.LongRunning);
            }
            else
            {
                tcpEq.ID = ID;
                receiverTask = new Task(ReceiveCallback, TaskCreationOptions.LongRunning);
            }

            senderTask = new Task(DoSendStart, TaskCreationOptions.LongRunning);
        }

        public override void SendASync(byte[] serializedObject, int length)
        {
            messageCircularBuffer?.put(serializedObject, length);
            //messageQueu.Enqueue(serializedObject);
        }

        public override void SendSync(byte[] bytes, int offset, int length)
        {
            Send2Equipment(bytes, offset, length, tcpEq);
        }

        public override void Start()
        {
            logger.Info("Start");
            exit = false;

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
            
            messageCircularBuffer.reset();
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

        public override string ID { get => tcpEq?.ID; }
        #endregion

        private void ClientDown()
        {
            if (tcpEq == null)
                return;

            logger.Info("ClientDown - " + tcpEq.ID);

            bytesAccumulator = 0;

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

            bytesAccumulator = 0;

            // Launch Event
            FireConnectionEvent(tcpEq.ID, tcpEq.ConnUri, true);
        }

        
        private void DoSendStart()
        {
            long toWait = 0;
            int length = 0;

            while (!exit)
            {

                try
                {
                    length = messageCircularBuffer.take(txBuffer, 0);

                    if ((toWait = TimeTools.GetCoarseMillisNow() - LastTX) < MINIMUM_SEND_GAP)
                        Thread.Sleep((int)toWait);

                    Send2Equipment(txBuffer, 0, length, tcpEq);
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Exception in messageQueue");
                }
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void Send2Equipment(byte[] data, int offset, int length, CommEquipmentObject<TcpClient> o)
        {
            if (o == null || o.ClientImpl == null)
                return;

            string ID = o.ID;
            TcpClient t = o.ClientImpl;
            try
            {
                t?.Client?.Send(data, offset, length, SocketFlags.None);
                LastTX = TimeTools.GetCoarseMillisNow();
            }
            catch (Exception e)
            {
                logger.Error(e, "Error while sending TCPNet");
                // Client Down
                ClientDown();
            }
        }

        private void Connect2EquipmentCallback()
        {
            while (!exit)
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
                                bytesAccumulator += rx;
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
                                                    ipChunks);

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

            }
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
                        bytesAccumulator += rx;
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
                                        ipChunks);

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
            float dataRateMpbs = (bytesAccumulator * 8f) / 1048576; // Mpbs
            FireDataRateEvent(dataRateMpbs);
        }
        

        protected override async void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    await Stop();

                    messageCircularBuffer.Dispose();
                    tcpEq.ClientImpl?.Dispose();
                    dataRateTimer?.Dispose();
                }

                messageCircularBuffer = null;
                tcpEq.ClientImpl = null;
                dataRateTimer = null;

                disposedValue = true;
            }

            base.Dispose(disposing);
        }

        public FrameWrapperBase<T> GetFrameWrapper()
        {
            return frameWrapper;
        }
    }

}
