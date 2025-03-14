using System;
using System.IO;

using Microsoft.Build.Framework;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks;

class AssemblyStoreAssemblyInfo
{
	public AndroidTargetArch Arch        { get; }
	public FileInfo SourceFile           { get; }
	public string AssemblyName           { get; }
	public byte[] AssemblyNameBytes      { get; }
	public string AssemblyNameNoExt      { get; }
	public byte[] AssemblyNameNoExtBytes { get; }
	public FileInfo? SymbolsFile         { get; set; }
	public FileInfo? ConfigFile          { get; set; }

	public AssemblyStoreAssemblyInfo (string sourceFilePath, AndroidTargetArch arch, string srcDir)
	{
		Arch = arch;
		// if (Arch == AndroidTargetArch.None) {
		// 	throw new InvalidOperationException ($"Internal error: assembly item '{sourceFilePath}' lacks ABI information metadata");
		// }

		SourceFile = new FileInfo (sourceFilePath);

		string? name = Path.GetFileName (SourceFile.Name);
		if (name == null) {
			throw new InvalidOperationException ("Internal error: info without assembly name");
		}

		if (name.EndsWith (".lz4", StringComparison.OrdinalIgnoreCase)) {
			name = Path.GetFileNameWithoutExtension (name);
		}

		string nameNoExt = Path.GetFileNameWithoutExtension (name);
		string parentDir = Directory.GetParent (sourceFilePath).Name;
		if (parentDir != Directory.GetParent(srcDir).Name) {
			name = $"{parentDir}/{name}";
			nameNoExt = $"{parentDir}/{nameNoExt}";
		}

		(AssemblyName, AssemblyNameBytes) = SetName (name);
		(AssemblyNameNoExt, AssemblyNameNoExtBytes) = SetName (nameNoExt);

		(string name, byte[] bytes) SetName (string assemblyName)
		{
			return (assemblyName, MonoAndroidHelper.Utf8StringToBytes (assemblyName));
		}
	}
}
