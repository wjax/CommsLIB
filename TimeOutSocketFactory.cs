using CommsLIB.Helper;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CommsLIB.Communications
{
    public static class TimeOutSocketFactory
    {
        public static TcpClient Connect(IPEndPoint remoteEndPoint, int timeoutMSec)
        {
            TcpClient tcpclient = new TcpClient();
            int startTime = TimeTools.GetCoarseMillisNow();

            try
            {
                IAsyncResult asyncResult = tcpclient.BeginConnect(remoteEndPoint.Address, remoteEndPoint.Port, null, null);

                if (asyncResult.AsyncWaitHandle.WaitOne(timeoutMSec, false))
                {
                    try
                    {
                        tcpclient.EndConnect(asyncResult);
                        return tcpclient;
                    }
                    catch
                    {
                        // See if we have to wait a little bit
                        int wait = timeoutMSec - (TimeTools.GetCoarseMillisNow() - startTime);
                        if (wait > 10)
                            Thread.Sleep(wait);

                        tcpclient?.Dispose();
                        return null;
                    }
                }
                else
                {
                    // See if we have to wait a little bit
                    int wait = timeoutMSec - (TimeTools.GetCoarseMillisNow() - startTime);
                    if (wait > 10)
                        Thread.Sleep(wait);
                    try
                    {
                        tcpclient?.Dispose();
                        return null;
                    }
                    catch { }

                    return null;
                    
                }
            } catch (Exception e)
            {
                // See if we have to wait a little bit
                int wait = timeoutMSec - (TimeTools.GetCoarseMillisNow() - startTime);
                if (wait > 10)
                    Thread.Sleep(wait);
                try
                {
                    tcpclient?.Dispose();
                }
                catch { }

                return null;
            }
            
        }
    }
}
