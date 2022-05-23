using System;
using System.Collections.Generic;
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
  [BepInAutoPlugin("dev.raptor.dsp.Blackbox", "Blackbox")]
  [BepInProcess("DSPGAME.exe")]
  [BepInDependency(DSPModSavePlugin.MODGUID)]
  [BepInDependency(CommonAPIPlugin.GUID)]
  [CommonAPISubmoduleDependency(nameof(ProtoRegistry), nameof(CustomKeyBindSystem))]
  public partial class Plugin : BaseUnityPlugin, IModCanSave
  {
    private Harmony _harmony;
    internal static ManualLogSource Log;
    public static string Path;

    private void Awake()
    {
      Plugin.Log = Logger;
      Plugin.Path = Info.Location;
      _harmony = new Harmony(Plugin.Id);
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

        ICollection<int> consideredObjIds;
        var inspectStationId = UIRoot.instance.uiGame.inspectStationId;
        if (inspectStationId > 0)
          consideredObjIds = new[] { player.factory.transport.stationPool[inspectStationId].entityId };
        else
          consideredObjIds = player.controller.actionBuild.blueprintCopyTool.selectedObjIds;

        var selection = BlackboxSelection.CreateFrom(player.factory, consideredObjIds);
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
