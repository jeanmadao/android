using System;
using System.Buffers;
using System.IO;
using Mono.Options;

using K4os.Compression.LZ4;
using Xamarin.Tools.Zip;
using Xamarin.Android.AssemblyStore;
using System.Collections.Generic;

namespace Xamarin.Android.Tools.DecompressAssemblies
{
	class App
	{
		const uint CompressedDataMagic = 0x5A4C4158; // 'XALZ', little-endian

		static readonly ArrayPool<byte> bytePool = ArrayPool<byte>.Shared;

		static bool ExtractDLL (Stream inputStream, string fileName, string filePath, string prefix, bool uncompress)
		{
			if (String.IsNullOrEmpty (prefix))
				filePath = Path.GetFileNameWithoutExtension(filePath);
			string outputFile = $"{prefix}{filePath}";
			bool retVal = true;

			Console.WriteLine ($"Processing {fileName}");
			//
			// LZ4 compressed assembly header format:
			//   uint magic;                 // 0x5A4C4158; 'XALZ', little-endian
			//   uint descriptor_index;      // Index into an internal assembly descriptor table
			//   uint uncompressed_length;   // Size of assembly, uncompressed
			//
			string outputDir = Path.GetDirectoryName (outputFile);
			if (!String.IsNullOrEmpty (outputDir)) {
				Directory.CreateDirectory (outputDir);
			}
			if (uncompress) {
				using (var reader = new BinaryReader (inputStream)) {
					uint magic = reader.ReadUInt32 ();
					if (magic == CompressedDataMagic) {
						reader.ReadUInt32 (); // descriptor index, ignore
						uint decompressedLength = reader.ReadUInt32 ();

						int inputLength = (int) (inputStream.Length - 12);
						byte [] sourceBytes = bytePool.Rent (inputLength);
						reader.Read (sourceBytes, 0, inputLength);

						byte [] assemblyBytes = bytePool.Rent ((int) decompressedLength);
						int decoded = LZ4Codec.Decode (sourceBytes, 0, inputLength, assemblyBytes, 0, (int) decompressedLength);
						if (decoded != (int) decompressedLength) {
							Console.Error.WriteLine ($"  Failed to decompress LZ4 data of {fileName} (decoded: {decoded})");
							retVal = false;
						} else {
							using (var fs = File.Open (outputFile, FileMode.Create, FileAccess.Write)) {
								fs.Write (assemblyBytes, 0, decoded);
								fs.Flush ();
							}
							Console.WriteLine ($"  uncompressed to: {outputFile}");
						}

						bytePool.Return (sourceBytes);
						bytePool.Return (assemblyBytes);
					} else {
						Console.WriteLine ($"  assembly is not compressed");
					}
				}
			} else {
				outputFile = outputFile + ".lz4";
				using (var fs = File.Open (outputFile, FileMode.Create, FileAccess.Write)) {
					inputStream.Seek (0, SeekOrigin.Begin);
					inputStream.CopyTo (fs);
				}
				Console.WriteLine ($"  saved to: {outputFile}");
			}
			return retVal;
		}

		static bool ExtractDLL (string filePath, string prefix, bool uncompress)
		{
			using (var fs = File.Open (filePath, FileMode.Open, FileAccess.Read)) {
				return ExtractDLL (fs, filePath, Path.GetFileName (filePath), prefix, uncompress);
			}
		}

		static bool ExtractFromAPK_IndividualEntries (ZipArchive apk, string filePath, string assembliesPath, string prefix, bool uncompress)
		{
			foreach (ZipEntry entry in apk) {
				if (!entry.FullName.StartsWith (assembliesPath, StringComparison.Ordinal)) {
					continue;
				}

				if (!entry.FullName.EndsWith (".dll", StringComparison.Ordinal)) {
					continue;
				}

				using (var stream = new MemoryStream ()) {
					entry.Extract (stream);
					stream.Seek (0, SeekOrigin.Begin);
					string fileName = entry.FullName.Substring (assembliesPath.Length);
					ExtractDLL (stream, $"{filePath}!{entry.FullName}", fileName, prefix, uncompress);
				}
			}

			return true;
		}

