using System.Text;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

namespace PackageSrv
{
	using Nancy;

	public class PackageSrv : NancyModule
	{
		public static string Remote = "";

		public PackageSrv()
		{
			Get("/", _ => 
			{
				// TODO: Put some nice HTML here?
				return 200;
			});
			Get("/-/all", _ =>
			{
				UpdatePackages();
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
			});
			Get("/{package}", query =>
			{
				UpdatePackages();
				var versionList = Packages
					.Where(p => p.Manifest["name"] == query.package)
					.OrderByDescending(p => p.Manifest["version"].ToString())
					.ToArray();
				if (versionList.Length == 0) { return 404; }
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
						["dist"] = GetDist(version.GitRef, version.Path, version.Manifest),
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
					["_id"] = (string)query.package,
					["_rev"] = "1-0", // TODO: What's this value?
					["name"] = (string)query.package,
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
						["url"] = Remote
					},
					["time"] = times
				});
			});
			Get("/{package}/{version}", query =>
			{
				UpdatePackages();
				var (GitRef, Path, Manifest) = Packages.FirstOrDefault(p =>
					p.Manifest["name"] == query.package &&
					p.Manifest["version"] == query.version);
				if (Manifest == null) {
					return 404;
				}
				return JsonResponse(new JObject(Manifest)
				{
					["repository"] = new JObject
					{
						["revision"] = GitRef,
						["type"] = "git",
						["url"] = Remote
					}
					["dist"] = GetDist(GitRef, Path, Manifest),
					["_id"] = $"{query.package}@{query.version}"
				});
			});
			Get("/-/v1/search", _ => {
				UpdatePackages();
				var packages = Packages.Select(p => p.Manifest);
				if (Request.Query.text) {
					bool Match(JToken token) => token != null && token.ToString().Contains(Request.Query.text);
					packages = packages.Where(m => Match(m["name"]) || Match(m["displayName"]) || Match(m["description"]) || Match(m["keywords"]));
				}
				if (Request.Query.size) {
					packages = packages.Take((int)Request.Query.size);
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
			});
			Get("/{package}/{version}/download.{format}", query =>
			{
				UpdatePackages();
				// Locate the package
				var (GitRef, Path, Manifest) = Packages.FirstOrDefault(p =>
					p.Manifest["name"] == query.package &&
					p.Manifest["version"] == query.version);
				if (Manifest == null) {
					return 404;
				}
				return new Response
				{
					StatusCode = HttpStatusCode.OK,
					ContentType = $"application/{query.format}",
					Contents = output => GitClient.GetArchive(
						input => input.CopyTo(output), Remote, GitRef, Path, (string)query.format)
				};
			});
		}

		private static (string GitRef, string Path, JObject Manifest)[] Packages;

		[MethodImpl(MethodImplOptions.Synchronized)]
		private static void UpdatePackages()
		{
			if (Packages == null) {
				Packages = GitClient.ListPackages(Remote, "*/package.json", r => r.Contains("package")).ToArray();
			}
		}

		private static Response JsonResponse(JToken json)
		{
			return new Response
			{
				StatusCode = HttpStatusCode.OK,
				ContentType = "application/json",
				Contents = stream => {
					var writer = new JsonTextWriter(new StreamWriter(stream));
					json.WriteTo(writer);
					writer.Flush();
				}
			};
		}

		private static readonly Dictionary<(string gitRef, string path), string> _hashes
			= new Dictionary<(string gitRef, string path), string>();

		private JObject GetDist(string gitRef, string directory, JObject manifest)
		{
			lock (_hashes)
			{
				const string format = "tgz";
				if (!_hashes.TryGetValue((gitRef, directory), out var hash))
				{
					byte[] hashOut = null;
					GitClient.GetArchive(input => hashOut = SHA1.Create().ComputeHash(input), Remote, gitRef, directory, format);
					var sb = new StringBuilder();
					foreach (var b in hashOut) {
						sb.Append(b.ToString("x2"));
					}
					_hashes.Add((gitRef, directory), hash = sb.ToString());
				}
				return new JObject
				{
					["tarball"] = $"{Request.Url.SiteBase}/{manifest["name"]}/{manifest["version"]}/download.tgz",
					["shasum"] = hash
				};
			}
		}
	}
}