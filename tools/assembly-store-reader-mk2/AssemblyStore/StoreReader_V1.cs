using System.Collections.Generic;
using System.IO;

using Xamarin.Android.Tools;
using Xamarin.Android.Tasks;
using System;
using Xamarin.Tools.Zip;
using System.Text;
using System.Reflection.Metadata;
using System.Globalization;

namespace Xamarin.Android.AssemblyStore;

partial class StoreReader_V1 : AssemblyStoreReader
{
	public override string Description => "Assembly store v1";
	public override bool NeedsExtensionInName => false;

	public static IList<string> ApkPaths      { get; }
	public static IList<string> AabPaths      { get; }
	public static IList<string> AabBasePaths  { get; }
	public static AssemblyStoreManifestReader ManifestReader  { get; set; }

	Header? header;

	static StoreReader_V1 ()
	{
		var paths = new List<string> {
			GetArchPath (AndroidTargetArch.None),
			GetArchPath (AndroidTargetArch.Arm64),
			GetArchPath (AndroidTargetArch.Arm),
			GetArchPath (AndroidTargetArch.X86_64),
			GetArchPath (AndroidTargetArch.X86),
		};
		ApkPaths = paths.AsReadOnly ();
		AabBasePaths = ApkPaths;

		const string AabBaseDir = "base/root";
		paths = new List<string> {
			GetArchPath (AndroidTargetArch.None, AabBaseDir),
			GetArchPath (AndroidTargetArch.Arm64, AabBaseDir),
			GetArchPath (AndroidTargetArch.Arm, AabBaseDir),
			GetArchPath (AndroidTargetArch.X86_64, AabBaseDir),
			GetArchPath (AndroidTargetArch.X86, AabBaseDir),
		};
		AabPaths = paths.AsReadOnly ();

		string GetArchPath (AndroidTargetArch arch, string? root = null)
		{
			const string LibDirName = "assemblies";

			string? abi = null;
			if (arch != AndroidTargetArch.None)
				abi = MonoAndroidHelper.ArchToAbi (arch).Replace('-', '_');
			var parts = new List <string> ();
			if (!String.IsNullOrEmpty (root)) {
				parts.Add (LibDirName);
			} else {
				root = LibDirName;
			}
			parts.Add (GetBlobName (abi));

			return MonoAndroidHelper.MakeZipArchivePath (root, parts);
		}

	}

	public StoreReader_V1 (Stream store, string path)
		: base (store, path)
	{}

	static string GetBlobName (string? abi) => $"assemblies{(abi is not null ? $".{abi}" : "")}.blob";

	protected override bool IsSupported ()
	{
		StoreStream.Seek (0, SeekOrigin.Begin);
		using var reader = CreateReader ();

		uint magic = reader.ReadUInt32 ();
		if (magic != Utils.ASSEMBLY_STORE_MAGIC) {
			Log.Debug ($"Store '{StorePath}' has invalid header magic number.");
			return false;
		}

		uint version = reader.ReadUInt32 ();
		if (version == 0) {
			Log.Debug ($"Store '{StorePath}' has unsupported version 0x{version:x}");
			return false;
		}

		if (version > Utils.ASSEMBLY_STORE_FORMAT_VERSION) {
			throw new InvalidOperationException ($"Store format version {version} is higher than the one understood by this reader, {Utils.ASSEMBLY_STORE_FORMAT_VERSION}");
		}

		uint local_entry_count  = reader.ReadUInt32 ();
		uint global_entry_count = reader.ReadUInt32 ();
		uint store_id           = reader.ReadUInt32 ();

		header = new Header (magic, version, local_entry_count, global_entry_count, store_id);
		return true;
	}

