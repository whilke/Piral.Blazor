using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using System;
using System.IO;
using System.Dynamic;
using Piral.Blazor.Tools.Models;

namespace Piral.Blazor.Tools.Tasks
{
    public class SetMonorepoPiralInstanceTask : Task
    {
        [Required]
        public string PiralInstancePath { get; set; }

        [Required]
        public string PiletPath { get; set; }

        public override bool Execute()
        {
            if (!PiralInstancePath.StartsWith("."))
            {
                Log.LogMessage("Nothing to do here - the given Piral instance already refers to a package.");
                return true;
            }

            Log.LogMessage("Symlinking to the piral instance inside the monorepo...");

            try
            {
                var piletPackageJsonFile = Path.Combine(PiletPath, "package.json");

                if (!File.Exists(piletPackageJsonFile))
                {
                    Log.LogError($"Could not find the pilet's package.json file at '{piletPackageJsonFile}'."); 
                    return false;
                }

                var piralInstanceVersion = GetPiralInstanceVersion();
                var escapedPath = PiralInstancePath.Replace('\\', '/');
                var piralInstancePathInPackageJson = $"file:../{escapedPath}";
                var piletPackageJsonText = File.ReadAllText(piletPackageJsonFile)
                    .Replace(piralInstancePathInPackageJson, piralInstanceVersion);

                File.WriteAllText(piletPackageJsonFile, piletPackageJsonText); 
            }
            catch (Exception error)
            {
                Log.LogError(error.Message);  
                return false;
            }

            return true;
        }

        private dynamic GetPiralInstanceVersion()
        {
            var piralInstanceDirectory = Path.GetDirectoryName(PiralInstancePath)
                .Replace($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}emulator", "");
            var piralInstancePackageJsonFile = Path.Combine(piralInstanceDirectory, "package.json");
            
            if (!File.Exists(piralInstancePackageJsonFile))
            {
                Log.LogError($"Could not find Piral instance package.json file at '{piralInstancePackageJsonFile}'."); 
                throw new FileNotFoundException();
            }

            var content = File.ReadAllText(piralInstancePackageJsonFile);
            return JsonConvert.DeserializeObject<PackageJsonObject>(content).Version;
        }
    }
}
