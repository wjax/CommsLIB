using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CommsLIB.Logging
{
    public sealed class LoggingManager
    {
        internal static ILoggerFactory LoggerFactory { get; private set; }

        public static void SetLoggingFactory(ILoggerFactory loggerFactory) => LoggerFactory = loggerFactory;

        public static ILogger<T> CreateLoggerSafe<T>()
        {
            if (LoggerFactory != null)
            {
                lock (LoggerFactory)
                    return LoggerFactory.CreateLogger<T>();
            }

            return null;
        }
    }
}
