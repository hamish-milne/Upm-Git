using uhttpsharp;
using uhttpsharp.Headers;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UpmGit
{
	public class Controller : ControllerBase
	{
		public Controller()
		{
			Get("/{remote}", async (_, __) => Ok);
			Get("/{remote}/-/all", ListAll);
			Get("/{remote}/{package}", GetPackage);
			Get("/{remote}/{package}/{version}", GetPackageVersion);
			Get("/{remote}/-/v1/search", Search);
			Get("/{remote}/{package}/{version}/download.{format}", Download);
		}

		string GetRemote(Dictionary<string, string> props)
		{
			// TODO: Add single remote config
			return props["remote"];
		}

		async Task<IHttpResponse> ListAll(Dictionary<string, string> query, IHttpContext __)
		{
			UpdatePackages(query);
			var packageInfos = new JObject
			{
				["_updated"] = 99999, // TODO: Why this number?
			};
			foreach (var package in Packages.GroupBy(p => p.Manifest["name"]).Select(g => 
			{
				var orderedVersions = g.OrderByDescending(p => p.Manifest["version"].ToString()).ToArray();
				var latest = orderedVersions.First().Manifest;
				return new JObject
				{
					["name"] = latest["name"],
					["description"] = latest["description"],
					["maintainers"] = latest["maintainers"] ?? new JArray(),
					["versions"] = new JObject {
						[latest["version"].ToString()] = "latest"
					},
					["time"] = null,
					["keywords"] = latest["keywords"] ?? new JArray(),
					["author"] = latest["author"]
				};
			})) {
				packageInfos[package["name"].ToString()] = package;
			}
			return JsonResponse(packageInfos);
		}

		async Task<IHttpResponse> GetPackage(Dictionary<string, string> query, IHttpContext __)
		{
			UpdatePackages(query);
			var versionList = Packages
				.Where(p => p.Manifest["name"].ToString() == query["package"])
				.OrderByDescending(p => p.Manifest["version"].ToString())
				.ToArray();
			if (versionList.Length == 0) { return NotFound; }
			var latest = versionList.First();
			var versions = new JObject();
			var times = new JObject
			{
				["modified"] = DateTime.UtcNow,
				["created"] = DateTime.UtcNow
			};
			foreach (var version in versionList)
			{
				versions[version.Manifest["version"].ToString()] = new JObject
				{
					["name"] = version.Manifest["name"],
					["description"] = version.Manifest["description"],
					["version"] = version.Manifest["version"],
					["dist"] = await GetDist(query, version.GitRef, version.Path, version.Manifest),
					["dependencies"] = version.Manifest["dependencies"] ?? new JObject(),
					["_id"] = $"{version.Manifest["name"]}@{version.Manifest["version"]}",
					["gitHead"] = version.GitRef,
					["unity"] = version.Manifest["unity"],
					["displayName"] = version.Manifest["displayName"],
					["repoPackagePath"] = version.Path
				};
				times[version.Manifest["version"].ToString()] = DateTime.UtcNow;
			}
			return JsonResponse(new JObject
			{
				["_id"] = query["package"],
				["_rev"] = "1-0", // TODO: What's this value?
				["name"] = query["package"],
				["description"] = latest.Manifest["description"],
				["dist-tags"] = new JObject
				{
					["latest"] = latest.Manifest["version"]
				},
				["versions"] = versions,
				["repository"] = new JObject
				{
					["revision"] = latest.GitRef,
					["type"] = "git",
					["url"] = GetRemote(query)
				},
				["time"] = times
			});
		}

		async Task<IHttpResponse> GetPackageVersion(Dictionary<string, string> query, IHttpContext __)
		{
			UpdatePackages(query);
			var (GitRef, Path, Manifest) = Packages.FirstOrDefault(p =>
				(string)p.Manifest["name"] == query["package"] &&
				(string)p.Manifest["version"] == query["version"]);
			if (Manifest == null) {
				return NotFound;
			}
			return JsonResponse(new JObject(Manifest)
			{
				["repository"] = new JObject
				{
					["revision"] = GitRef,
					["type"] = "git",
					["url"] = GetRemote(query)
				}
				["dist"] = await GetDist(query, GitRef, Path, Manifest),
				["_id"] = $"{query["package"]}@{query["version"]}"
			});
		}

		async Task<IHttpResponse> Search(Dictionary<string, string> query, IHttpContext context)
		{
			UpdatePackages(query);
			var packages = Packages.Select(p => p.Manifest);
			if (context.Request.QueryString.TryGetByName("text", out var text)) {
				bool Match(JToken token) => token != null && token.ToString().Contains(text);
				packages = packages.Where(m => Match(m["name"]) || Match(m["displayName"]) || Match(m["description"]) || Match(m["keywords"]));
			}
			if (context.Request.QueryString.TryGetByName("size", out var size)) {
				packages = packages.Take(int.Parse(size));
			}
			return JsonResponse(new JObject
			{
				["objects"] = new JArray(packages.Select(m =>
					new JObject
					{
						["package"] = new JObject
						{
							["name"] = m["name"],
							["description"] = m["description"],
							["maintainers"] = m["maintainers"] ?? new JArray(),
							["version"] = m["version"],
							["date"] = m["date"],
							["keywords"] = m["keywords"] ?? new JArray(),
							["author"] = m["author"]
						}
					})
				)
			});
		}

		class DownloadResponse : IHttpResponse
		{
			public DownloadResponse(string format)
			{
				Format = format;
				Headers = new HttpHeaders(
					new Dictionary<string, string>{
						{"Content-Type", "application/json"}
					}
				);
			}
			public string GitRef { get; set; }
			public string Path { get; set; }
			public string Format { get; }
			public string Remote { get; set; }
			public HttpResponseCode ResponseCode => HttpResponseCode.Ok;
			public bool CloseConnection => true;
			public IHttpHeaders Headers { get; }

			public async Task WriteBody(StreamWriter writer)
			{
				await GitClient.GetArchive(stream => stream.CopyToAsync(writer.BaseStream),
					Remote, GitRef, Path, Format);
			}
		}

		async Task<IHttpResponse> Download(Dictionary<string, string> query, IHttpContext __)
		{
			UpdatePackages(query);
			// Locate the package
			var (GitRef, Path, Manifest) = Packages.FirstOrDefault(p =>
				(string)p.Manifest["name"] == query["package"] &&
				(string)p.Manifest["version"] == query["version"]);
			if (Manifest == null) {
				return NotFound;
			}
			return new DownloadResponse(query["format"])
			{
				GitRef = GitRef,
				Path = Path,
				Remote = GetRemote(query)
			};
		}

		private static (string GitRef, string Path, JObject Manifest)[] Packages;

		[MethodImpl(MethodImplOptions.Synchronized)]
		private void UpdatePackages(Dictionary<string, string> query)
		{
			if (Packages == null) {
				Packages = GitClient.ListPackages(GetRemote(query), "*/package.json", r => r.Contains("package")).ToArray();
			}
		}

		private static readonly Dictionary<(string gitRef, string path), string> _hashes
			= new Dictionary<(string gitRef, string path), string>();

		private async Task<JObject> GetDist(Dictionary<string, string> props, string gitRef, string directory, JObject manifest)
		{
			string hash;
			lock (_hashes)
				_hashes.TryGetValue((gitRef, directory), out hash);
			const string format = "tgz";
			if (hash == null)
			{
				byte[] hashOut = null;
				await GitClient.GetArchive(
					async input => hashOut = SHA1.Create().ComputeHash(input),
					GetRemote(props), gitRef, directory, format);
				var sb = new StringBuilder();
				foreach (var b in hashOut) {
					sb.Append(b.ToString("x2"));
				}
				hash = sb.ToString();
				lock (_hashes)
					_hashes[(gitRef, directory)] = hash;
			}
			return new JObject
			{
				["tarball"] = $"/{manifest["name"]}/{manifest["version"]}/download.tgz",
				["shasum"] = hash
			};
		}
	}
}