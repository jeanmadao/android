using System;
using System.Buffers;
using System.IO;

using K4os.Compression.LZ4;

namespace Xamarin.Android.Tasks
{
	class AssemblyCompression
	{
		public enum CompressionResult
		{
			Success,
			InputTooBig,
			EncodingFailed,
		}

		public sealed class AssemblyData
		{
			public string SourcePath { get; internal set; }
			public uint DescriptorIndex { get; internal set; }

			public string DestinationPath;
			public uint SourceSize;
			public uint DestinationSize;

			public AssemblyData (string sourcePath, uint descriptorIndex)
			{
				SetData (sourcePath, descriptorIndex);
			}

			public void SetData (string sourcePath, uint descriptorIndex)
			{
				if (String.IsNullOrEmpty (sourcePath))
					throw new ArgumentException ("must not be null or empty", nameof (sourcePath));
				SourcePath = sourcePath;
				DescriptorIndex = descriptorIndex;
			}
		}

		const uint CompressedDataMagic = 0x5A4C4158; // 'XALZ', little-endian

		// TODO: consider making it configurable via an MSBuild property, would be more flexible this way
		//
		// Arbitrary limit of the input assembly size, to clamp down on memory allocation. Our unlinked Mono.Android.dll
		// assembly (the biggest one we have) is currently (May 2020) around 27MB, so let's bump the value to 30MB times
		// two - it should be more than enough for most needs.
		//public const ulong InputAssemblySizeLimit = 60 * 1024 * 1024;

		static readonly ArrayPool<byte> bytePool = ArrayPool<byte>.Shared;

		static CompressionResult Compress (AssemblyData data, string outputFilePath)
		{
			if (data == null)
				throw new ArgumentNullException (nameof (data));

			var outputDirectory = Path.GetDirectoryName (outputFilePath);

			if (String.IsNullOrEmpty (outputDirectory))
				throw new ArgumentException ("must not be null or empty", nameof (outputDirectory));

			Directory.CreateDirectory (outputDirectory);

			var fi = new FileInfo (data.SourcePath);
			if (!fi.Exists)
				throw new InvalidOperationException ($"File '{data.SourcePath}' does not exist");
			// if ((ulong)fi.Length > InputAssemblySizeLimit) {
			// 	return CompressionResult.InputTooBig;
			// }

			data.DestinationPath = outputFilePath;
			data.SourceSize = checked((uint)fi.Length);

			int bytesRead;
			byte[] sourceBytes = null;
			byte[] destBytes = null;
			try {
				int fileSize = checked((int)fi.Length);
				sourceBytes = bytePool.Rent (fileSize);
				using (var fs = File.Open (data.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
					bytesRead = fs.Read (sourceBytes, 0, fileSize);
				}

				destBytes = bytePool.Rent (LZ4Codec.MaximumOutputSize (bytesRead));
				int encodedLength = LZ4Codec.Encode (sourceBytes, 0, bytesRead, destBytes, 0, destBytes.Length, LZ4Level.L12_MAX);
				if (encodedLength < 0)
					return CompressionResult.EncodingFailed;

				data.DestinationSize = (uint)encodedLength;
				using (var fs = File.Open (data.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.Read)) {
					using (var bw = new BinaryWriter (fs)) {
						bw.Write (CompressedDataMagic);  // magic
						bw.Write (data.DescriptorIndex); // index into runtime array of descriptors
						bw.Write (checked((uint)fi.Length));      // file size before compression

						bw.Write (destBytes, 0, encodedLength);
						bw.Flush ();
					}
				}
			} finally {
				if (sourceBytes != null)
					bytePool.Return (sourceBytes);
				if (destBytes != null)
					bytePool.Return (destBytes);
			}

			return CompressionResult.Success;
		}

		public static bool TryCompress (string sourceAssembly, string destinationAssembly, uint descriptorIndex)
		{
			AssemblyData compressedAssembly = new AssemblyData (sourceAssembly, descriptorIndex);
			CompressionResult result = Compress (compressedAssembly, destinationAssembly);

			if (result != CompressionResult.Success) {
				switch (result) {
					case CompressionResult.EncodingFailed:
						Console.WriteLine ($"Failed to compress {sourceAssembly}");
						break;
					case CompressionResult.InputTooBig:
						Console.WriteLine ($"Input assembly {sourceAssembly} exceeds maximum input size");
						break;
					default:
						Console.WriteLine ($"Unknown error compressing {sourceAssembly}");
						break;
				}

				return false;
			}
			Console.WriteLine ($"Successfully compressed \"{sourceAssembly}\" to path \"{destinationAssembly}\"");

			return true;
		}
	}
}
