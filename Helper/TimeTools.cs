using System;

namespace CommsLIB.Helper
{
    public class TimeTools
    {
        // Ojo. Reset every 24 days. 0 --> Max --> Min
        public static int GetCoarseMillisNow()
        {
            return Environment.TickCount & Int32.MaxValue;
        }
    }
}
