using CommsLIB.Communications;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace CommsLIB.Logging
{
    public static class ExtensionLogger
    {
        public static ILogger<T> GetLogger<T>(this T obj) where T : class
        {
            return LoggingManager.CreateLoggerSafe<T>();
        }
    }
}
