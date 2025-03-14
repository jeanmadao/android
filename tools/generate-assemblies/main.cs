using System;
using System.Collections.Generic;
using System.IO;
using Mono.Options;
using Xamarin.Android.Tasks;

namespace Xamarin.Android.Tools.Recompress
{
    class App
    {
        static int Main (string[] args)
        {
            bool showHelp = false;

            var options = new OptionSet {
                "Usage: read-assembly-store [OPTIONS] BLOB_PATH", "",
                    "  where each BLOB_PATH can point to:",
                    "    * aab file",
                    "    * apk file",
                    "    * index store file (e.g. base_assemblies.blob or assemblies.arm64_v8a.blob.so)",
                    "    * arch store file (e.g. base_assemblies.arm64_v8a.blob)",
                    "    * store manifest file (e.g. base_assemblies.manifest)",
                    "    * store base name (e.g. base or base_assemblies)",
                    "",
                    "  In each case the whole set of stores and manifests will be read (if available). Search for the",
                    "  various members of the store set (common/main store, arch stores, manifest) is based on this naming",
                    "  convention:",
                    "",
                    "     {BASE_NAME}[.ARCH_NAME].{blob|blob.so|manifest}",
                    "",
                    "  Whichever file is referenced in `BLOB_PATH`, the BASE_NAME component is extracted and all the found files are read.",
                    "  If `BLOB_PATH` points to an aab or an apk, BASE_NAME will always be `assemblies`",
                    "",
                    // {"a|arch=", $"Limit listing of assemblies to these {{ARCHITECTURES}} only.  A comma-separated list of one or more of: {GetArchNames ()}", v => arches = ParseArchList (v) },
                    "",
                    {"?|h|help", "Show this help screen", v => showHelp = true},
            };

            List<string>? theRest = options.Parse (args);
            if (theRest == null || theRest.Count < 2 || showHelp) {
                options.WriteOptionDescriptions (Console.Out);
                return showHelp ? 0 : 1;
            }

            string srcDir = theRest[0];
            if (!Path.EndsInDirectorySeparator(srcDir)) {
                srcDir = srcDir + Path.DirectorySeparatorChar;
            }
            srcDir = Path.GetFullPath(srcDir);

            AndroidTargetArch arch = theRest[1] switch {
                "Arm64" => AndroidTargetArch.Arm64,
                "Arm" => AndroidTargetArch.Arm,
                "x64" => AndroidTargetArch.X86_64,
                "x86" => AndroidTargetArch.X86,
                "None" => AndroidTargetArch.None,
                _ => throw new NotSupportedException($"Unsupported arch {theRest[1]}")
            };

            AssemblyStoreBuilder storeBuilder = new AssemblyStoreBuilder(theRest[2]);

            foreach (string file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories)) {
                storeBuilder.AddAssembly(file, arch, srcDir, true);
            }
            string storePath = storeBuilder.Generate(Directory.GetParent(Directory.GetParent(srcDir).FullName).FullName)[arch];
            Console.WriteLine(storePath);

            return 0;
        }
    }
}