	protected override void Prepare ()
	{
		if (header == null) {
			throw new InvalidOperationException ("Internal error: header not set, was IsSupported() called?");
		}

		TargetArch = GetStoreArch(StorePath) switch {
			"arm64_v8a"   => AndroidTargetArch.Arm64,
			"armeabi_v7a" => AndroidTargetArch.Arm,
			"x86_64"      => AndroidTargetArch.X86_64,
			"x86"         => AndroidTargetArch.X86,
			"" 			  => AndroidTargetArch.None,
			_ => throw new NotSupportedException ($"Unsupported ABI in store version: 0x{header.version:x}")
		};

		Is64Bit = TargetArch switch {
			AndroidTargetArch.Arm64 => true,
			AndroidTargetArch.Arm => false,
			AndroidTargetArch.X86_64 => true,
			AndroidTargetArch.X86 => false,
			AndroidTargetArch.None => false,
		};
		AssemblyCount = header.local_entry_count;
		IndexEntryCount = 2 * header.global_entry_count;

		StoreStream.Seek (Header.NativeSize, SeekOrigin.Begin);
		if (header.store_id == 0) {
			ManifestReader = GetManifestReader();
			AssemblyStoreManifestReader GetManifestReader()
			{
				var split_store_path = Path.GetDirectoryName(StorePath).Split('!');
				using var zip = ZipArchive.Open (split_store_path[0], FileMode.Open);
				string manifest_path = Path.Join(split_store_path[1], Path.ChangeExtension(split_store_path[1], "manifest"));
				if (zip.ContainsEntry (manifest_path)) {
					ZipEntry entry = zip.ReadEntry (manifest_path);
					var stream = new MemoryStream ();
					entry.Extract (stream);
					var manifest_reader = new AssemblyStoreManifestReader(stream);
					return manifest_reader;
				}
				return null;
			}
		}
		using var reader = CreateReader ();

		var descriptors = new List<EntryDescriptor> ();
		for (uint i = 0; i < header.local_entry_count; i++) {
			uint data_offset        = reader.ReadUInt32 ();
			uint data_size          = reader.ReadUInt32 ();
			uint debug_data_offset  = reader.ReadUInt32 ();
			uint debug_data_size    = reader.ReadUInt32 ();
			uint config_data_offset = reader.ReadUInt32 ();
			uint config_data_size   = reader.ReadUInt32 ();

			var desc = new EntryDescriptor {
				data_offset        = data_offset,
				data_size          = data_size,
				debug_data_offset  = debug_data_offset,
				debug_data_size    = debug_data_size,
				config_data_offset = config_data_offset,
				config_data_size   = config_data_size,
			};
			descriptors.Add (desc);
		}

		if (header.store_id == 0) {
			ReadIndex (true, ManifestReader);
			ReadIndex (false, ManifestReader);
			void ReadIndex (bool is32Bit, AssemblyStoreManifestReader manifest_reader)
			{
				for (uint i = 0; i < header.global_entry_count; i++) {
					ulong hash = reader.ReadUInt64 ();
					uint mapping_index = reader.ReadUInt32 ();
					uint local_store_index = reader.ReadUInt32 ();
					uint store_id = reader.ReadUInt32 ();
					var hash_entry = new AssemblyStoreHashEntry (is32Bit, hash, mapping_index, local_store_index, store_id);
					AssemblyStoreManifestEntry manifest_entry;
					if (is32Bit)
						manifest_reader.EntriesByHash32.TryGetValue((uint)hash, out manifest_entry);
					else
						manifest_reader.EntriesByHash64.TryGetValue(hash, out manifest_entry);
					manifest_entry.MappingIndex = mapping_index;
				}
			}
		}

		ManifestReader.EntriesByStore.TryGetValue(header.store_id, out Dictionary<uint, AssemblyStoreManifestEntry> entries);

		var storeItems = new List<AssemblyStoreItem> ();
		for (uint i = 0; i < header.local_entry_count; i++) {
			entries.TryGetValue(i, out AssemblyStoreManifestEntry entry);
			var item = new StoreItem_V1 (TargetArch, entry.Name + ".dll", Is64Bit, descriptors[(int)i], new List<ulong> { entry.Hash32, entry.Hash64 }, entry.MappingIndex, entry.IndexInStore, entry.StoreID);
			storeItems.Add (item);
		}
		Assemblies = storeItems.AsReadOnly ();

	}
	string? GetStoreArch (string path)
	{
		string? arch = Path.GetFileNameWithoutExtension (path);
		if (!String.IsNullOrEmpty (arch)) {
			arch = Path.GetExtension (arch);
			if (!String.IsNullOrEmpty (arch)) {
				arch = arch.Substring (1);
			}
		}

		return arch;
	}

	protected override ulong GetStoreStartDataOffset () => 0;
}
