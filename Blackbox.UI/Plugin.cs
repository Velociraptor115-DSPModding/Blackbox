using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using DysonSphereProgram.Modding.Blackbox.UI.Builder;

namespace DysonSphereProgram.Modding.Blackbox.UI
{
  [BepInPlugin(GUID, NAME, VERSION)]
  [BepInProcess("DSPGAME.exe")]
  [BepInDependency("dev.raptor.dsp.Blackbox")]
  public class Plugin : BaseUnityPlugin
  {
    public const string GUID = "dev.raptor.dsp.Blackbox-UI";
    public const string NAME = "Blackbox-UI";
    public const string VERSION = "0.0.8";

    private Harmony _harmony;
    internal static ManualLogSource Log;
    internal static string Path;

    private void Awake()
    {
      Plugin.Log = Logger;
      Plugin.Path = Info.Location;
      _harmony = new Harmony(GUID);
      _harmony.PatchAll(typeof(BlackboxUIPatch));
      UIBuilderPlugin.Create(GUID, BlackboxUIGateway.CreateUI);
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