using CommsLIB.SmartPcap.Base;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
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

        #region members
        private string filePath = "";
        private string idxFilePath = "";
        private FileStream file;
        private FileStream idxFile;

        private int secsIdxInterval;
        private bool onlyLocal = false;

        private byte[] buffer;

        private byte[] idxBuffer = new byte[HelperTools.idxDataSize];

        private Task runningJob;
        private CancellationTokenSource cancelSource;
        private CancellationToken cancelToken;

        private bool resetTimeOffsetRequired = true;
        private bool has2UpdatePosition = false;
        private long newPosition = 0;

        private Dictionary<int, UDPSender> udpSenders = new Dictionary<int, UDPSender>();
        private Dictionary<int, ulong> udpReplayAddresses = new Dictionary<int, ulong>();

        public delegate void StatusDelegate(State state);
        public event StatusDelegate StatusEvent;
        public delegate void ProgressDelegate(int time);
        public event ProgressDelegate ProgressEvent;

        private DateTime RecDateTime = DateTime.MinValue;

        public State CurrentState { get; private set; }

        private Dictionary<long, long> timeIndexes = new Dictionary<long, long>();
        private int lastTime;

        private string netCard = "";

        #endregion

        ArrayPool<byte> bytePool = ArrayPool<byte>.Shared;


        public NetPlayer(string _netcard)
        {
            netCard = _netcard;
            buffer = bytePool.Rent(HelperTools.SIZE_BYTES);
        }

        public int LoadFile(string _filePath, out DateTime _startTime)
        {
            filePath = _filePath;
            idxFilePath = Path.Combine(Path.GetDirectoryName(filePath),Path.GetFileNameWithoutExtension(filePath)) + ".idx";

            // Init times
            lastTime = 0;
            _startTime = DateTime.MinValue;
            timeIndexes.Clear();
            using (FileStream idxFile = new FileStream(idxFilePath, FileMode.Open, FileAccess.Read))
            {
                int time = 0; long position = 0;
                byte[] auxBuff = bytePool.Rent(HelperTools.idxDataSize);

                // Read DateTime and Secs Interval
                if (idxFile.Read(auxBuff, 0, HelperTools.idxDataSize) == HelperTools.idxDataSize)
                {
                    RecDateTime = _startTime = HelperTools.fromMillis(BitConverter.ToInt64(auxBuff, 0));
                    secsIdxInterval = BitConverter.ToInt32(auxBuff, 8);

                    // Get All times to cache
                    while (idxFile.Read(auxBuff, 0, HelperTools.idxDataSize) == HelperTools.idxDataSize)
                    {
                        time = BitConverter.ToInt32(auxBuff, 0);
                        position = BitConverter.ToInt64(auxBuff, 4);

                        timeIndexes.Add(time, position);
                    }
                    lastTime = time;
                }
                bytePool.Return(auxBuff);
            }

            return lastTime;
        }

        public void Seek(int _time)
        {
            // Find closest time in Dictionary
            if (_time <= lastTime)
            {
                int seqTime = (_time / secsIdxInterval) * secsIdxInterval;

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

            if (onlyLocal != _onlyLocal)
                CloseSenders();

            if (CurrentState == State.Paused)
                Resume();
            else
            {
                resetTimeOffsetRequired = true;

                onlyLocal = _onlyLocal;
                cancelSource = new CancellationTokenSource();
                cancelToken = cancelSource.Token;
                runningJob = new Task(() => RunReaderSenderProcessCallback(filePath, idxFilePath, cancelToken), cancelToken, TaskCreationOptions.LongRunning);
                runningJob.Start();

                CurrentState = State.Playing;
                StatusEvent?.Invoke(CurrentState);
            }
        }

        private void CloseSenders()
        {
            foreach (var u in udpSenders.Values)
                u.Close();

            udpSenders.Clear();
        }

        private void RunReaderSenderProcessCallback(string _filePath, string _idxFilePath, CancellationToken token)
        {
            ulong ipport;
            int n_bytes = 0, size = 0;
            int hashedID = 0;
            long timeoffset = 0, waitTime = 0, nowTime = 0, lastTimeStatus = 0, time = 0;

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
                            time = BitConverter.ToInt64(buffer, 0);
                            hashedID = BitConverter.ToInt32(buffer, 8);
                            ipport = BitConverter.ToUInt64(buffer, 12);
                            size = BitConverter.ToInt32(buffer, 20);

                            // Read Payload
                            n_bytes = file.Read(buffer, 0, size);

                            nowTime = HelperTools.GetLocalMicrosTime();

                            if (resetTimeOffsetRequired)
                            {
                                timeoffset = nowTime - time;
                                waitTime = 0;
                                resetTimeOffsetRequired = false;
                                lastTimeStatus = time;
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

                            // Fire Event every 1 sec
                            if (time - lastTimeStatus > 1000000)
                            {
                                ProgressEvent?.Invoke((int)(time / 1000000));
                                lastTimeStatus = time;
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
                            // rewind
                            Seek(0);
                            ProgressEvent?.Invoke(0);
                        }
                    }
                }
            }
            WinAPI.WinAPITime.TimeEndPeriod(1);
        }

        private void Send(ulong _ipport, int hashedID, byte[] _buff, int offset, int count)
        {
            UDPSender u = null;
            if (!udpSenders.ContainsKey(hashedID))
            {
                ulong dst = _ipport;
                if (udpReplayAddresses.ContainsKey(hashedID))
                    dst = udpReplayAddresses[hashedID];

                u = new UDPSender(dst, onlyLocal, netCard);
                u.Start();
                udpSenders.Add(hashedID, u);
            }
            else
                u = udpSenders[hashedID];

            u.Send(_buff, offset, count);
        }

        public void AddReplayAddress(string _ID, string _dstIP, int _dstPort)
        {
            //ulong src = HelperTools.IPPort2Long(_srcIP, _srcPort);
            int hashedID = HelperTools.GetDeterministicHashCode(_ID);
            ulong dst = HelperTools.IPPort2Long(_dstIP, _dstPort);

            udpReplayAddresses[hashedID] = dst;
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
            if (cancelToken.CanBeCanceled)
            {
                cancelSource.Cancel();
                runningJob.Wait();
            }

            CurrentState = State.Stoped;
            StatusEvent?.Invoke(CurrentState);
        }

        public void Dispose()
        {
            CloseSenders();
            bytePool.Return(buffer);
        }
    }
}
