using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using DysonSphereProgram.Modding.Blackbox.UI.Builder;
using BlackboxPlugin = DysonSphereProgram.Modding.Blackbox.Plugin;

namespace DysonSphereProgram.Modding.Blackbox.UI
{
  [BepInAutoPlugin("dev.raptor.dsp.Blackbox-UI", "Blackbox-UI")]
  [BepInProcess("DSPGAME.exe")]
  [BepInDependency(BlackboxPlugin.Id)]
  public partial class Plugin : BaseUnityPlugin
  {
    private Harmony _harmony;
    internal static ManualLogSource Log;
    internal static string Path;

    private void Awake()
    {
      Plugin.Log = Logger;
      Plugin.Path = Info.Location;
      _harmony = new Harmony(Plugin.Id);
      _harmony.PatchAll(typeof(BlackboxUIPatch));
      UIBuilderPlugin.Create(Plugin.Id, BlackboxUIGateway.CreateUI);
      Logger.LogInfo("Blackbox-UI Awake() called");
    }

    private void OnDestroy()
    {
      Logger.LogInfo("Blackbox-UI OnDestroy() called");
      BlackboxUIGateway.DestroyUI();
      UIBuilderPlugin.Destroy();
      _harmony?.UnpatchSelf();
      Plugin.Log = null;
      Plugin.Path = null;
    }
  }
}