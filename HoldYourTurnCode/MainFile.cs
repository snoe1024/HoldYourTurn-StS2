using System.Reflection;
using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace HoldYourTurn.HoldYourTurnCode;

//You're recommended but not required to keep all your code in this package and all your assets in the HoldYourTurn folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "HoldYourTurn"; //At the moment, this is used only for the Logger and harmony names.

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static HoldYourTurnConfig Config { get; private set; } = null!;


    public static void Initialize()
    {
        var assembly = Assembly.GetExecutingAssembly();
        
        Config = new HoldYourTurnConfig();
        Config.Load();
        ModConfigRegistry.Register(ModId, Config);

        //If you want to use scripts defined in your mod for Godot scenes, uncomment the following line.
        //Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(assembly);

        Harmony harmony = new(ModId);

        harmony.PatchAll(assembly);
        
        TurnHolder.Initialize();
    }
}