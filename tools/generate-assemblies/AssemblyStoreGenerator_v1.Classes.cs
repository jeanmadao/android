namespace Xamarin.Android.Tasks;

partial class AssemblyStoreGenerator_v1
{
	sealed class AssemblyStoreHeader
	{
		public const uint NativeSize = 5 * sizeof (uint);

		public readonly uint magic = ASSEMBLY_STORE_MAGIC;
		public readonly uint version;
		public readonly uint local_entry_count;
		public readonly uint global_entry_count;
		public readonly uint store_id;

		public AssemblyStoreHeader (uint version, uint local_entry_count, uint global_entry_count, uint store_id)
		{
			this.version = version;
			this.local_entry_count = local_entry_count;
			this.global_entry_count = global_entry_count;
			this.store_id = store_id;
		}
#if XABT_TESTS
		public AssemblyStoreHeader (uint magic, uint version, uint local_entry_count, uint global_entry_count, uint store_id)
			: this (version, local_entry_count, global_entry_count, store_id)
		{
			this.magic = magic;
		}
#endif
	}

	sealed class AssemblyStoreIndexEntry
	{
		public const uint NativeSize32 = 2 * sizeof (uint);
		public const uint NativeSize64 = sizeof (ulong) + sizeof (uint);

		public readonly string name;
		public readonly ulong name_hash;
		public readonly uint  descriptor_index;

		public AssemblyStoreIndexEntry (string name, ulong name_hash, uint descriptor_index)
		{
			this.name = name;
			this.name_hash = name_hash;
			this.descriptor_index = descriptor_index;
		}
	}

	sealed class AssemblyStoreEntryDescriptor
	{
		public const uint NativeSize = 6 * sizeof (uint);

		public uint mapping_index;
		public uint data_offset;
		public uint data_size;

		public uint debug_data_offset;
		public uint debug_data_size;

		public uint config_data_offset;
		public uint config_data_size;
	}

	sealed class AssemblyStoreHashEntry
	{
		public const uint NativeSize = 3 * sizeof (uint) + sizeof(ulong);
		public bool is_32_bit;
		public ulong hash;
		public uint mapping_index;
		public uint local_store_index;
		public uint store_id;
	}

}
