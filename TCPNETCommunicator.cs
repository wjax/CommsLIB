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
    public class TCPNETCommunicator<T> : CommunicatorBase
    {
        #region logger
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        #endregion

        private bool disposedValue = false;

        private int INACTIVITY_TIMER = 4000;

        private const int CONNECTION_TIMEOUT = 5000;
        private const int SEND_TIMEOUT = 2000;

        private int MINIMUM_SEND_GAP = 0;
        private long LastTX = 0;

        private BlockingQueue<byte[]> messageQueu;

        private System.Timers.Timer DetectInactivityTimer;

        private Task senderTask;
        private Task receiverTask;
        private volatile bool exit = false;

        private volatile CommEquipmentObject<TcpClient> tcpEq;
        private FrameWrapperBase<T> frameWrapper;

        private byte[] rxBuffer = new byte[65536];

        public TCPNETCommunicator(FrameWrapperBase<T> _frameWrapper = null) : base()
        {
            
            frameWrapper = _frameWrapper != null ? _frameWrapper : null;
        }

        public override void init(ConnUri uri, bool persistent, string ID, int inactivityMS,  int _sendGap = 0)
        {
            if (uri == null || !uri.IsValid)
                return;

            MINIMUM_SEND_GAP = _sendGap; 
            frameWrapper?.SetID(ID);

            CommsUri = uri;
            SetIPChunks(uri.IP);

            tcpEq = new CommEquipmentObject<TcpClient>(ID, uri, null, persistent);
            senderTask = new Task(doSendStart, TaskCreationOptions.LongRunning);
            receiverTask = new Task(Connect2EquipmentCallback, TaskCreationOptions.LongRunning);

            INACTIVITY_TIMER = inactivityMS;
            if (INACTIVITY_TIMER > 0)
            {
                // Task Detect Inactive Clients
                DetectInactivityTimer = new System.Timers.Timer(INACTIVITY_TIMER);
                DetectInactivityTimer.AutoReset = true;
                DetectInactivityTimer.Elapsed += OnInactivityTimer;
                DetectInactivityTimer.Enabled = true;
            }
        }

        private void OnInactivityTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Check last time comms was done
            long now = TimeTools.GetCoarseMillisNow();

            if (Math.Abs(now - tcpEq.timeLastIncoming) > INACTIVITY_TIMER)
                ClientDown();
        }

        private void ClientDown()
        {
            if (tcpEq == null)
                return;

            
            logger.Info("ClientDown - " + tcpEq.ID);

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

            // Launch Event
            FireConnectionEvent(tcpEq.ID, tcpEq.ConnUri, true);
        }

        public override void sendASync(byte[] serializedObject, int length)
        {
            messageQueu?.Enqueue(serializedObject);
        }

        public override void sendSync(byte[] bytes, int offset, int length)
        {
            Send2Equipment(bytes, offset, length, tcpEq);
        }

        private void doSendStart()
        {
            long toWait = 0;
            while (!exit)
            {
                try
                {
                    byte[] data = messageQueu.Dequeue();

                    if ((toWait = TimeTools.GetCoarseMillisNow() - LastTX) < MINIMUM_SEND_GAP)
                        Thread.Sleep((int)toWait);

                    Send2Equipment(data, 0, data.Length, tcpEq);
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Exception in messageQueue");
                }
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Send2Equipment(byte[] data, int offset, int length,  CommEquipmentObject<TcpClient> o)
        {
            if (o == null || o.ClientImpl == null)
                return;

            string ID = o.ID;
            TcpClient t = o.ClientImpl;
            logger.Trace("Just before sending ");
            try
            {
                t?.Client?.Send(data, offset, length, SocketFlags.None);
                logger.Trace("Just after sending");
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
                if (tcpEq.ClientImpl == null)
                {
                    logger.Info("Waiting for new connection");
                    IPEndPoint ipep = new IPEndPoint(IPAddress.Parse(tcpEq.ConnUri.IP), tcpEq.ConnUri.Port);

                    // Blocks here for timeout
                    using (TcpClient t = TimeOutSocketFactory.Connect(ipep, CONNECTION_TIMEOUT))
                    {
                        if (t != null)
                        {
                            t.SendTimeout = SEND_TIMEOUT;
                            // Launch event and Add to Dictionary of valid connections
                            ClientUp(t);

                            int rx;

                            try
                            {
                                while ((rx = tcpEq.ClientImpl.Client.Receive(rxBuffer)) > 0)
                                {
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
        }

        public override void start()
        {
            logger.Info("Start");
            exit = false;
            messageQueu = new BlockingQueue<byte[]>();

            senderTask.Start();
            receiverTask.Start();

            State = STATE.RUNNING;
        }

        public override async Task stop()
        {
            logger.Info("Stop");
            exit = true;

            messageQueu.UnBlock();
            tcpEq.ClientImpl.Close();

            await Task.WhenAll(senderTask, receiverTask);
            //Task.WaitAll(senderTask, receiverTask);

            if (DetectInactivityTimer != null)
            {
                DetectInactivityTimer.Elapsed -= OnInactivityTimer;
                DetectInactivityTimer.Dispose();
                DetectInactivityTimer = null;
            }

            State = STATE.STOP;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    messageQueu.Dispose();
                    tcpEq.ClientImpl?.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.
                messageQueu = null;
                tcpEq.ClientImpl = null;

                disposedValue = true;
            }

            base.Dispose(disposing);
        }
    }

}
