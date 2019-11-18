using CommsLIB.Base;
using CommsLIB.Communications.FrameWrappers;
using CommsLIB.Helper;
using CommsLIB.SmartPcap;
using System;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CommsLIB.Communications
{
    //public class SERIALCommunicator<T> : CommunicatorBase<T>
    //{
    //    private int INACTIVITY_TIMER = 4000;

    //    private const int CONNECTION_TIMEOUT = 3000;
    //    private const int SEND_TIMEOUT = 2000;

    //    private int MINIMUM_SEND_GAP = 0;
    //    private long LastTX = 0;

    //    private BlockingQueue<byte[]> messageQueu;

    //    private System.Timers.Timer DetectInactivityTimer;

    //    private Task senderTask;
    //    private volatile bool exit = false;

    //    private volatile CommEquipmentObject<SerialPort> serialEq;
    //    private FrameWrapperBase<T> frameWrapper;

    //    private byte[] rxBuffer = new byte[65536];

    //    public SERIALCommunicator(FrameWrapperBase<T> _frameWrapper = null) : base()
    //    {
    //        messageQueu = new BlockingQueue<byte[]>();
    //        frameWrapper = _frameWrapper != null ? _frameWrapper : null;
    //    }

    //    public override void init(ConnUri uri, bool persistent, string ID, int inactivityMS, int _sendGap = 0)
    //    {
    //        if (uri == null || !uri.IsValid)
    //            return;

    //        MINIMUM_SEND_GAP = _sendGap;
    //        frameWrapper?.SetID(ID);

    //        serialEq = new CommEquipmentObject<SerialPort>(ID, uri, null, persistent);
    //        senderTask = new Task(doSendStart, TaskCreationOptions.LongRunning);

    //        INACTIVITY_TIMER = inactivityMS;
    //        if (INACTIVITY_TIMER > 0)
    //        {
    //            // Task Detect Inactive Clients
    //            DetectInactivityTimer = new System.Timers.Timer(INACTIVITY_TIMER);
    //            DetectInactivityTimer.AutoReset = true;
    //            DetectInactivityTimer.Elapsed += OnInactivityTimer;
    //            DetectInactivityTimer.Enabled = true;
    //        }
    //    }

    //    private void OnInactivityTimer(object sender, System.Timers.ElapsedEventArgs e)
    //    {
    //        // Check last time comms was done
    //        long now = TimeTools.GetCoarseMillisNow();

    //        if (Math.Abs(now - serialEq.timeLastIncoming) > INACTIVITY_TIMER * 10000)
    //            ClientDown();
    //    }

    //    private void ClientDown()
    //    {
    //        if (serialEq == null)
    //            return;

    //        System.Diagnostics.Debug.WriteLine("ClientDown - " + serialEq.ID);

    //        try
    //        {
    //            serialEq.ClientImpl?.Close();
    //            serialEq.ClientImpl?.Dispose();
    //        }
    //        finally
    //        {
    //            serialEq.ClientImpl = null;
    //        }

    //        // Launch Event
    //        FireConnectionEvent(serialEq.ID, serialEq.ConnUri, false);
    //    }

    //    private void ClientUp(SerialPort o)
    //    {
    //        serialEq.ClientImpl = o;

    //        // Launch Event
    //        FireConnectionEvent(serialEq.ID, serialEq.ConnUri, true);
    //    }

    //    public override void sendASync(byte[] serializedObject, int length)
    //    {
    //        messageQueu.Enqueue(serializedObject);
    //    }

    //    public override void sendSync(byte[] bytes, int offset, int length)
    //    {
    //        Send2Equipment(bytes, offset, length, serialEq);
    //    }

    //    private void doSendStart()
    //    {
    //        long toWait = 0;
    //        while (!exit)
    //        {

    //            byte[] data = messageQueu.Dequeue();

    //            if ((toWait = DateTime.UtcNow.ToFileTimeUtc() - LastTX) < MINIMUM_SEND_GAP)
    //                Thread.Sleep((int)toWait);

    //            Send2Equipment(data, 0, data.Length, serialEq);
    //        }
    //    }

    //    [MethodImpl(MethodImplOptions.Synchronized)]
    //    private void Send2Equipment(byte[] data, int offset, int length, CommEquipmentObject<SerialPort> o)
    //    {
    //        if (o == null || o.ClientImpl == null)
    //            return;

    //        string ID = o.ID;
    //        SerialPort t = o.ClientImpl;
    //        try
    //        {
    //            t?.Write(data, offset, length);
    //            LastTX = DateTime.UtcNow.ToFileTimeUtc();
    //        }
    //        catch (Exception e)
    //        {
    //            // Client Down
    //            ClientDown();
    //        }
    //    }

    //    private void Connect2EquipmentCallback()
    //    {
    //        while (!exit)
    //        {
    //            if (serialEq.ClientImpl == null)
    //            {
    //                System.Diagnostics.Debug.WriteLine("Connect2EquipmentCallback - Entry");

    //                // Blocks here for timeout
    //                using (SerialPort t = new SerialPort(serialEq.ConnUri.SerialPort)
    //                                                    {
    //                                                        BaudRate = serialEq.ConnUri.SerialBPS,
    //                                                        Parity = Parity.None,
    //                                                        StopBits = StopBits.One,
    //                                                        DataBits = 8,
    //                                                        Handshake = Handshake.None
    //                                                    })
    //                {
    //                    if (t != null)
    //                    {
    //                        t.Open();
    //                        // Launch event and Add to Dictionary of valid connections
    //                        ClientUp(t);

    //                        int rx;

    //                        try
    //                        {
    //                            while ((rx = serialEq.ClientImpl.Read(rxBuffer, 0, rxBuffer.Length)) > 0)
    //                            {
    //                                // Update RX Time
    //                                serialEq.timeLastIncoming = TimeTools.GetCoarseMillisNow(); 

    //                                // RAW Data Event
    //                                FireDataEvent(null, 0, HelperTools.GetLocalMicrosTime(), rxBuffer, 0, rx, serialEq.ID);

    //                                // Feed to FrameWrapper
    //                                frameWrapper?.AddBytes(rxBuffer, rx);
    //                            }
    //                        }
    //                        finally
    //                        {
    //                            ClientDown();
    //                        }
    //                    }
    //                }
    //            }
    //        }
    //    }

    //    public override void start()
    //    {
    //        senderTask.Start();

    //        ThreadPool.QueueUserWorkItem(state => Connect2EquipmentCallback());

    //        State = STATE.RUNNING;
    //    }

    //    public override async Task stop()
    //    {
    //        exit = true;
    //        State = STATE.STOP;
    //    }

    //    protected override void Dispose(bool disposing)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public override void sendSync(T Message)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public override string getID()
    //    {
    //        return serialEq.ID;
    //    }
    //}
}
