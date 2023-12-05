using CobaltCoreModding.Definitions;
using CobaltCoreModding.Definitions.ModManifests;
using HarmonyLib;
using Microsoft.Extensions.Logging;

namespace ITRsSaveFix;

public class ModManifest : IManifest
{
    public static ModManifest? Instance { get; private set; }
    public IEnumerable<DependencyEntry> Dependencies => Array.Empty<DependencyEntry>();
    public DirectoryInfo? GameRootFolder { get; set; }
    public ILogger? Logger { get; set; }
    public DirectoryInfo? ModRootFolder { get; set; }
    public string Name => "ITR's Save Fix";

    public ModManifest()
    {
        Instance = this;
        var harmony = new Harmony("com.itr.cobaltcore.savefix");
        harmony.PatchAll();
    }
}
