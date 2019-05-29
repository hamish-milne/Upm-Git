using System.Text;
using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Diagnostics;
using ICSharpCode.SharpZipLib.Tar;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace PackageSrv
{
	public class GitClient
	{
		// We keep the manifest as a JObject to preseve the contents without having to deserialize all the keys
		public static IEnumerable<(string Path, JObject Manifest)> ListPackages(string remote, string searchPath, string gitRef)
		{
			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "git",
				Arguments = $"archive --format=tar --remote={remote} {gitRef} {searchPath}",
				RedirectStandardOutput = true,
				UseShellExecute = false
			}))
			{
				Console.WriteLine(gitRef);
				var archive = new TarInputStream(process.StandardOutput.BaseStream)
				{
					IsStreamOwner = false
				};
				TarEntry entry;
				while ((entry = archive.GetNextEntry()) != null)
				{
					if (entry.IsDirectory) { continue; }
					var manifest = (JObject)JObject.ReadFrom(new JsonTextReader(new StreamReader(archive)));
					Console.WriteLine(entry.Name);
					yield return (Path.GetDirectoryName(entry.Name).Replace('\\', '/'), manifest);
				}
			}
		}

		public static IEnumerable<(string GitRef, string Path, JObject Manifest)> ListPackages(string remote, string searchPath, Predicate<string> refFilter)
		{
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
					if (!refFilter(refId)) {
						continue;
					}
					foreach (var entry in ListPackages(remote, searchPath, refId)) {
						yield return (refId, entry.Path, entry.Manifest);
					}
				}
			}
		}

		public static void GetArchive(Action<Stream> streamDelegate, string remote, string gitRef, string directory, string format)
		{
			using (var process = Process.Start(new ProcessStartInfo
			{
				FileName = "git",
				Arguments = $"archive --format={format} --remote={remote} --prefix=package/ {gitRef}:{directory}",
				RedirectStandardOutput = true,
				UseShellExecute = false
			}))
			{
				streamDelegate(process.StandardOutput.BaseStream);
			}
		}
	}
}