using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Extensions.Logging;
using Serilog.Extensions.Hosting;
using Serilog.AspNetCore;
using Serilog;

namespace MemoriaNote
{
    /// <summary>
    /// This class represents a logging utility for the application.
    /// </summary>
    public class Log //: IDisposable
    {
        static Serilog.ILogger _logger = null;
        
        /// <summary>
        /// Property that provides access to the logger instance. If the logger instance is null, it is created with specified configurations and file destination.
        /// </summary>
        public static Serilog.ILogger Logger        
        {
            get {
                if (_logger == null) {
                    _logger = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
                        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                        .MinimumLevel.Override("MemoriaNote", Serilog.Events.LogEventLevel.Debug)
                        .Enrich.FromLogContext()
                        .WriteTo.File(Scratchpad.Singleton.GetFile("Log-" + DateTime.Now.ToString("yyyyMMdd") + ".txt", false))
                        .CreateLogger();
                }
                return _logger;
            }
        }
    }

    /// <summary>
    /// Define an enumeration type LoggerType with three possible values: None, File, and Console
    /// </summary>
    public enum LoggerType
    {
        None,
        File,
        Console
    }
}