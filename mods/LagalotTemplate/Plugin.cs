using BepInEx;
using BepInEx.Logging;
using Jotunn;

namespace LagalotTemplate;

[BepInPlugin(GUID, NAME, VERSION)]
public class LagalotTemplatePlugin : BaseUnityPlugin
{
    public const string GUID = "lagalot.template";
    public const string NAME = "LagalotTemplate";
    public const string VERSION = "0.0.1";

    internal static new ManualLogSource Logger = null!;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"{NAME} {VERSION} loaded.");

        // Jotunn entry points for custom content:
        //   ItemManager      - custom items, pieces, recipes
        //   PieceManager     - custom build pieces
        //   PrefabManager    - custom prefabs / creatures
        //   ZoneManager      - custom locations, vegvisirs
        //   Localization      - custom strings / translations
        //   ConfigManager     - in-game config UI (also syncs to clients)
        // Player controls/behaviors are usually Harmony patches on
        //   Player methods (see assembly_valheim.dll in a decompiler).
    }
}
