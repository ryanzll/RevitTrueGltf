using System;
using System.IO;
using Serilog;

namespace RevitTrueGltf.Utils
{
    public static class LoggingHelper
    {
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        public static void Initialize()
        {
            if (_isInitialized) return;

            lock (_lock)
            {
                if (_isInitialized) return;

                // 2. Place logs in LocalApplicationData to avoid permission issues in restricted directories
                string logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitTrueGltf", "Logs");
                string logFile = Path.Combine(logFolder, "log-.txt");

                var config = new LoggerConfiguration()
                    // 1. Use local file logging with 10MB limit and retain last 7 daily files
                    .WriteTo.File(
                        path: logFile,
                        rollingInterval: RollingInterval.Day,
                        fileSizeLimitBytes: 10 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: 7
                    );

                // 4. Adjust logging level: Debug for dev builds, Information for release
#if DEBUG
                config.MinimumLevel.Debug();
#else
                config.MinimumLevel.Information();
#endif

                Log.Logger = config.CreateLogger();
                _isInitialized = true;
                
                Log.Debug("Serilog initialized successfully.");
            }
        }
    }
}
