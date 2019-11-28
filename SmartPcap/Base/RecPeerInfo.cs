using CommsLIB.Communications;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CommsLIB.SmartPcap.Base
{
    internal class RecPeerInfo
    {
        public string ID;
        public float DataRate;
        public bool DumpToFile;

        public string IP;
        public int Port;

        public ICommunicator commsLink;
        public FileStream file;
    }
}
