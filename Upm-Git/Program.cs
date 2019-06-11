using System.Linq;
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
using System.Threading.Tasks;

namespace UpmGit
{
	class Program
    {
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
                .WriteTo.Console()
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

                httpServer.Use(new Controller(config));
                httpServer.Use(new ErrorHandler());
                httpServer.Start();
                if (!args.Contains("--test"))
                    WaitForExit();
                Log.Information("Exiting gracefully");
            }
        }

        // Waits for CTRL+C/SIGTERM before returning, allowing the program to dispose of resources
        static void WaitForExit()
        {
            using (var waitEvent = new ManualResetEvent(false))
            {
                ConsoleCancelEventHandler handler = (sender, e) =>
                {
                    e.Cancel = true;
                    waitEvent.Set();
                };
                Console.CancelKeyPress += handler;
                waitEvent.WaitOne();
                Console.CancelKeyPress -= handler;
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

	public class ErrorHandler : IHttpRequestHandler
	{
		public Task Handle(IHttpContext context, Func<Task> next)
		{
            Log.Information("Not found: {0}", context.Request.Uri);
			context.Response = new HttpResponse(HttpResponseCode.NotFound, "", true);
            return Task.CompletedTask;
		}
	}
}
