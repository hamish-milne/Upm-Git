using System;
using System.IO;
using System.Diagnostics;
using ICSharpCode.SharpZipLib.Tar;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Microsoft.Extensions.Primitives;

namespace UpmGit
{
	public class SingleFileProvider : IFileProvider, IFileInfo
	{
		public Stream Stream { get; set; }

		public bool Exists => true;

		public long Length => Stream.Length;

		public string PhysicalPath { get; set; }

		public string Name => Path.GetFileName(PhysicalPath);

		public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;

		public bool IsDirectory => false;

		public Stream CreateReadStream() => Stream;

		public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

		public IFileInfo GetFileInfo(string subpath) => this;

		public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
	}

	public class GitClient
	{
		public Regex RefFilter { get; set; }

		// We keep the manifest as a JObject to preseve the contents without having to deserialize all the keys
		public IEnumerable<(string Path, JObject Manifest)> ListPackages(
			string remote, string searchPath, string gitRef)
		{
			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "git",
				Arguments = $"archive --format=tar --remote={remote} {gitRef} {searchPath}",
				RedirectStandardOutput = true,
				UseShellExecute = false
			}))
			{
				Log.Debug("Loading git ref {0}", gitRef);
				var archive = new TarInputStream(process.StandardOutput.BaseStream)
				{
					IsStreamOwner = false
				};
				TarEntry entry;
				while ((entry = archive.GetNextEntry()) != null)
				{
					if (entry.IsDirectory) { continue; }
					var manifest = (JObject)JObject.ReadFrom(new JsonTextReader(new StreamReader(archive)));
					Log.Debug("Found package {0}", entry.Name);
					yield return (Path.GetDirectoryName(entry.Name).Replace('\\', '/'), manifest);
				}
			}
		}

		public async Task<IConfiguration> GetRepositoryConfig(string remote)
		{
			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "git",
				Arguments = $"archive --format=tar --remote={remote} HEAD .upm-git.json",
				RedirectStandardOutput = true,
				UseShellExecute = false
			}))
			{
				var archive = new TarInputStream(process.StandardOutput.BaseStream)
				{
					IsStreamOwner = false
				};
				var entry = archive.GetNextEntry();
				if (entry == null) {
					Log.Debug("No configuration found in {0}", remote);
				} else {
					Log.Information("Configuration found for {0}", remote);
					return new ConfigurationBuilder()
						.AddJsonFile(new SingleFileProvider
						{
							Stream = archive,
							PhysicalPath = entry.Name
						}, entry.Name, false, false)
						.Build();
				}
			}
			return null;
		}

		public IEnumerable<(string GitRef, string Path, JObject Manifest)> ListPackages(
			string remote, string searchPath)
		{
			var config = GetRepositoryConfig(remote);
			config.Wait();
			var refFilter = config.Result == null ? RefFilter
				: new Regex(config.Result.GetValue<string>("refFilter"));
			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "git",
				Arguments = $"ls-remote {remote}",
				RedirectStandardOutput = true,
				UseShellExecute = false
			}))
			{
				string line;
				while ((line = process.StandardOutput.ReadLine()) != null)
				{
					var tokens = line.Split('\t', 2, StringSplitOptions.RemoveEmptyEntries);
					if (tokens.Length < 2) {
						continue;
					}
					var refId = tokens[1];
					if (!refFilter.Match(refId).Success) {
						continue;
					}
					foreach (var entry in ListPackages(remote, searchPath, refId)) {
						yield return (refId, entry.Path, entry.Manifest);
					}
				}
			}
		}

		public async Task GetArchive(Func<Stream, Task> streamDelegate,
			string remote, string gitRef, string directory, string format)
		{
			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "git",
				Arguments = $"archive --format={format} --remote={remote} --prefix=package/ {gitRef}:{directory}",
				RedirectStandardOutput = true,
				UseShellExecute = false
			}))
			{
				await streamDelegate(process.StandardOutput.BaseStream);
			}
		}
	}
}