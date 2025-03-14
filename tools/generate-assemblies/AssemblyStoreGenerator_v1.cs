using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Reflection;
using Microsoft.Build.Utilities;
using Xamarin.Android.AssemblyStore;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks;

//
// Assembly store format
//
// Each target ABI/architecture has a single assembly store file, composed of the following parts:
//
// [HEADER]
// [ASSEMBLY_DESCRIPTORS]
// [INDEX_STORE]
//
// Formats of the sections above are as follows:
//
// HEADER (fixed size)
//  [MAGIC]              uint; value: 0x41424158
//  [FORMAT_VERSION]     uint; store format version number
//  [LOCAL_ENTRY_COUNT]  uint; number of entries in the store
//  [GLOBAL_ENTRY_COUNT] uint; number of global entries in the global index (0 if not the index store)
//  [STORE_ID]           uint; store ID (0 == index store)
//
// ASSEMBLY_DESCRIPTORS (variable size, HEADER.LOCAL_ENTRY_COUNT entries), each entry formatted as follows:
//  [DATA_OFFSET]        uint; offset from the beginning of the store to the start of assembly data
//  [DATA_SIZE]          uint; size of the stored assembly data
//  [DEBUG_DATA_OFFSET]  uint; offset from the beginning of the store to the start of assembly PDB data, 0 if absent
//  [DEBUG_DATA_SIZE]    uint; size of the stored assembly PDB data, 0 if absent
//  [CONFIG_DATA_OFFSET] uint; offset from the beginning of the store to the start of assembly .config contents, 0 if absent
//  [CONFIG_DATA_SIZE]   uint; size of the stored assembly .config contents, 0 if absent
//
// GLOBAL_INDEX (variable size, 2 hash tables of HEADER.GLOBAL_ENTRY_COUNT entries each), each entry formatted as follows:
//  [HASH]               uint64; the 32-bit or 64-bit hash of the assembly's name without the .dll suffix
//  [MAPPING_INDEX]      uint; index into a compile-time generated array of assembly data pointers. This is a global index, unique across all the APK files comprising the application.
//  [LOCAL_STORE_INDEX]  uint; index into assembly store Assembly descriptor table describing the assembly.
//  [STORE_ID]           uint; ID of the assembly store containing the assembly
//
partial class AssemblyStoreGenerator_v1: AssemblyStoreGenerator
{
	// The two constants below must match their counterparts in src/monodroid/jni/xamarin-app.hh
	const uint ASSEMBLY_STORE_MAGIC = 0x41424158; // 'XABA', little-endian, must match the BUNDLED_ASSEMBLIES_BLOB_MAGIC native constant

	IList<AssemblyStoreExplorer> explorers;

	readonly Dictionary<AndroidTargetArch, List<AssemblyStoreAssemblyInfo>> assemblies;
	readonly Dictionary<ulong, AssemblyStoreItem> globalIndexByHash;

	public AssemblyStoreGenerator_v1 (IList<AssemblyStoreExplorer> explorers)
	{
		Console.WriteLine("<=.NET8 APK detected!");
		assemblies = new ();
		globalIndexByHash = new ();
		this.explorers = explorers;
		foreach (var explorer in explorers) {
			foreach (var assembly in explorer.Assemblies) {
				if (!globalIndexByHash.ContainsKey(assembly.Hashes[0]))
					globalIndexByHash.Add (assembly.Hashes[0], assembly);
				if (!globalIndexByHash.ContainsKey(assembly.Hashes[1]))
					globalIndexByHash.Add (assembly.Hashes[1], assembly);
			}
		}
	}

	internal override void Add (AssemblyStoreAssemblyInfo asmInfo)
	{
		if (!assemblies.TryGetValue (asmInfo.Arch, out List<AssemblyStoreAssemblyInfo> infos)) {
			infos = new List<AssemblyStoreAssemblyInfo> ();
			assemblies.Add (asmInfo.Arch, infos);
		}

		infos.Add (asmInfo);
	}

	internal override Dictionary<AndroidTargetArch, string> Generate (string baseOutputDirectory)
	{
		var ret = new Dictionary<AndroidTargetArch, string> ();

		foreach (var kvp in assemblies) {
			string storePath = Generate (baseOutputDirectory, kvp.Key, kvp.Value);
			ret.Add (kvp.Key, storePath);
		}

		return ret;
	}

