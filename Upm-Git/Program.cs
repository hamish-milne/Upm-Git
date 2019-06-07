using System.Threading;
using System;
using System.Net;
using System.Net.Sockets;

using uhttpsharp;
using uhttpsharp.Logging;
using uhttpsharp.Listeners;
using uhttpsharp.RequestProviders;

using Serilog;
using Serilog.Events;
using Log = Serilog.Log;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;

namespace UpmGit
{
	class Program
    {
        static void WaitForExit()
        {
            var waitEvent = new ManualResetEvent(false);
            ConsoleCancelEventHandler handler = (sender, e) => waitEvent.Set();
            Console.CancelKeyPress += handler;
            waitEvent.WaitOne();
            Console.CancelKeyPress -= handler;
        }

        static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(x => 
                {
                    x.Path = "appsettings.json";
                    x.FileProvider = new EmbeddedFileProvider(typeof(Program).Assembly);
                })
                .AddJsonFile("appsettings.json", true)
                .Build();
                
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();
            LogProvider.SetCurrentLogProvider(new SerilogProvider());

            using (var httpServer = new HttpServer(new HttpRequestProvider()))
            {
                var endpoint = new IPEndPoint(
                    IPAddress.Parse(config.GetValue<string>("listenAddress")),
                    config.GetValue<int>("port")
                );
                Log.Information("Listening on {endpoint}", endpoint);
                
                httpServer.Use(new TcpListenerAdapter(new TcpListener(endpoint)));

                // TODO: SSL support

                httpServer.Use(new Controller());
                httpServer.Start();
                WaitForExit();
            }
        }
    }

    public class SerilogProvider : ILogProvider
    {
		public ILog GetLogger(string name) => new SerilogLogger();

		class SerilogLogger : ILog
		{
			public bool Log(LogLevel logLevel, Func<string> messageFunc,
                Exception exception = null, params object[] formatParameters)
			{
                if (!Serilog.Log.IsEnabled((LogEventLevel)logLevel)) {
                    return false;
                }
                if (messageFunc != null || exception != null)
                    Serilog.Log.Write((LogEventLevel)logLevel, exception,
                        messageFunc?.Invoke() ?? "", formatParameters);
                return true;
			}
		}
	}
}
