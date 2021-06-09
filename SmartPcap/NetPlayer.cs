using CommsLIB.Base;
using CommsLIB.Communications;
using CommsLIB.Logging;
using CommsLIB.SmartPcap.Base;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CommsLIB.SmartPcap
{
    public class NetPlayer : IDisposable
    {
        public enum State
        {
            Stoped,
            Playing,
            Paused
        }

        #region logger
        private readonly ILogger<NetPlayer> logger = null;
        #endregion

        public delegate void DataRateDelegate(Dictionary<string, float> dataRates);
        public event DataRateDelegate DataRateEvent;

        #region members
        private string filePath = "";
        private string idxFilePath = "";
        private FileStream file;
        private FileStream idxFile;

        private int secsIdxInterval;
        private bool onlyLocal = false;

        private byte[] buffer;

        private byte[] idxBuffer = new byte[HelperTools.idxIndexSize];

        private Task runningJob;
        private CancellationTokenSource cancelSource;
        private CancellationToken cancelToken;

        private bool resetTimeOffsetRequired = true;
        private bool has2UpdatePosition = false;
        private long newPosition = 0;

        private Dictionary<int, PlayPeerInfo> udpSenders = new Dictionary<int, PlayPeerInfo>();
        private Dictionary<int, PlayPeerInfo> udpSendersOriginal = new Dictionary<int, PlayPeerInfo>();
        private Dictionary<string, float> _dataRate = new Dictionary<string, float>();

        public delegate void StatusDelegate(State state);
        public event StatusDelegate StatusEvent;
        public delegate void ProgressDelegate(int time);
        public event ProgressDelegate ProgressEvent;

        private DateTime RecDateTime = DateTime.MinValue;

        public State CurrentState { get; private set; }

        private Dictionary<long, long> timeIndexes = new Dictionary<long, long>();
        private int lastTime;
        private long replayTime;

        #endregion

        #region timer
        private Timer eventTimer;
        #endregion

        ArrayPool<byte> bytePool = ArrayPool<byte>.Shared;


        public NetPlayer()
        {
            buffer = bytePool.Rent(HelperTools.SIZE_BYTES);
            logger = this.GetLogger();
        }

        public static (ICollection<PlayPeerInfo>, DateTime, int, long) GetRecordingInfo(string _idxFilePath)
        {
            int duration = 0;
            int nPeers = 0;
            DateTime date = DateTime.MinValue;
            List<PlayPeerInfo> peers = new List<PlayPeerInfo>();

            using (FileStream idxFile = new FileStream(_idxFilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader idxBinaryReader = new BinaryReader(idxFile))
            {
                byte[] auxBuff = new byte[1024];

                // Read DateTime, Secs Interval and Peers info
                if (idxBinaryReader.Read(auxBuff, 0, 16) == 16)
                {
                    date = HelperTools.fromMillis(BitConverter.ToInt64(auxBuff, 0));
                    int secsIdxInterval = BitConverter.ToInt32(auxBuff, 8);
                    nPeers = BitConverter.ToInt32(auxBuff, 12);

                    // Read peers
                    for (int i = 0; i < nPeers; i++)
                    {
                        string ID = HelperTools.Bytes2StringWithLength(idxFile);
                        string IP = HelperTools.Bytes2StringWithLength(idxFile);
                        int Port = idxBinaryReader.ReadInt32();

                        peers.Add(new PlayPeerInfo
                            {
                                ID = ID,
                                IP = IP,
                                Port = Port
                            });
                    }

                    // Guess duration
                    FileInfo fInfo = new FileInfo(_idxFilePath);
                    duration = ((int)((fInfo.Length - idxBinaryReader.BaseStream.Position) / HelperTools.idxIndexSize)) * secsIdxInterval;
                }
            }

            string rawFile = Path.Combine(Path.GetDirectoryName(_idxFilePath), Path.GetFileNameWithoutExtension(_idxFilePath)) + ".raw";
            FileInfo fRaw = new FileInfo(rawFile);

            return (peers, date, duration, fRaw.Length);
        }

        public ICollection<PlayPeerInfo> LoadFile(string _filePath, out DateTime _startTime, out int _lastTime)
        {
            Stop();

            _lastTime = 0;

            filePath = _filePath;
            idxFilePath = Path.Combine(Path.GetDirectoryName(filePath),Path.GetFileNameWithoutExtension(filePath)) + ".idx";

            // Init times
            lastTime = 0;
            _startTime = DateTime.MinValue;
            timeIndexes.Clear();
            int nPeers = 0;
            udpSendersOriginal.Clear();
            udpSenders.Clear();
            _dataRate.Clear();
            

            using (FileStream idxFile = new FileStream(idxFilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader idxBinaryReader = new BinaryReader(idxFile))
            {
                int time = 0; long position = 0;
                byte[] auxBuff = bytePool.Rent(4096);

                // Read DateTime, Secs Interval and Peers info
                if (idxBinaryReader.Read(auxBuff, 0, 16) == 16)
                {
                    RecDateTime = _startTime = HelperTools.fromMillis(BitConverter.ToInt64(auxBuff, 0));
                    secsIdxInterval = BitConverter.ToInt32(auxBuff, 8);
                    nPeers = BitConverter.ToInt32(auxBuff, 12);

                    // Read peers
                    for (int i = 0; i<nPeers;i++)
                    {
                        string ID = HelperTools.Bytes2StringWithLength(idxFile);
                        string IP = HelperTools.Bytes2StringWithLength(idxFile);
                        int Port = idxBinaryReader.ReadInt32();

                        udpSenders.Add(HelperTools.GetDeterministicHashCode(ID),
                            new PlayPeerInfo {
                                ID = ID,
                                IP = IP,
                                Port = Port
                            });

                        udpSendersOriginal.Add(HelperTools.GetDeterministicHashCode(ID),
                            new PlayPeerInfo
                            {
                                ID = ID,
                                IP = IP,
                                Port = Port
                            });

                        _dataRate[ID] = 0f;
                    }

                    // Get All times to cache
                    while (idxBinaryReader.Read(auxBuff, 0, HelperTools.idxIndexSize) == HelperTools.idxIndexSize)
                    {
                        time = BitConverter.ToInt32(auxBuff, 0);
                        position = BitConverter.ToInt64(auxBuff, 4);

                        try
                        {
                            timeIndexes.Add(time, position);
                        }
                        catch(Exception e)
                        {
                            logger?.LogError(e, "Error reading idx file. TimeIndexes");
                        }
                    }
                    _lastTime = lastTime = time;
                }
                bytePool.Return(auxBuff);
            }

            return udpSenders.Values;
        }

        private void OnDataRate(string ID, float MbpsTX)
        {
            _dataRate[ID] = MbpsTX;
        }

        public void Seek(int _time)
        {
            // Find closest time in Dictionary
            if (_time <= lastTime)
            {
                int seqTime = (_time / (secsIdxInterval == 0 ? 1: secsIdxInterval)) * secsIdxInterval;

                if (timeIndexes.ContainsKey(seqTime))
                {
                    // DO actual Seek
                    newPosition = timeIndexes[seqTime];
                    has2UpdatePosition = true;
                }
            }
        }

        public void Play(bool _onlyLocal = true)
        {
            if (CurrentState == State.Playing)
                return;

            if (string.IsNullOrEmpty(filePath))
                return;

            //if (onlyLocal != _onlyLocal)
            //    CloseSenders();

            if (CurrentState == State.Paused)
                Resume();
            else
            {
                resetTimeOffsetRequired = true;

                onlyLocal = _onlyLocal;
                cancelSource = new CancellationTokenSource();
                cancelToken = cancelSource.Token;
                runningJob = new Task(() => RunReaderSenderProcessCallback(filePath, idxFilePath, cancelToken), cancelToken, TaskCreationOptions.None);
                CurrentState = State.Playing;

                // Create Senders
                foreach (PlayPeerInfo p in udpSenders.Values)
                {
                    var u = new UDPSender(p.ID, p.IP, p.Port, false);
                    u.DataRateEvent += OnDataRate;
                    u.Start();

                    p.commsLink = u;
                }

                // Start Timer
                eventTimer = new Timer(OnEventTimer, null, 0, 1000);

                runningJob.Start();
                
                StatusEvent?.Invoke(CurrentState);
            }
        }

        private void CloseSenders()
        {
            foreach (var u in udpSenders.Values)
            {
                u.commsLink.DataRateEvent -= OnDataRate;
                (u.commsLink as IDisposable).Dispose();
            }

            //udpSenders.Clear();
        }

        private void RunReaderSenderProcessCallback(string _filePath, string _idxFilePath, CancellationToken token)
        {
            ulong ipport;
            int n_bytes = 0, size = 0;
            int hashedID = 0;
            long timeoffset = 0, waitTime = 0, nowTime = 0, time = 0;
            int lastTimeStatusS = 0, timeStatusS = 0;

            // Increase timers resolution
            WinAPI.WinAPITime.TimeBeginPeriod(1);

            // Open file
            using (FileStream file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                if (has2UpdatePosition)
                {
                    file.Seek(newPosition, SeekOrigin.Begin);
                    has2UpdatePosition = false;
                    resetTimeOffsetRequired = true;
                }

                while (!token.IsCancellationRequested)
                {
                    // Paused
                    if (CurrentState == State.Paused)
                    {
                        Thread.Sleep(500);
                    }
                    else
                    {
                        if ((n_bytes = file.Read(buffer, 0, HelperTools.headerSize)) == HelperTools.headerSize)
                        {
                            // Get fields
                            replayTime = time = BitConverter.ToInt64(buffer, 0);
                            hashedID = BitConverter.ToInt32(buffer, 8);
                            ipport = BitConverter.ToUInt64(buffer, 12);
                            size = BitConverter.ToInt32(buffer, 20);

                            // Update time in secs
                            timeStatusS = (int)(replayTime / 1000_000);

                            // Read Payload
                            n_bytes = file.Read(buffer, 0, size);

                            nowTime = HelperTools.GetLocalMicrosTime();

                            if (resetTimeOffsetRequired)
                            {
                                timeoffset = nowTime - time;
                                waitTime = 0;
                                resetTimeOffsetRequired = false;
                            }
                            else
                            {
                                nowTime -= timeoffset;
                                waitTime = time - nowTime;
                                if (waitTime > 1000) // 1ms
                                    Thread.Sleep((int)(waitTime / 1000));
                            }

                            // Send
                            Send(ipport, hashedID, buffer, 0, n_bytes);

                            // Update Progress
                            if (timeStatusS != lastTimeStatusS)
                            {
                                Task.Run(()=> ProgressEvent?.Invoke(timeStatusS));
                                lastTimeStatusS = timeStatusS;
                            }

                            if (has2UpdatePosition)
                            {
                                file.Seek(newPosition, SeekOrigin.Begin);
                                has2UpdatePosition = false;
                                resetTimeOffsetRequired = true;
                            }
                        }
                        else
                        {
                            // End of File
                            cancelSource.Cancel();
                            // Update Stop Status
                            CurrentState = State.Stoped;
                            StatusEvent?.Invoke(CurrentState);
                            foreach (var k in _dataRate.Keys.ToList())
                                _dataRate[k] = 0;
                            DataRateEvent?.Invoke(_dataRate);
                            // rewind
                            Seek(0);
                            eventTimer.Dispose();
                            eventTimer = null;
                            ProgressEvent?.Invoke(0);
                        }
                    }
                }
            }
            WinAPI.WinAPITime.TimeEndPeriod(1);
        }

        private void Send(ulong _ipport, int hashedID, byte[] _buff, int offset, int count)
        {
            if (udpSenders.TryGetValue(hashedID, out PlayPeerInfo p) && p.IsEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"Sending {hashedID} : {count}");
                p.commsLink.Send(_buff, offset, count);
            }
        }

        public void SetPeerEnabled(string _id, bool enabled)
        {
            int hashedID = HelperTools.GetDeterministicHashCode(_id);
            if (udpSenders.TryGetValue(hashedID, out PlayPeerInfo p))
                p.IsEnabled = enabled;
        }

        public bool AddReplayAddress(string _ID, string _dstIP, int _dstPort, string _nic)
        {
            if (CurrentState != State.Stoped)
                return false;

            if (_dstIP is null || _dstPort <= 0 || _dstPort > 65535)
                return false;

            int hashedID = HelperTools.GetDeterministicHashCode(_ID);
            if (udpSenders.TryGetValue(hashedID, out PlayPeerInfo p))
            {
                p.IP = _dstIP;
                p.Port = _dstPort;
                p.NIC = _nic;

                return true;
            }

            return false;
        }

        public bool RemovePeer(string _id)
        {
            if (CurrentState != State.Stoped)
                return false;

            return udpSenders.Remove(HelperTools.GetDeterministicHashCode(_id));
        }

        public bool RemoveReplayAddress(string _id, out PlayPeerInfo pOriginal)
        {
            pOriginal = null;

            if (CurrentState != State.Stoped)
                return false;

            int hashedID = HelperTools.GetDeterministicHashCode(_id);
            if (udpSenders.TryGetValue(hashedID, out PlayPeerInfo p))
            {
                var original = udpSendersOriginal[hashedID];

                p.IP = original.IP;
                p.Port = original.Port;
                p.NIC = original.NIC;

                pOriginal = original;

                return true;
            }

            return false;
        }

        public void Pause()
        {
            if (CurrentState == State.Playing)
            {
                CurrentState = State.Paused;
                StatusEvent?.Invoke(CurrentState);
            }
        }

        public void Resume()
        {
            if (CurrentState == State.Paused)
            {
                resetTimeOffsetRequired = true;
                CurrentState = State.Playing;
                StatusEvent?.Invoke(CurrentState);
            }
        }

        public void Stop()
        {
            if (CurrentState == State.Stoped)
                return;

            if (cancelToken != null && cancelToken.CanBeCanceled)
            {
                cancelSource.Cancel();
                runningJob.Wait();
            }

            // Clean Senders
            CloseSenders();

            eventTimer?.Dispose();
            eventTimer = null;

            CurrentState = State.Stoped;
            StatusEvent?.Invoke(CurrentState);

            foreach (var k in _dataRate.Keys.ToList())
                _dataRate[k] = 0;
            DataRateEvent?.Invoke(_dataRate);

            ProgressEvent?.Invoke(0);
        }

        private void OnEventTimer(object state)
        {
            // Status event
            if (CurrentState == State.Playing)
            {
                // Datarate event
                DataRateEvent?.Invoke(_dataRate);
                //ProgressEvent?.Invoke((int)(replayTime / 1000000));
            }
        }

        public void Dispose()
        {
            CloseSenders();
            bytePool.Return(buffer);
        }
    }
}
