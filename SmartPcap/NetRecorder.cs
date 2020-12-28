using CommsLIB.Base;
using CommsLIB.Communications;
using CommsLIB.Helper;
using CommsLIB.SmartPcap.Base;
using Microsoft.Extensions.Logging;
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

        #region logger
        private readonly ILogger<NetRecorder> logger = null;
        #endregion

        #region const
        private const int SAMPLES_DATARATE_AVERAGE = 100;
        #endregion

        #region member
        private string filePath = @".\dumptest.mpg";
        private string idxFilePath = @".\dumptest.idx";
        private string folder;
        private string name;
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

        private Dictionary<string, RecPeerInfo> udpListeners = new Dictionary<string, RecPeerInfo>();

        public State CurrentState { get; set; }

        //private List<string> _IDs = new List<string>();
        private Dictionary<string, float> _dataRate = new Dictionary<string, float>();
        #endregion

        #region timer
        private Timer eventTimer;
        #endregion

        public NetRecorder(ILogger<NetRecorder> logger_ = null)
        {
            idxBuffer = new byte[HelperTools.idxIndexSize];
            headerBuffer = new byte[HelperTools.headerSize];
            logger = logger_;
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

        public bool AddPeer(string ID, string ip, int port, bool dumpToFile, string netcard = "", string dumpFileExtension = ".dump")
        {
            // Check valid IP
            if (IPAddress.TryParse(ip, out IPAddress ipAddres) && !udpListeners.ContainsKey(ID))
            {
                UDPNETCommunicator<object> u = new UDPNETCommunicator<object>();
                u.Init(new ConnUri($"udp://{ip}::{netcard}:{port}"), false, ID, 0);
                u.DataReadyEvent += DataReadyEventCallback;
                u.DataRateEvent += OnDataRate;

                RecPeerInfo p = new RecPeerInfo()
                {
                    ID = ID,
                    DumpToFile = dumpToFile,
                    DumpFileExtension = dumpFileExtension,
                    commsLink = u,
                    IP = ip,
                    Port= port
                };

                udpListeners.Add(ID, p);
                _dataRate.Add(ID, 0);

                return true;
            }

            return false;
        }

        public bool RemovePeer(string id)
        {
            if (udpListeners.TryGetValue(id, out var u))
            {
                u.commsLink.DataReadyEvent -= DataReadyEventCallback;
                u.commsLink.DataRateEvent -= OnDataRate;
            }

            _dataRate.Remove(id);
            return udpListeners.Remove(id);
        }

        public void AddPeer(ICommunicator commLink, bool dumpToFile, string dumpFileExtension = ".dump")
        {
            if (!udpListeners.ContainsKey(commLink.ID))
            {
                commLink.DataReadyEvent += DataReadyEventCallback;
                commLink.DataRateEvent += OnDataRate;

                RecPeerInfo p = new RecPeerInfo()
                {
                    ID = commLink.ID,
                    DumpToFile = dumpToFile,
                    DumpFileExtension = dumpFileExtension,
                    commsLink = commLink,
                    IP = commLink.CommsUri.IP,
                    Port = commLink.CommsUri.LocalPort
                };

                udpListeners.Add(commLink.ID, p);
                _dataRate.Add(commLink.ID, 0);
            }
        }

        public void RemovePeer(ICommunicator commLink)
        {
            try
            {
                commLink.DataReadyEvent -= DataReadyEventCallback;
                commLink.DataRateEvent -= OnDataRate;

                _dataRate.Remove(commLink.ID);
                udpListeners.Remove(commLink.ID);
            }
            catch {
                
            }
        }

        public void RemoveAllPeers()
        {

        }

        private void OnDataRate(string ID, float MbpsRX, float MbpsTX)
        {
            _dataRate[ID] = MbpsRX;
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
                //// Write idx Header
                //HelperTools.Long2Bytes(idxBuffer, 0, HelperTools.millisFromEpochNow());
                //HelperTools.Int32Bytes(idxBuffer, 8, secsIdxInterval);
                //idxFile.Write(idxBuffer, 0, 12);
            }

            correctedTime = time - timeOffset;

            // Fill header
            FillHeader(correctedTime, ID, rawDataSize, headerBuffer, ipChunks, port);
            // Save to file
            file.Write(headerBuffer, 0, HelperTools.headerSize);
            file.Write(buff, rawDataOffset, rawDataSize);

            // Dump if requested
            if (udpListeners[ID].DumpToFile && udpListeners[ID].file != null)
                udpListeners[ID].file.Write(buff, rawDataOffset, rawDataSize);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void WriteIdx()
        {
            // Write index
            HelperTools.Int32Bytes(idxBuffer, 0, secTime);
            HelperTools.Long2Bytes(idxBuffer, 4, file.Position);

            idxFile.Write(idxBuffer, 0, HelperTools.idxIndexSize);
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

        public void SetRecordingFolder(string _folder, string _name)
        {
            if (CurrentState != State.Recording)
            {
                if (!Directory.Exists(_folder))
                    Directory.CreateDirectory(_folder);

                name = _name;
                folder = _folder;
                filePath = Path.Combine(_folder , $"{_name}.raw");
                idxFilePath = Path.Combine(_folder, $"{_name}.idx");
            }
        }

        //public void SetFilePath(string _filePath)
        //{
        //    if (CurrentState != State.Recording)
        //    {
        //        string folder = Path.GetDirectoryName(_filePath);

        //        filePath = _filePath+".raw";
        //        idxFilePath = Path.Combine(folder, Path.GetFileNameWithoutExtension(filePath) + ".idx");
        //    }
        //}

        public void Start(int _secsIdxInterval)
        {
            secsIdxInterval = _secsIdxInterval;
            timeOffset = long.MinValue;
            secTime = 0;

            // Open file
            file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            idxFile = new FileStream(idxFilePath, FileMode.Create, FileAccess.Write,FileShare.None);

            // Open file per Peer with Dump2File
            foreach(KeyValuePair<string, RecPeerInfo> kv in udpListeners)
            {
                if (kv.Value.DumpToFile)
                    kv.Value.file = new FileStream(Path.Combine(folder, $"{name}_{kv.Key}{kv.Value.DumpFileExtension}"),FileMode.Create, FileAccess.Write, FileShare.None);
            }

            // Fill IDX File header
            FillIDXHeader(idxFile);

            // Start Timer
            eventTimer = new Timer(OnEventTimer, null, 0, 1000);

            foreach (var u in udpListeners.Values)
                u.commsLink.Start();

            CurrentState = State.Recording;
            StatusEvent?.Invoke(CurrentState);
        }

        public async Task Stop()
        {
            List<Task> tasks = new List<Task>();
            foreach (var u in udpListeners.Values)
                tasks.Add(u.commsLink.Stop());

            await Task.WhenAll(tasks);

            eventTimer.Dispose();
            eventTimer = null;

            foreach (KeyValuePair<string, RecPeerInfo> kv in udpListeners)
            {
                if (kv.Value.DumpToFile)
                    kv.Value.file.Dispose();
            }

            file.Close();
            file.Dispose();
            idxFile.Close();
            idxFile.Dispose();

            CurrentState = State.Stoped;
            StatusEvent?.Invoke(CurrentState);
        }

        private void FillIDXHeader(FileStream _idxFile)
        {
            byte[] idxHeader = new byte[4096];
            int i = 0;
            // Write idx Header
            // Time of recording
            i += HelperTools.Long2Bytes(idxHeader, i, HelperTools.millisFromEpochNow());
            // Interval
            i += HelperTools.Int32Bytes(idxHeader, i, secsIdxInterval);
            // Number of peers
            i += HelperTools.Int32Bytes(idxHeader, i, udpListeners.Count);
            foreach (RecPeerInfo p in udpListeners.Values)
            {
                i += HelperTools.StringWithLength2Bytes(idxHeader, i, p.ID);
                i += HelperTools.StringWithLength2Bytes(idxHeader, i, p.IP);
                i += HelperTools.Int32Bytes(idxHeader, i, p.Port);
            }

            idxFile.Write(idxHeader, 0, i);
        }

        public void Dispose()
        {
        }
    }
}
