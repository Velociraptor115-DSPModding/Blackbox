using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using crecheng.DSPModSave;
using CommonAPI;
using CommonAPI.Systems;

namespace DysonSphereProgram.Modding.Blackbox
{
  [BepInPlugin(GUID, NAME, VERSION)]
  [BepInProcess("DSPGAME.exe")]
  [BepInDependency(DSPModSavePlugin.MODGUID)]
  [BepInDependency(CommonAPIPlugin.GUID)]
  [CommonAPISubmoduleDependency(nameof(ProtoRegistry), nameof(CustomKeyBindSystem))]
  public class Plugin : BaseUnityPlugin, IModCanSave
  {
    public const string GUID = "dev.raptor.dsp.Blackbox";
    public const string NAME = "Blackbox";
    public const string VERSION = "0.0.5";

    private Harmony _harmony;
    internal static ManualLogSource Log;
    public static string Path;

    private void Awake()
    {
      Plugin.Log = Logger;
      Plugin.Path = Info.Location;
      _harmony = new Harmony(GUID);
      _harmony.PatchAll(typeof(BlackboxBenchmarkPatch));
      _harmony.PatchAll(typeof(BlackboxPatch));
      _harmony.PatchAll(typeof(VanillaSavePreservationPatch));
      _harmony.PatchAll(typeof(InputUpdatePatch));
      KeyBinds.RegisterKeyBinds();
      Logger.LogInfo("Blackbox Awake() called");
    }

    private void OnDestroy()
    {
      Logger.LogInfo("Blackbox OnDestroy() called");
      _harmony?.UnpatchSelf();
      Plugin.Log = null;
      Plugin.Path = null;
    }

    public void Export(BinaryWriter w)
    {
      BlackboxManager.Instance.Export(w);
    }

    public void Import(BinaryReader r)
    {
      BlackboxManager.Instance.Import(r);
    }

    public void IntoOtherSave()
    {
      
    }
  }

  [HarmonyPatch(typeof(VFInput), nameof(VFInput.OnUpdate))]
  class InputUpdatePatch
  {
    static void Postfix()
    {
      if (KeyBinds.CreateBlackbox.IsActive)
      {
        var player = GameMain.mainPlayer;
        if (player == null)
          return;
        if (player.factory == null)
          return;

        var selection = BlackboxSelection.CreateFrom(player.factory, player.controller.actionBuild.blueprintCopyTool.selectedObjIds);
        if (selection.stationIds.Count <= 0)
          return;

        BlackboxManager.Instance.CreateForSelection(selection);
      }
    }
  }

  [HarmonyPatch(typeof(GameSave), nameof(GameSave.SaveCurrentGame))]
  class VanillaSavePreservationPatch
  {
    [HarmonyPrefix]
    static void Prefix()
    {
      BlackboxManager.Instance.PreserveVanillaSaveBefore();
    }

    [HarmonyPostfix]
    static void Postfix()
    {
      BlackboxManager.Instance.PreserveVanillaSaveAfter();
    }
  }
}

namespace System.Runtime.CompilerServices
{
  public record IsExternalInit;
}
