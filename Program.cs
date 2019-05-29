using System.Reflection;
using System.Dynamic;
using System.Diagnostics;
using System.Net;
using System;
using Nancy;
using Nancy.Extensions;
using uhttpsharp;
using uhttpsharp.RequestProviders;
using uhttpsharp.Listeners;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using uhttpsharp.Headers;
//using uhttpsharp.Logging;
using System.Collections;

// Temp
using Microsoft.Extensions.DependencyModel;

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
            // LogProvider.LogProviderResolvers.Clear();
            // LogProvider.LogProviderResolvers.Add(
            //     new Tuple<LogProvider.IsLoggerAvailable, LogProvider.CreateLogProvider>(() => true,
            //         () => NullLoggerProvider.Instance));
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
                Console.ReadLine();
            }
        }
    }

    // public class NullLoggerProvider : ILogProvider
    // {
    //     public static readonly NullLoggerProvider Instance = new NullLoggerProvider();
        
    //     private static readonly ILog NullLogInstance = new NullLog();

	// 	public ILog GetLogger(string name) => NullLogInstance;

	// 	public IDisposable OpenNestedContext(string message) => null;

	// 	public IDisposable OpenMappedContext(string key, string value) => null;

	// 	public class NullLog : ILog
    //     {
    //         public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception = null, params object[] formatParameters)
    //         {
    //             if (messageFunc != null) {
    //                 Console.WriteLine(string.Format(messageFunc(), formatParameters));
    //             }
    //             if (exception != null) {
    //                 Console.WriteLine(exception);
    //             }
    //             return true;
    //         }
    //     }
    // }

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
                Debug.Write($"---\n{e}\n---\n");
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
