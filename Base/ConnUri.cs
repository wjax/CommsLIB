namespace CommsLIB.Base
{
    // tcp://IP:PORT
    // udp://IP:PORT:BIND_IP:BIND_PORT
    // udp://:PORT:LOCAL_PORT
    // serial://com1:9600
    // sdp://yua_Live.sdp::50100
    public class ConnUri
    {
        public enum TYPE
        {
            UNKNOWN,
            UDP,
            TCP,
            SERIAL,
            SDP
        }

        public TYPE UriType;
        
        private bool isValid;
        public bool IsValid
        {
            get => isValid;
        }

        private string _uriPath;
        public string UriPath
        {
            get => _uriPath;
            set
            {
                _uriPath = value;
                if (string.IsNullOrEmpty(value))
                {
                    isValid = false;
                }
                else
                {
                    int slashIndex = -1;
                    
                    if ((slashIndex = value.IndexOf('/')) > -1 && value.LastIndexOf('/') > slashIndex)
                    {

                        string type = value.Substring(0, slashIndex - 1);
                        string data = value.Substring(slashIndex + 2);
                        string[] splitted = data.Split(':');
                        switch (type)
                        {
                            case "tcp":
                                if (splitted.Length == 2)
                                {
                                    _ip = splitted[0];
                                    if (!int.TryParse(splitted[1], out _port))
                                    {
                                        isValid = false;
                                    }
                                    isValid = true;
                                    _path = "tcp://" + _ip + ":" + _port;
                                    UriType = TYPE.TCP;
                                }
                                else
                                    isValid = false;
                                break;
                            case "udp":
                                if (splitted.Length >= 2)
                                {
                                    _ip = splitted[0].Trim('@');
                                    if (!int.TryParse(splitted[1], out _port))
                                    {
                                        isValid = false;
                                    }
                                    if (splitted.Length >= 3)
                                    {
                                        _bindIP = splitted[2];
                                    }
                                    if (splitted.Length >= 4)
                                    {
                                        if (!int.TryParse(splitted[3], out _localPort))
                                            isValid = false;
                                    }
                                    _path = "udp://" + _ip + ":" + _port;
                                    UriType = TYPE.UDP;
                                    isValid = true;
                                }
                                else
                                    isValid = false;
                                break;
                            case "serial":
                                if (splitted.Length == 2)
                                {
                                    _serialPort = splitted[0];
                                    if (!int.TryParse(splitted[1], out _serialBPS))
                                    {
                                        isValid = false;
                                    }
                                    isValid = true;
                                    UriType = TYPE.SERIAL;
                                }
                                break;
                            case "sdp":
                                if (splitted.Length==4)
                                {
                                    _path = splitted[0];
                                    _ip = splitted[1];
                                    if (!int.TryParse(splitted[2], out _port))
                                    {
                                        isValid = false;
                                    }
                                    isValid = true;
                                    _bindIP = splitted[3];
                                    UriType = TYPE.SDP;
                                }
                                break;
                            default:
                                UriType = TYPE.UNKNOWN;
                                break;
                        }
                    }
                    
                }
            }
        }

        private string _ip;
        public string IP
        {
            get => _ip;
        }


        private string _bindIP;
        public string BindIP
        {
            get => _bindIP;
        }


        private string _serialPort;
        public string SerialPort
        {
            get => _serialPort;
        }

        private int _port;
        public int Port
        {
            get => _port;
        }

        private int _serialBPS;
        public int SerialBPS
        {
            get => _serialBPS;
        }

        private int _localPort;
        public int LocalPort
        {
            get => _localPort;
        }

        private string _path;
        public string Path
        {
            get => _path;
        }

        public ConnUri(string uriString)
        {
            UriPath = uriString;
        }
    }
}
