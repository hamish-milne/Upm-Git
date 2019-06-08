using System.Reflection;
using System.Linq;
using System.Threading;
using System;
using System.IO;
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
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.FileProviders.Embedded;
using System.Text;

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
                    x.FileProvider = new AOTFriendlyEmbeddedFileProvider(typeof(Program).Assembly);
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

                httpServer.Use(new Controller());
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

	public class AOTFriendlyEmbeddedFileProvider : IFileProvider
	{
        public AOTFriendlyEmbeddedFileProvider(Assembly assembly)
        {
            _assembly = assembly;
            _baseNamespace = assembly.GetName().Name;
            _surrogate = new EmbeddedFileProvider(assembly, _baseNamespace);
            _baseNamespace += ".";
        }
        private readonly Assembly _assembly;
        private readonly string _baseNamespace;
        private readonly EmbeddedFileProvider _surrogate;
        private readonly DateTime _lastModified = DateTime.UtcNow;
		public IDirectoryContents GetDirectoryContents(string subpath)
            => _surrogate.GetDirectoryContents(subpath);

		public IFileInfo GetFileInfo(string subpath)
		{
            if (string.IsNullOrEmpty(subpath))
            {
                return new NotFoundFileInfo(subpath);
            }

            var builder = new StringBuilder(_baseNamespace.Length + subpath.Length);
            builder.Append(_baseNamespace);

            // Relative paths starting with a leading slash okay
            if (subpath.StartsWith("/", StringComparison.Ordinal))
            {
                builder.Append(subpath, 1, subpath.Length - 1);
            }
            else
            {
                builder.Append(subpath);
            }

            for (var i = _baseNamespace.Length; i < builder.Length; i++)
            {
                if (builder[i] == '/' || builder[i] == '\\')
                {
                    builder[i] = '.';
                }
            }

            var resourcePath = builder.ToString();

            var name = Path.GetFileName(subpath);
            if (!_assembly.GetManifestResourceNames().Contains(resourcePath))
            {
                return new NotFoundFileInfo(name);
            }
            return new EmbeddedResourceFileInfo(_assembly, resourcePath, name, _lastModified);
		}

		public IChangeToken Watch(string filter)
            => _surrogate.Watch(filter);
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
