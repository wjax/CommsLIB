using CommsLIB.Base;
using CommsLIB.Helper;
using System;



namespace CommsLIB.Communications
{
    public sealed class CommEquipmentObject<T>
    {
        public T ClientImpl;
        public ConnUri ConnUri;
        public string ID;
        public long timeLastIncoming;
        private bool persitentConnection;
        public bool Connected { get; set; }

        public CommEquipmentObject(string ID, ConnUri uri, T _ClientImpl, bool _persistent = false)
        {
            this.ID = ID;
            this.ConnUri = uri;
            this.ClientImpl = _ClientImpl;
            this.timeLastIncoming = TimeTools.GetCoarseMillisNow();
            this.persitentConnection = _persistent;
        }

        public bool IsPersistent { get => persitentConnection; }
    }
}
