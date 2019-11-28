using System;
using System.Collections.Generic;
using System.Text;

namespace CommsLIB.SmartPcap.Base
{
    public class PlayPeerInfo
    {
        public string ID { get; set; }
        public float DataRate;

        public string IP;
        public int Port;
        public string NIC;

        public bool IsEnabled = true;

        public UDPSender commsLink;
    }
}
