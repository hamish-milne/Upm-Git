using uhttpsharp;
using uhttpsharp.Headers;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UpmGit
{
	public class ControllerBase : IHttpRequestHandler
	{
		public delegate Task<IHttpResponse> RouteHandler(Dictionary<string, string> properties, IHttpContext context);
		public void Get(string match, RouteHandler handler)
		{
			routes.Add((match.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => new UrlSegmentMatcher(s)).ToArray(), handler));
		}

		public class UrlSegmentMatcher
		{
			private static Regex converter = new Regex(@"\\{(\w*)}");
			private readonly List<string> propertyNames = new List<string>();
			private readonly Regex regex;

			public UrlSegmentMatcher(string segment)
			{
				var escaped = Regex.Escape(segment);
				regex = new Regex("^" + converter.Replace(Regex.Escape(segment), match =>
				{
					propertyNames.Add(match.Groups[1].Value);
					return "(.+)";
				}) + "$");
			}

			public bool Match(string input, Dictionary<string, string> properties)
			{
				var match = regex.Match(input);
				if (match.Success)
				{
					for (int i = 0; i < propertyNames.Count; i++)
					{
						properties[propertyNames[i]] = match.Groups[i+1].Value;
					}
					return true;
				}
				return false;
			}
		}

		private readonly List<(UrlSegmentMatcher[], RouteHandler)> routes = new List<(UrlSegmentMatcher[], RouteHandler)>();

		private class JsonHttpResponse : IHttpResponse
		{
			public HttpResponseCode ResponseCode => HttpResponseCode.Ok;

			public HttpHeaders Headers { get; } = new HttpHeaders(
				new Dictionary<string, string>{
					{"Content-Type", "application/json"}
				}
			);

			IHttpHeaders IHttpResponse.Headers => Headers;

			public bool CloseConnection => true;

			public JToken Json { get; set; }

			public async Task WriteBody(StreamWriter writer)
			{
				var jwriter = new JsonTextWriter(writer);
				await Json.WriteToAsync(jwriter);
				await jwriter.FlushAsync();
			}
		}

		public static IHttpResponse JsonResponse(JToken json)
		{
			return new JsonHttpResponse{Json = json};
		}

		public async Task Handle(IHttpContext context, Func<Task> next)
		{
			foreach (var route in routes)
			{
				var inSegments = context.Request.Uri.OriginalString.Split('/', StringSplitOptions.RemoveEmptyEntries);
				var outSegments = route.Item1;
				if (inSegments.Length != outSegments.Length) {
					continue;
				}
				var properties = new Dictionary<string, string>();
				var success = true;
				for (int i = 0; i < inSegments.Length; i++)
				{
					if (!outSegments[i].Match(Uri.UnescapeDataString(inSegments[i]), properties)) {
						success = false;
						break;
					}
				}
				if (success) {
					context.Response = await route.Item2(properties, context);
					return;
				}
			}
			await next();
		}

		public static IHttpResponse Ok { get; } = new HttpResponse(HttpResponseCode.Ok, "", true); 
		public static IHttpResponse NotFound { get; } = new HttpResponse(HttpResponseCode.NotFound, "", true); 
	}
}