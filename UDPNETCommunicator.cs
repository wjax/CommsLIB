using CommsLIB.Base;
using CommsLIB.Communications.FrameWrappers;
using CommsLIB.Helper;
using CommsLIB.SmartPcap;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CommsLIB.Communications
{
    public class UDPNETCommunicator<T> : CommunicatorBase
    {
        #region logger
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        #endregion

        private bool disposedValue = false;

        private int INACTIVITY_TIMER = 4000;

        private const int CONNECTION_TIMEOUT = 2000;
        private const int SEND_TIMEOUT = 2000;

        private int MINIMUM_SEND_GAP = 0;
        private long LastTX = 0;

        private BlockingQueue<byte[]> messageQueu;

        private System.Timers.Timer DetectInactivityTimer;

        private Task senderTask;
        private Task receiverTask;
        private volatile bool exit = false;

        private volatile CommEquipmentObject<UdpClient> udpEq;
        private FrameWrapperBase<T> frameWrapper;

        private IPEndPoint remoteEP;

        private IPEndPoint remoteIPEPSource = new IPEndPoint(IPAddress.Any, 0);
        public IPEndPoint RemoteIPEPSource { get => remoteEPSource as IPEndPoint; }

        private EndPoint remoteEPSource;

        private EndPoint bindEP;

        private byte[] rxBuffer = new byte[65536];

        public UDPNETCommunicator(FrameWrapperBase<T> _frameWrapper = null) : base()
        {
            frameWrapper = _frameWrapper != null ? _frameWrapper : null;
            remoteEPSource = (EndPoint)remoteIPEPSource;
        }

        public override void init(ConnUri uri, bool persistent, string ID, int inactivityMS, int _sendGap = 0)
        {
            if (uri == null || !uri.IsValid)
                return;

            MINIMUM_SEND_GAP = _sendGap;
            frameWrapper?.SetID(ID);

            messageQueu = new BlockingQueue<byte[]>();

            CommsUri = uri;
            SetIPChunks(uri.IP);

            udpEq = new CommEquipmentObject<UdpClient>(ID, uri, null, persistent);
            senderTask = new Task(doSendStart, TaskCreationOptions.LongRunning);
            receiverTask = new Task(Connect2EquipmentCallback, TaskCreationOptions.LongRunning);

            remoteEP = new IPEndPoint(IPAddress.Parse(uri.IP), uri.Port);
            if (string.IsNullOrEmpty(uri.BindIP))
                bindEP = new IPEndPoint(IPAddress.Any, udpEq.ConnUri.LocalPort);
            else
                bindEP = new IPEndPoint(IPAddress.Parse(uri.BindIP), udpEq.ConnUri.LocalPort);

            INACTIVITY_TIMER = inactivityMS;
            
        }

        private void OnInactivityTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Check last time comms was done
            long now = TimeTools.GetCoarseMillisNow();

            if (Math.Abs(now - udpEq.timeLastIncoming) > INACTIVITY_TIMER)
                ClientDown();
        }

        private void ClientDown()
        {
            if (!udpEq.Connected)
                return;

            //if (udpEq == null)
            //    return;

            logger.Info("ClientDown - " + udpEq.ID);

            try
            {
                udpEq.ClientImpl?.Close();
                udpEq.ClientImpl?.Dispose();
            }
            catch (Exception e)
            {
                logger.Error(e, "ClientDown Exception");
            }
            finally
            {
                udpEq.ClientImpl = null;
            }

            // Launch Event
            FireConnectionEvent(udpEq.ID, udpEq.ConnUri, false);

            udpEq.Connected = false;
        }

        private void ClientUp(UdpClient o)
        {
            if (!udpEq.Connected)
            {
                udpEq.Connected = true;
                // Launch Event
                FireConnectionEvent(udpEq.ID, udpEq.ConnUri, true);
            }
        }

        public override void sendASync(byte[] serializedObject, int length)
        {
            messageQueu?.Enqueue(serializedObject);
        }

        public override void sendSync(byte[] bytes, int offset, int length)
        {
            Send2Equipment(bytes, offset, length, udpEq);
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

                    Send2Equipment(data, 0, data.Length, udpEq);
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Exception in messageQueue");
                }
                
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void Send2Equipment(byte[] data, int offset, int length, CommEquipmentObject<UdpClient> o)
        {
            if (o == null || o.ClientImpl == null)
                return;

            string ID = o.ID;
            int nSent = 0;
            UdpClient t = o.ClientImpl;
            try
            {
                nSent = t.Client.SendTo(data, offset, length, SocketFlags.None, remoteEP);
                LastTX = TimeTools.GetCoarseMillisNow();
            }
            catch (Exception e)
            {
                logger.Error(e, "Error while sending UDPNet");
                // Client Down
                ClientDown();
            }
        }

        private void Connect2EquipmentCallback()
        {
            while (!exit)
            {
                if (udpEq.ClientImpl == null)
                {
                    Thread.Sleep(CONNECTION_TIMEOUT);
                    logger.Info("Waiting for new connection");

                    // Blocks here for timeout
                    using (UdpClient t = new UdpClient())
                    {
                        try
                        {
                            t.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            t.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 25);
                            t.Client.Bind(bindEP);

                            if (IsMulticast(udpEq.ConnUri.IP, out IPAddress adr))
                                JoinMulticastOnSteroids(t.Client, udpEq.ConnUri.IP);
                                

                            if (t != null)
                            {
                                int rx;
                                udpEq.ClientImpl = t;
                                try
                                {
                                    // Make first reception with RecveiveFrom. Just first to avoid allocations for IPEndPoint
                                    if ((rx = udpEq.ClientImpl.Client.ReceiveFrom(rxBuffer, ref remoteEPSource)) > 0)
                                    {
                                        // Launch event and Add to Dictionary of valid connections
                                        ClientUp(t);
                                        // Update RX Time
                                        udpEq.timeLastIncoming = TimeTools.GetCoarseMillisNow();

                                        // RAW Data Event
                                        FireDataEvent(CommsUri.IP,
                                                         CommsUri.Port,
                                                         HelperTools.GetLocalMicrosTime(),
                                                         rxBuffer,
                                                         0,
                                                         rx,
                                                         udpEq.ID,
                                                         ipChunks);

                                        // Feed to FrameWrapper
                                        frameWrapper?.AddBytes(rxBuffer, rx);
                                    }
                                    // Following receives does not allocate remoteEPSource
                                    while ((rx = udpEq.ClientImpl.Client.Receive(rxBuffer)) > 0)
                                    {
                                        // Update RX Time
                                        udpEq.timeLastIncoming = TimeTools.GetCoarseMillisNow();

                                        // RAW Data Event
                                        FireDataEvent(CommsUri.IP,
                                                         CommsUri.Port,
                                                         HelperTools.GetLocalMicrosTime(),
                                                         rxBuffer,
                                                         0,
                                                         rx,
                                                         udpEq.ID,
                                                         ipChunks);

                                        // Feed to FrameWrapper
                                        frameWrapper?.AddBytes(rxBuffer, rx);
                                    }
                                }
                                catch (Exception e)
                                {
                                    logger.Error(e, "Error while receiving UDPNet");
                                }
                                finally
                                {
                                    ClientDown();
                                }
                            }
                        } catch (Exception eInner)
                        {
                            logger.Error(eInner, "Error while connecting Inner");
                        }
                    }
                }
            }

            logger.Info("Exited Connect2EquipmentCallback");
        }


        public override void start()
        {
            logger.Info("Start");
            exit = false;
            

            senderTask.Start();
            receiverTask.Start();

            if (INACTIVITY_TIMER > 0)
            {
                // Task Detect Inactive Clients
                DetectInactivityTimer = new System.Timers.Timer(INACTIVITY_TIMER);
                DetectInactivityTimer.AutoReset = true;
                DetectInactivityTimer.Elapsed += OnInactivityTimer;
                DetectInactivityTimer.Enabled = true;
            }

            State = STATE.RUNNING;
        }

        public async override Task stop()
        {
            logger.Info("Stop");
            exit = true;

            messageQueu.reset();
            udpEq.ClientImpl?.Dispose();

            if (DetectInactivityTimer != null)
            {
                DetectInactivityTimer.Elapsed -= OnInactivityTimer;
                DetectInactivityTimer.Stop();
                DetectInactivityTimer.Dispose();
                DetectInactivityTimer = null;
            }

            await senderTask;
            await receiverTask;

            State = STATE.STOP;
        }

        private bool IsMulticast(string ip, out IPAddress adr)
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

        private void JoinMulticastOnSteroids(Socket s, string multicastIP)
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nics)
            {
                IPInterfaceProperties ip_properties = adapter.GetIPProperties();
                //if (!adapter.GetIPProperties().MulticastAddresses.Any())
                //    continue; // most of VPN adapters will be skipped
                //if (!adapter.SupportsMulticast)
                //    continue; // multicast is meaningless for this type of connection
                //if (OperationalStatus.Up != adapter.OperationalStatus)
                //    continue; // this adapter is off or not connected
                //IPv4InterfaceProperties p = adapter.GetIPProperties().GetIPv4Properties();
                //if (null == p)
                //    continue; // IPv4 is not configured on this adapter
                //s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, (int)IPAddress.HostToNetworkOrder(p.Index));
                //if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                //{
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

        protected override async void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    await stop();

                    messageQueu.Dispose();
                    udpEq.ClientImpl?.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.
                messageQueu = null;
                udpEq.ClientImpl = null;

                disposedValue = true;
            }

            base.Dispose(disposing);
        }
    }

    

}