	string Generate (string baseOutputDirectory, AndroidTargetArch arch, List<AssemblyStoreAssemblyInfo> infos)
	{
		Dictionary<string, AssemblyStoreAssemblyInfo> assembliesByName = new ();
		foreach (var info in infos) {
			assembliesByName.Add(info.AssemblyName, info);
		}
		AssemblyStoreExplorer explorer = explorers.SingleOrDefault(exp => exp.TargetArch == arch );
		string storePath;
		if (arch == AndroidTargetArch.None)
			storePath = Path.Combine (baseOutputDirectory, "none", $"assemblies.blob");
		else 
		{
			string androidAbi = MonoAndroidHelper.ArchToAbi (arch).Replace('-', '_');
			storePath = Path.Combine (baseOutputDirectory, androidAbi, $"assemblies.{androidAbi}.blob");
		}
		uint infoCount = (uint)infos.Count;
		var index = new List<AssemblyStoreIndexEntry> ();
		var descriptors = new List<AssemblyStoreEntryDescriptor> ();

		uint storeVersion = 1;
		uint global_entry_count = 0;
		uint store_id = 1;
		if (arch == AndroidTargetArch.None) {
			global_entry_count = (uint)globalIndexByHash.Count / 2;
			store_id = 0;
		}

		var header = new AssemblyStoreHeader (storeVersion, explorer.AssemblyCount, global_entry_count, store_id);

		ulong assemblyDataStart = AssemblyStoreHeader.NativeSize + explorer.AssemblyCount * AssemblyStoreEntryDescriptor.NativeSize + 2 * global_entry_count * AssemblyStoreHashEntry.NativeSize;
		// We'll start writing to the stream after we seek to the position just after the header, descriptors and hash tables if any.
		ulong curPos = assemblyDataStart;

		Directory.CreateDirectory (Path.GetDirectoryName (storePath));
		using var fs = File.Open (storePath, FileMode.Create, FileAccess.Write, FileShare.Read);
		fs.Seek ((long)curPos, SeekOrigin.Begin);

		foreach (var asm in explorer.Assemblies.OrderBy(asm => asm.MappingIndex)) {
			assembliesByName.TryGetValue(asm.Name, out AssemblyStoreAssemblyInfo info);
			(AssemblyStoreEntryDescriptor desc, curPos) = MakeDescriptor (info, curPos);
			desc.mapping_index = asm.MappingIndex;
			descriptors.Add (desc);

			if ((uint)fs.Position != desc.data_offset) {
				throw new InvalidOperationException ($"Internal error: corrupted store '{storePath}' stream");
			}
			CopyData (info.SourceFile, fs, storePath);
			CopyData (info.SymbolsFile, fs, storePath);
			CopyData (info.ConfigFile, fs, storePath);
		}
		fs.Flush ();
		fs.Seek (0, SeekOrigin.Begin);

		using var writer = new BinaryWriter (fs);
		WriteHeader (writer, header);

		// using var manifestFs = File.Open ($"{storePath}.manifest", FileMode.Create, FileAccess.Write, FileShare.Read);
		// using var mw = new StreamWriter (manifestFs, new System.Text.UTF8Encoding (false));
		// WriteIndex (writer, mw, index, descriptors, is64Bit);
		// mw.Flush ();

		// Console.WriteLine ($"Number of descriptors: {descriptors.Count}; index entries: {index.Count}");
		// Console.WriteLine ($"Header size: {AssemblyStoreHeader.NativeSize}; index entry size: {IndexEntrySize ()}; descriptor size: {AssemblyStoreEntryDescriptor.NativeSize}");

		WriteDescriptors (writer, descriptors);
		if (store_id == 0)
			WriteGlobalIndex (writer, globalIndexByHash);
		writer.Flush ();

		if (fs.Position != (long)assemblyDataStart) {
			Console.WriteLine ($"fs.Position == {fs.Position}; assemblyDataStart == {assemblyDataStart}");
			throw new InvalidOperationException ($"Internal error: store '{storePath}' position is different than metadata size after header write");
		}

		return storePath;
	}

	void CopyData (FileInfo? src, Stream dest, string storePath)
	{
		if (src == null) {
			return;
		}

		Console.WriteLine ($"Adding file '{src.Name}' to assembly store '{storePath}'");
		using var fs = src.Open (FileMode.Open, FileAccess.Read, FileShare.Read);
		fs.CopyTo (dest);
	}

	static (AssemblyStoreEntryDescriptor desc, ulong newPos) MakeDescriptor (AssemblyStoreAssemblyInfo info, ulong curPos)
	{
		var ret = new AssemblyStoreEntryDescriptor {
			data_offset = (uint)curPos,
			data_size = GetDataLength (info.SourceFile),
		};
		if (info.SymbolsFile != null) {
			ret.debug_data_offset = ret.data_offset + ret.data_size;
			ret.debug_data_size = GetDataLength (info.SymbolsFile);
		}

		if (info.ConfigFile != null) {
			ret.config_data_offset = ret.data_offset + ret.data_size + ret.debug_data_size;
			ret.config_data_size = GetDataLength (info.ConfigFile);
		}

		curPos += ret.data_size + ret.debug_data_size + ret.config_data_size;
		if (curPos > UInt32.MaxValue) {
			throw new NotSupportedException ("Assembly store size exceeds the maximum supported value");
		}

		return (ret, curPos);

		uint GetDataLength (FileInfo? info) {
			if (info == null) {
				return 0;
			}

			if (info.Length > UInt32.MaxValue) {
				throw new NotSupportedException ($"File '{info.Name}' exceeds the maximum supported size");
			}

			return (uint)info.Length;
		}
	}

	void WriteHeader (BinaryWriter writer, AssemblyStoreHeader header)
	{
		writer.Write (header.magic);
		writer.Write (header.version);
		writer.Write (header.local_entry_count);
		writer.Write (header.global_entry_count);
		writer.Write (header.store_id);
	}

	void WriteGlobalIndex (BinaryWriter writer,Dictionary<ulong, AssemblyStoreItem> globalIndexByHash)
	{
		foreach (var hash in globalIndexByHash.Keys.Order()) {
			globalIndexByHash.TryGetValue(hash, out AssemblyStoreItem item);
			writer.Write(hash);
			writer.Write(item.MappingIndex);
			writer.Write(item.LocalStoreIndex);
			writer.Write(item.StoreID);
		}
	}

	void WriteDescriptors (BinaryWriter writer, List<AssemblyStoreEntryDescriptor> descriptors)
	{
		foreach (AssemblyStoreEntryDescriptor desc in descriptors) {
			writer.Write (desc.data_offset);
			writer.Write (desc.data_size);
			writer.Write (desc.debug_data_offset);
			writer.Write (desc.debug_data_size);
			writer.Write (desc.config_data_offset);
			writer.Write (desc.config_data_size);
		}
	}
}
