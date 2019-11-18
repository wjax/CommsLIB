using CommsLIB.Base;
using CommsLIB.Communications;
using CommsLIB.Helper;
using CommsLIB.SmartPcap.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CommsLIB.SmartPcap
{
    public class NetRecorder
    {
        public enum State
        {
            Stoped,
            Recording
        }

        #region const
        private const int SAMPLES_DATARATE_AVERAGE = 100;
        #endregion

        #region member
        private string filePath = @".\dumptest.mpg";
        private string idxFilePath = @".\dumptest.idx";
        private FileStream file;
        private FileStream idxFile;
        private byte[] idxBuffer;
        private byte[] headerBuffer;

        private long timeOffset = long.MinValue;

        long correctedTime = 0;
        int secTime;

        private int secsIdxInterval;

        public delegate void StatusDelegate(State state);
        public event StatusDelegate StatusEvent;
        public delegate void ProgressDelegate(float mb, int secs);
        public event ProgressDelegate ProgressEvent;
        public delegate void DataRateDelegate(Dictionary<string, float> dataRates);
        public event DataRateDelegate DataRateEvent;

        private Dictionary<string, UDPNETCommunicator<object>> udpListeners = new Dictionary<string, UDPNETCommunicator<object>>();

        public State CurrentState { get; set; }

        private List<string> _IDs = new List<string>();
        private Dictionary<string, float> _dataRate = new Dictionary<string, float>();
        #endregion

        #region timer
        private Timer eventTimer;
        #endregion

        public NetRecorder()
        {
            idxBuffer = new byte[HelperTools.idxDataSize];
            headerBuffer = new byte[HelperTools.headerSize];
        }

        private void OnEventTimer(object state)
        {
            // Write IDX
            WriteIdx();
            // Datarate event
            DataRateEvent?.Invoke(_dataRate);
            // Update Time
            secTime++;

            // Status event
            if (CurrentState == State.Recording)
                ProgressEvent?.Invoke((float)(file.Position / 1000000.0), secTime);
        }

        public bool AddPeer(string ID, string ip, int port, string netcard = "")
        {
            // Check valid IP
            if (IPAddress.TryParse(ip, out IPAddress ipAddres) && !udpListeners.ContainsKey(ID))
            {
                UDPNETCommunicator<object> u = new UDPNETCommunicator<object>(null, false);
                u.Init(new ConnUri($"udp://{ip}::{netcard}:{port}"), false, ID, 0);
                u.DataReadyEvent += DataReadyEventCallback;
                u.DataRateEvent += OnDataRate;

                udpListeners.Add(ID, u);
                _dataRate.Add(ID, 0);

                return true;
            }

            return false;
        }

        public bool RemovePeer(string id)
        {
            if (udpListeners.TryGetValue(id, out var u))
            {
                u.DataReadyEvent -= DataReadyEventCallback;
                u.DataRateEvent -= OnDataRate;
            }

            _dataRate.Remove(id);
            return udpListeners.Remove(id);
        }

        public void AddPeer<T>(CommunicatorBase<T> commLink)
        {
            _dataRate.Add(commLink.ID, 0);
            commLink.DataReadyEvent += DataReadyEventCallback;
            commLink.DataRateEvent += OnDataRate;
        }

        public void RemovePeer<T>(CommunicatorBase<T> commLink)
        {
            try
            {
                commLink.DataReadyEvent -= DataReadyEventCallback;
                _dataRate.Remove(commLink.ID);
            }
            catch {
                
            }
        }

        public void RemoveAllPeers()
        {

        }

        private void OnDataRate(string ID, float Mbps)
        {
            _dataRate[ID] = Mbps;
        }


        // Time is microseconds
        [MethodImpl(MethodImplOptions.Synchronized)]
        private void DataReadyEventCallback(string ip, int port, long time, byte[] buff, int rawDataOffset, int rawDataSize, string ID, ushort[] ipChunks)
        {
            // Do not follow on if not recording
            if (CurrentState != State.Recording)
                return;

            // Just started recording. Need to reset time
            if (timeOffset == long.MinValue)
            {
                timeOffset = time;
                // Write idx Header
                HelperTools.Long2Bytes(idxBuffer, 0, HelperTools.millisFromEpochNow());
                HelperTools.Int32Bytes(idxBuffer, 8, secsIdxInterval);
                idxFile.Write(idxBuffer, 0, 12);
            }

            correctedTime = time - timeOffset;

            // Fill header
            FillHeader(correctedTime, ID, rawDataSize, headerBuffer, ipChunks, port);
            // Save to file
            file.Write(headerBuffer, 0, HelperTools.headerSize);
            file.Write(buff, rawDataOffset, rawDataSize);
        }

        //private void UpdateAllDataRates()
        //{
        //    foreach (string key in _IDs)
        //    {
        //        //_dataRate[key] = _dataRateCalculators[key].Average * 8; // Mbps
        //        //Console.WriteLine($"DataReady {key}: {_dataRate[key]} - Count: {_loopCount[key]}");
        //        _dataRate[key] = (_loopCount[key] * 8f)/ 1048576; // 
        //        _loopCount[key] = 0;
        //    }
        //}

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void WriteIdx()
        {
            // Write index
            HelperTools.Int32Bytes(idxBuffer, 0, secTime);
            HelperTools.Long2Bytes(idxBuffer, 4, file.Position);

            idxFile.Write(idxBuffer, 0, HelperTools.idxDataSize);
        }

        private void FillHeader(long _time, string _ID, int _size, byte[] _buffer, ushort[] ipChunks, int _port)
        {
            // Time 8
            HelperTools.Long2Bytes(_buffer, 0, _time);
            // Hashed ID 4
            HelperTools.Int32Bytes(_buffer, 8, HelperTools.GetDeterministicHashCode(_ID));
            // IP 4
            for (int i = 0; i < 4; i++)
                _buffer[12 + i] = (byte)ipChunks[i];

            // Port 4
            HelperTools.Int32Bytes(_buffer, 16, _port);
            // Size 4
            HelperTools.Int32Bytes(_buffer, 20, _size);
        }

        public void SetFilePath(string _filePath)
        {
            if (CurrentState != State.Recording)
            {
                string folder = Path.GetDirectoryName(_filePath);

                filePath = _filePath+".raw";
                idxFilePath = Path.Combine(folder, Path.GetFileNameWithoutExtension(filePath) + ".idx");
            }
        }

        public void Start(int _secsIdxInterval)
        {
            // Delete previous file if exists
            if (File.Exists(filePath))
                File.Delete(filePath);

            // Delete previous file if exists
            if (File.Exists(idxFilePath))
                File.Delete(idxFilePath);

            secsIdxInterval = _secsIdxInterval;
            timeOffset = long.MinValue;
            secTime = 0;

            // Open file
            file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            idxFile = new FileStream(idxFilePath, FileMode.Create, FileAccess.Write,FileShare.None);

            // Start Timer
            eventTimer = new Timer(OnEventTimer, null, 0, 1000);

            foreach (var u in udpListeners.Values)
                u.Start();

            CurrentState = State.Recording;
            StatusEvent?.Invoke(CurrentState);
        }

        public async Task Stop()
        {
            List<Task> tasks = new List<Task>();
            foreach (var u in udpListeners.Values)
                tasks.Add(u.Stop());

            await Task.WhenAll(tasks);
            //Task.WaitAll(tasks.ToArray());

            eventTimer.Dispose();
            eventTimer = null;
            //foreach (var u in udpListeners)
            //    u.Dispose();

            file.Close();
            file.Dispose();
            idxFile.Close();
            idxFile.Dispose();

            //udpListeners.Clear();

            CurrentState = State.Stoped;
            StatusEvent?.Invoke(CurrentState);
        }

        public void Dispose()
        {
        }
    }
}
