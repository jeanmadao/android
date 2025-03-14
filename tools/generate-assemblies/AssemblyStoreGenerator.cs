using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Build.Utilities;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks;

abstract class AssemblyStoreGenerator
{
	readonly Dictionary<AndroidTargetArch, List<AssemblyStoreAssemblyInfo>> assemblies;

	public AssemblyStoreGenerator ()
	{
		// assemblies = new Dictionary<AndroidTargetArch, List<AssemblyStoreAssemblyInfo>> ();
	}

	internal abstract void Add (AssemblyStoreAssemblyInfo asmInfo);

	internal abstract Dictionary<AndroidTargetArch, string> Generate (string baseOutputDirectory);

	// protected abstract string Generate (string baseOutputDirectory, AndroidTargetArch arch, List<AssemblyStoreAssemblyInfo> infos);

	// protected abstract void CopyData (FileInfo? src, Stream dest, string storePath);

	// protected abstract static (AssemblyStoreEntryDescriptor desc, ulong newPos) MakeDescriptor (AssemblyStoreAssemblyInfo info, ulong curPos);

// 	void WriteHeader (BinaryWriter writer, AssemblyStoreHeader header)
// 	{
// 		writer.Write (header.magic);
// 		writer.Write (header.version);
// 		writer.Write (header.entry_count);
// 		writer.Write (header.index_entry_count);
// 		writer.Write (header.index_size);
// 	}
// #if XABT_TESTS
// 	AssemblyStoreHeader ReadHeader (BinaryReader reader)
// 	{
// 		reader.BaseStream.Seek (0, SeekOrigin.Begin);
// 		uint magic             = reader.ReadUInt32 ();
// 		uint version           = reader.ReadUInt32 ();
// 		uint entry_count       = reader.ReadUInt32 ();
// 		uint index_entry_count = reader.ReadUInt32 ();
// 		uint index_size        = reader.ReadUInt32 ();

// 		return new AssemblyStoreHeader (magic, version, entry_count, index_entry_count, index_size);
// 	}
// #endif

// 	void WriteIndex (BinaryWriter writer, StreamWriter manifestWriter, List<AssemblyStoreIndexEntry> index, List<AssemblyStoreEntryDescriptor> descriptors, bool is64Bit)
// 	{
// 		index.Sort ((AssemblyStoreIndexEntry a, AssemblyStoreIndexEntry b) => a.name_hash.CompareTo (b.name_hash));

// 		foreach (AssemblyStoreIndexEntry entry in index) {
// 			if (is64Bit) {
// 				writer.Write (entry.name_hash);
// 				manifestWriter.Write ($"0x{entry.name_hash:x}");
// 			} else {
// 				writer.Write ((uint)entry.name_hash);
// 				manifestWriter.Write ($"0x{(uint)entry.name_hash:x}");
// 			}
// 			writer.Write (entry.descriptor_index);
// 			manifestWriter.Write ($" di:{entry.descriptor_index}");

// 			AssemblyStoreEntryDescriptor desc = descriptors[(int)entry.descriptor_index];
// 			manifestWriter.Write ($" mi:{desc.mapping_index}");
// 			manifestWriter.Write ($" do:{desc.data_offset}");
// 			manifestWriter.Write ($" ds:{desc.data_size}");
// 			manifestWriter.Write ($" ddo:{desc.debug_data_offset}");
// 			manifestWriter.Write ($" dds:{desc.debug_data_size}");
// 			manifestWriter.Write ($" cdo:{desc.config_data_offset}");
// 			manifestWriter.Write ($" cds:{desc.config_data_size}");
// 			manifestWriter.WriteLine ($" {entry.name}");
// 		}
// 	}

// 	List<AssemblyStoreIndexEntry> ReadIndex (BinaryReader reader, AssemblyStoreHeader header)
// 	{
// 		if (header.index_entry_count > Int32.MaxValue) {
// 			throw new InvalidOperationException ("Assembly store index is too big");
// 		}

// 		var index = new List<AssemblyStoreIndexEntry> ((int)header.index_entry_count);
// 		reader.BaseStream.Seek (AssemblyStoreHeader.NativeSize, SeekOrigin.Begin);

// 		bool is64Bit = (header.version & ASSEMBLY_STORE_FORMAT_VERSION_64BIT) == ASSEMBLY_STORE_FORMAT_VERSION_64BIT;
// 		for (int i = 0; i < (int)header.index_entry_count; i++) {
// 			ulong name_hash;
// 			if (is64Bit) {
// 				name_hash = reader.ReadUInt64 ();
// 			} else {
// 				name_hash = reader.ReadUInt32 ();
// 			}

// 			uint descriptor_index = reader.ReadUInt32 ();
// 			index.Add (new AssemblyStoreIndexEntry (String.Empty, name_hash, descriptor_index));
// 		}

// 		return index;
// 	}

// 	void WriteDescriptors (BinaryWriter writer, List<AssemblyStoreEntryDescriptor> descriptors)
// 	{
// 		foreach (AssemblyStoreEntryDescriptor desc in descriptors) {
// 			writer.Write (desc.mapping_index);
// 			writer.Write (desc.data_offset);
// 			writer.Write (desc.data_size);
// 			writer.Write (desc.debug_data_offset);
// 			writer.Write (desc.debug_data_size);
// 			writer.Write (desc.config_data_offset);
// 			writer.Write (desc.config_data_size);
// 		}
// 	}

// 	List<AssemblyStoreEntryDescriptor> ReadDescriptors (BinaryReader reader, AssemblyStoreHeader header)
// 	{
// 		if (header.entry_count > Int32.MaxValue) {
// 			throw new InvalidOperationException ("Assembly store descriptor table is too big");
// 		}

// 		var descriptors = new List<AssemblyStoreEntryDescriptor> ();
// 		reader.BaseStream.Seek (AssemblyStoreHeader.NativeSize + header.index_size, SeekOrigin.Begin);

// 		for (int i = 0; i < (int)header.entry_count; i++) {
// 			uint mapping_index      = reader.ReadUInt32 ();
// 			uint data_offset        = reader.ReadUInt32 ();
// 			uint data_size          = reader.ReadUInt32 ();
// 			uint debug_data_offset  = reader.ReadUInt32 ();
// 			uint debug_data_size    = reader.ReadUInt32 ();
// 			uint config_data_offset = reader.ReadUInt32 ();
// 			uint config_data_size   = reader.ReadUInt32 ();

// 			var desc = new AssemblyStoreEntryDescriptor {
// 				mapping_index      = mapping_index,
// 				data_offset        = data_offset,
// 				data_size          = data_size,
// 				debug_data_offset  = debug_data_offset,
// 				debug_data_size    = debug_data_size,
// 				config_data_offset = config_data_offset,
// 				config_data_size   = config_data_size,
// 			};
// 			descriptors.Add (desc);
// 		}

// 		return descriptors;
// 	}

// 	void WriteNames (BinaryWriter writer, List<AssemblyStoreAssemblyInfo> infos)
// 	{
// 		foreach (AssemblyStoreAssemblyInfo info in infos) {
// 			writer.Write ((uint)info.AssemblyNameBytes.Length);
// 			writer.Write (info.AssemblyNameBytes);
// 		}
// 	}
}
