using CommsLIB.Communications;
using CommsLIB.SmartPcap.Base;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
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

        #region member
        private string filePath = @".\dumptest.mpg";
        private string idxFilePath = @".\dumptest.idx";
        private FileStream file;
        private FileStream idxFile;
        private byte[] idxBuffer;
        private byte[] headerBuffer;

        private long timeOffset = long.MinValue;
        private long lastTimeIdx = long.MinValue;

        long correctedTime = 0;
        int lastIdxTime = -1;

        private int secsIdxInterval;

        public delegate void StatusDelegate(State state);
        public event StatusDelegate StatusEvent;
        public delegate void ProgressDelegate(float mb, int secs);
        public event ProgressDelegate ProgressEvent;

        private List<UDPListener> udpListeners = new List<UDPListener>();

        public State CurrentState { get; set; }

        #endregion

        public NetRecorder()
        {
            idxBuffer = HelperTools.RentBuffer(HelperTools.idxDataSize);
            headerBuffer = HelperTools.RentBuffer(HelperTools.headerSize);
        }

        public void AddPeer(string ID, string ip, int port, string netcard = "")
        {
            // Check valid IP
            if (IPAddress.TryParse(ip, out IPAddress ipAddres))
            {
                var u = new UDPListener(ID, ip, port, netcard);
                u.DataReadyEvent += DataReadyEventCallback;
                udpListeners.Add(u);
            }
        }

        public void AddPeer<T>(CommunicatorBase<T> commLink)
        {
            commLink.DataReadyEvent += DataReadyEventCallback;
        }

        public void RemovePeer<T>(CommunicatorBase<T> commLink)
        {
            try
            {
                commLink.DataReadyEvent -= DataReadyEventCallback;
            }
            catch {
                
            }
        }

        public void RemoveAllPeers()
        {

        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void DataReadyEventCallback(string ip, int port, long time, byte[] buff, int rawDataOffset, int rawDataSize, string ID, ushort[] ipChunks)
        {
            if (CurrentState != State.Recording)
                return;

            if (timeOffset == long.MinValue)
            {
                timeOffset = time;
                correctedTime = 0;
                // Write idx Header
                HelperTools.Long2Bytes(idxBuffer, 0, HelperTools.millisFromEpochNow());
                HelperTools.Int32Bytes(idxBuffer, 8, secsIdxInterval);
                idxFile.Write(idxBuffer, 0, 12);
            }
            else
                correctedTime = time - timeOffset;

            // Time in seconds
            int secTime = (int)(correctedTime / 1000000);

            // 1sec
            if (secTime % secsIdxInterval == 0)
            {
                if (lastIdxTime != secTime)
                {
                    // Write index
                    HelperTools.Int32Bytes(idxBuffer, 0, secTime);
                    HelperTools.Long2Bytes(idxBuffer, 4, file.Position);

                    idxFile.Write(idxBuffer, 0, HelperTools.idxDataSize);

                    // Launch event
                    ProgressEvent?.Invoke((float)(file.Position / 1000000.0), secTime);

                    lastIdxTime = secTime;
                }
            }

            // Fill header
            FillHeader(correctedTime, ID, rawDataSize, headerBuffer, ipChunks, port);
            // Save to file
            file.Write(headerBuffer, 0, HelperTools.headerSize);
            file.Write(buff, rawDataOffset, rawDataSize);
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

        public void setFilePath(string _filePath)
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

            // Open file
            file = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            idxFile = new FileStream(idxFilePath, FileMode.Create, FileAccess.Write);

            lastIdxTime = -1;

            foreach (var u in udpListeners)
                u.Start();

            CurrentState = State.Recording;
            StatusEvent?.Invoke(CurrentState);
        }

        public void Stop()
        {
            List<Task> tasks = new List<Task>();
            foreach (var u in udpListeners)
                tasks.Add(u.Stop());

            Task.WaitAll(tasks.ToArray());

            //foreach (var u in udpListeners)
            //    u.Dispose();

            file.Close();
            file.Dispose();
            idxFile.Close();
            idxFile.Dispose();

            //udpListeners.Clear();

            CurrentState = State.Stoped;
            timeOffset = long.MinValue;

            StatusEvent?.Invoke(CurrentState);
        }

        public void Dispose()
        {
            HelperTools.ReturnBuffer(idxBuffer);
        }
    }
}
