using System;
using System.Collections.Generic;
using System.IO;
using Xamarin.Android.AssemblyStore;		

using Microsoft.Build.Framework;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks;

class AssemblyStoreBuilder
{
	readonly AssemblyStoreGenerator        storeGenerator;
	readonly IList<AssemblyStoreExplorer>? explorers;
	public AssemblyStoreBuilder (string apkPath)
	{
		(explorers, string? errorMessage) = AssemblyStoreExplorer.Open (apkPath);

		if (explorers == null)
			throw new Exception ($"No Explorers");

		string assemblyStoreExt = Path.GetExtension(explorers[0].StorePath);
		storeGenerator = assemblyStoreExt switch {
			".blob" => new AssemblyStoreGenerator_v1(explorers),
			".so"   => new AssemblyStoreGenerator_v2(explorers),
			_       => throw new NotSupportedException ($"Assembly Store Extension {assemblyStoreExt}")
		};
	}

	public void AddAssembly (string assemblySourcePath, AndroidTargetArch arch, string srcDir, bool includeDebugSymbols)
	{
		var storeAssemblyInfo = new AssemblyStoreAssemblyInfo (assemblySourcePath, arch, srcDir);

		// Try to add config if exists.  We use assemblyItem, because `sourcePath` might refer to a compressed
		// assembly file in a different location.
		var config = Path.ChangeExtension (assemblySourcePath, "dll.config");
		if (File.Exists (config)) {
			storeAssemblyInfo.ConfigFile = new FileInfo (config);
		}

		if (includeDebugSymbols) {
			string debugSymbolsPath = Path.ChangeExtension (assemblySourcePath, "pdb");
			if (File.Exists (debugSymbolsPath)) {
				storeAssemblyInfo.SymbolsFile = new FileInfo (debugSymbolsPath);
			}
		}

		storeGenerator.Add (storeAssemblyInfo);
	}

	public Dictionary<AndroidTargetArch, string> Generate (string outputDirectoryPath) => storeGenerator.Generate (outputDirectoryPath);
}
