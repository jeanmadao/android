using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Xamarin.Android.Tools;

namespace Xamarin.Android.AssemblyStore;

partial class StoreReader_V1
{
	sealed class Header
	{
		public const uint NativeSize = 5 * sizeof (uint);

		public readonly uint magic;
		public readonly uint version;
		public readonly uint local_entry_count;
		public readonly uint global_entry_count;
		public readonly uint store_id;

		public Header (uint magic, uint version, uint local_entry_count, uint global_entry_count, uint store_id)
		{
			this.magic = magic;
			this.version = version;
			this.local_entry_count = local_entry_count;
			this.global_entry_count = global_entry_count;
			this.store_id = store_id;
		}
	}

	sealed class EntryDescriptor
	{
		public uint data_offset;
		public uint data_size;

		public uint debug_data_offset;
		public uint debug_data_size;

		public uint config_data_offset;
		public uint config_data_size;
	}

	sealed class AssemblyStoreHashEntry
	{
		public readonly bool Is32Bit;

		public readonly ulong Hash;
		public readonly uint MappingIndex;
		public readonly uint LocalStoreIndex;
		public readonly uint StoreID;

		public AssemblyStoreHashEntry (bool is32Bit, ulong hash, uint mapping_index, uint local_store_index, uint store_id)
		{
			Is32Bit = is32Bit;

			Hash = hash;
			MappingIndex = mapping_index;
			LocalStoreIndex = local_store_index;
			StoreID = store_id;
		}

	}

	internal class AssemblyStoreManifestReader
	{
		static readonly char[] fieldSplit = new char[] { ' ' };

		public List<AssemblyStoreManifestEntry> Entries                      { get; } = new List<AssemblyStoreManifestEntry> ();
		public Dictionary<uint, AssemblyStoreManifestEntry> EntriesByHash32  { get; } = new Dictionary<uint, AssemblyStoreManifestEntry> ();
		public Dictionary<ulong, AssemblyStoreManifestEntry> EntriesByHash64 { get; } = new Dictionary<ulong, AssemblyStoreManifestEntry> ();
		public Dictionary<uint, Dictionary<uint, AssemblyStoreManifestEntry>> EntriesByStore { get; } = new Dictionary<uint, Dictionary<uint, AssemblyStoreManifestEntry>> ();

		public AssemblyStoreManifestReader (Stream manifest)
		{
			manifest.Seek (0, SeekOrigin.Begin);
			using (var sr = new StreamReader (manifest, Encoding.UTF8, detectEncodingFromByteOrderMarks: false)) {
				ReadManifest (sr);
			}
		}

		void ReadManifest (StreamReader reader)
		{
			// First line is ignored, it contains headers
			reader.ReadLine ();

			// Each subsequent line consists of fields separated with any number of spaces (for the pleasure of a human being reading the manifest)
			while (!reader.EndOfStream) {
				string[]? fields = reader.ReadLine ()?.Split (fieldSplit, StringSplitOptions.RemoveEmptyEntries);
				if (fields == null) {
					continue;
				}

				var entry = new AssemblyStoreManifestEntry (fields);
				Entries.Add (entry);
				if (entry.Hash32 != 0) {
					EntriesByHash32.Add (entry.Hash32, entry);
				}

				if (entry.Hash64 != 0) {
					EntriesByHash64.Add (entry.Hash64, entry);
				}

				if (!EntriesByStore.TryGetValue (entry.StoreID, out Dictionary<uint, AssemblyStoreManifestEntry> entries_by_index)) {
					entries_by_index = new Dictionary<uint, AssemblyStoreManifestEntry> ();
					EntriesByStore.Add(entry.StoreID, entries_by_index);
				}
				entries_by_index.Add(entry.IndexInStore, entry);
			}
		}
	}

	internal class AssemblyStoreManifestEntry
	{
		// Fields are:
		//  Hash 32 | Hash 64 | Store ID | Store idx | Name
		const int NumberOfFields = 5;
		const int Hash32FieldIndex = 0;
		const int Hash64FieldIndex = 1;
		const int StoreIDFieldIndex = 2;
		const int StoreIndexFieldIndex = 3;
		const int NameFieldIndex = 4;

		public uint Hash32 { get; }
		public ulong Hash64 { get; }
		public uint StoreID { get; }
		public uint IndexInStore { get; }
		public string Name { get; }
		public uint MappingIndex { get; set; }

		public AssemblyStoreManifestEntry (string[] fields)
		{
			if (fields.Length != NumberOfFields) {
				throw new ArgumentOutOfRangeException (nameof (fields), "Invalid number of fields");
			}

			Hash32 = GetUInt32 (fields[Hash32FieldIndex]);
			Hash64 = GetUInt64 (fields[Hash64FieldIndex]);
			StoreID = UInt32.Parse (fields[StoreIDFieldIndex]);
			IndexInStore = UInt32.Parse (fields[StoreIndexFieldIndex]);
			Name = fields[NameFieldIndex].Trim ();
		}

		uint GetUInt32 (string value)
		{
			if (UInt32.TryParse (PrepHexValue (value), NumberStyles.HexNumber, null, out uint hash)) {
				return hash;
			}

			return 0;
		}

		ulong GetUInt64 (string value)
		{
			if (UInt64.TryParse (PrepHexValue (value), NumberStyles.HexNumber, null, out ulong hash)) {
				return hash;
			}

			return 0;
		}

		string PrepHexValue (string value)
		{
			if (value.StartsWith ("0x", StringComparison.Ordinal)) {
				return value.Substring (2);
			}

			return value;
		}
	}


	sealed class StoreItem_V1 : AssemblyStoreItem
	{
		public StoreItem_V1 (AndroidTargetArch targetArch, string name, bool is64Bit, EntryDescriptor descriptor, List<ulong> hashes, uint mapping_index, uint local_store_index, uint store_id)
			: base (name, is64Bit, hashes)
		{
			MappingIndex = mapping_index;
			LocalStoreIndex = local_store_index;
			StoreID = store_id;
			DataOffset = descriptor.data_offset;
			DataSize = descriptor.data_size;
			DebugOffset = descriptor.debug_data_offset;
			DebugSize = descriptor.debug_data_size;
			ConfigOffset = descriptor.config_data_offset;
			ConfigSize = descriptor.config_data_size;
			TargetArch = targetArch;
		}
	}

}
