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

namespace UpmGit
{
	class Program
    {
        static Program()
        {
            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console()
                .CreateLogger();
            LogProvider.SetCurrentLogProvider(new SerilogProvider());
        }

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
            using (var httpServer = new HttpServer(new HttpRequestProvider()))
            {
                httpServer.Use(new TcpListenerAdapter(new TcpListener(IPAddress.Loopback, 1234)));

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
