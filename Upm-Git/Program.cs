using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Net;
using System.Net.Sockets;

using uhttpsharp;
using uhttpsharp.Headers;
using uhttpsharp.Logging;
using uhttpsharp.Listeners;
using uhttpsharp.RequestProviders;
using Nancy;
using Nancy.Extensions;

using Serilog;
using Serilog.Events;

namespace PackageSrv
{
	public class MyAssemblyCatalog : IAssemblyCatalog
	{
		public IReadOnlyCollection<Assembly> GetAssemblies()
		{
			return new []{typeof(DefaultNancyBootstrapper).Assembly, typeof(PackageSrv).Assembly};
		}
	}

	public class MyBootstrapper : DefaultNancyBootstrapper
    {
        private readonly IAssemblyCatalog assemblyCatalog = new MyAssemblyCatalog();
        protected override IAssemblyCatalog AssemblyCatalog => assemblyCatalog;
    }


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
            Nancy.Bootstrapper.NancyBootstrapperLocator.Bootstrapper = new MyBootstrapper();

            using (var httpServer = new HttpServer(new HttpRequestProvider()))
            {
                httpServer.Use(new TcpListenerAdapter(new TcpListener(IPAddress.Loopback, 1234)));

                // TODO: SSL support

                httpServer.Use(new NancyRequestHandler());
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

	class NancyRequestHandler : IHttpRequestHandler
	{
        private readonly INancyEngine _engine;

        public NancyRequestHandler()
        {
            var bootstrapper = Nancy.Bootstrapper.NancyBootstrapperLocator.Bootstrapper;
            bootstrapper.Initialise();
            _engine = bootstrapper.GetEngine();
        }

		public async Task Handle(IHttpContext context, Func<Task> next)
		{
            try
            {
                using (var nancyContext = await _engine.HandleRequest(ConvertToNancyRequest(context)))
                {
                    WriteResponse(nancyContext.Response, context);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error processing request");
            }
            await Task.Factory.GetCompleted();
		}

        public Request ConvertToNancyRequest(IHttpContext context)
        {
            // TODO: Can we get this from the context somehow?
            var baseUri = new Uri("http://localhost:1234");
            var request = new Request(
                context.Request.Method.ToString(),
                new Uri(baseUri, context.Request.Uri),
                new MemoryStream(context.Request.Post.Raw),
                context.Request.Headers.ToDictionary(p => p.Key, p => (IEnumerable<string>)new []{p.Value}),
                (context.RemoteEndPoint as IPEndPoint)?.Address.ToString(),
                certificate: null,
                context.Request.Protocol);
            if (context.Request.QueryString.Count() > 0) {
                request.Query = context.Request.QueryString.ToUriData().AsQueryDictionary();
            }
            return request;
        }

        public void WriteResponse(Response response, IHttpContext context)
        {
            // TODO: Chunked transfer encoding?
            var buffer = new MemoryStream();
            response.Contents(buffer);
            context.Response = new HttpResponse(
                (HttpResponseCode)response.StatusCode,
                response.ContentType,
                buffer,
                keepAliveConnection: true,
                response.Headers
            );
        }
	}
}