		static bool ExtractFromAPK_AssemblyStores (string filePath, string prefix, bool uncompress)
		{
			(IList<AssemblyStoreExplorer>? explorers, string? errorMessage) = AssemblyStoreExplorer.Open (filePath);
			foreach (AssemblyStoreExplorer store in explorers) {
				foreach (AssemblyStoreItem assembly in store.Assemblies) {
					string assemblyName = $"{assembly.TargetArch}/{assembly.Name}";

					ExtractDLL (store.ReadImageData(assembly), $"{filePath}!{assemblyName}", assemblyName, prefix, uncompress);
				}
			}

			return true;
		}

		static bool ExtractFromAPK (string filePath, string assembliesPath, bool uncompress)
		{
			string prefix;
			if (uncompress)
				prefix = $"uncompressed-{Path.GetFileNameWithoutExtension (filePath)}{Path.DirectorySeparatorChar}";
			else
				prefix = $"compressed-{Path.GetFileNameWithoutExtension (filePath)}{Path.DirectorySeparatorChar}";
			// using (ZipArchive apk = ZipArchive.Open (filePath, FileMode.Open)) {
			// 	if (!apk.ContainsEntry ($"{assembliesPath}assemblies.blob")) {
			// 		return UncompressFromAPK_IndividualEntries (apk, filePath, assembliesPath, prefix, uncompress);
			// 	}
			// }

			return ExtractFromAPK_AssemblyStores (filePath, prefix, uncompress);
		}

		static int Main (string[] args)
		{
			bool showHelp = false;
			bool compressed = false;

			var options = new OptionSet {
                "Usage: decompress-assemblies [OPTIONS] BLOB_PATH", "",
                    "  where each BLOB_PATH can point to:",
                    "    * aab file",
                    "    * apk file",
                    "    * index store file (e.g. base_assemblies.blob or assemblies.arm64_v8a.blob.so)",
                    "    * arch store file (e.g. base_assemblies.arm64_v8a.blob)",
                    "    * store manifest file (e.g. base_assemblies.manifest)",
                    "    * store base name (e.g. base or base_assemblies)",
                    "",
                    {"c|compressed", "Extract the assembly images without decompressing them", v => compressed = true},
                    "",
                    {"?|h|help", "Show this help screen", v => showHelp = true},
            };

            List<string>? theRest = options.Parse (args);
            if (theRest == null || theRest.Count < 1 || showHelp) {
                options.WriteOptionDescriptions (Console.Out);
                return showHelp ? 0 : 1;
            }

			bool haveErrors = false;
			foreach (string file in args) {
				string ext = Path.GetExtension (file);
				if (String.Compare (".dll", ext, StringComparison.OrdinalIgnoreCase) == 0) {
					if (!ExtractDLL (file, "uncompressed-", true)) {
						haveErrors = true;
					}
					continue;
				}

				if (String.Compare (".lz4", ext, StringComparison.OrdinalIgnoreCase) == 0) {
					if (!ExtractDLL (file, "", true)) {
						haveErrors = true;
					}
					continue;
				}

				if (String.Compare (".apk", ext, StringComparison.OrdinalIgnoreCase) == 0) {
					if (!ExtractFromAPK (file, "assemblies/", !compressed) && !ExtractFromAPK (file, "lib/", !compressed)) {
						haveErrors = true;
					}
					continue;
				}

				if (String.Compare (".aab", ext, StringComparison.OrdinalIgnoreCase) == 0) {
					if (!ExtractFromAPK (file, "base/root/assemblies/", !compressed)) {
						haveErrors = true;
					}
					continue;
				}
			}

			return haveErrors ? 1 : 0;
		}
	}
}
