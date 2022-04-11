using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DysonSphereProgram.Modding.Blackbox.UI
{
  [HarmonyPatch]
  class BlackboxUIPatch
  {
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame.OnPlayerInspecteeChange))]
    static void UIGame__OnPlayerInspecteeChange(EObjectType objType, int objId, ref bool __runOriginal)
    {
      if (!__runOriginal)
        return;
      var factory = GameMain.mainPlayer.factory;
      if (factory == null || objType != EObjectType.Entity || objId <= 0)
        return;

      var entity = factory.entityPool[objId];
      Debug.Log($"Trying to inspect Entity #{objId}");

      var blackboxId = 0;

      if (entity.assemblerId > 0 && factory.factorySystem.assemblerPool[entity.assemblerId].id < 0)
        blackboxId = -factory.factorySystem.assemblerPool[entity.assemblerId].id;
      if (entity.labId > 0 && factory.factorySystem.labPool[entity.labId].id < 0)
        blackboxId = -factory.factorySystem.labPool[entity.labId].id;
      if (entity.beltId > 0 && factory.cargoTraffic.beltPool[entity.beltId].id < 0)
        blackboxId = -factory.cargoTraffic.beltPool[entity.beltId].id;
      if (entity.splitterId > 0 && factory.cargoTraffic.splitterPool[entity.splitterId].id < 0)
        blackboxId = -factory.cargoTraffic.splitterPool[entity.splitterId].id;
      if (entity.inserterId > 0 && factory.factorySystem.inserterPool[entity.inserterId].id < 0)
        blackboxId = -factory.factorySystem.inserterPool[entity.inserterId].id;
      if (entity.spraycoaterId > 0 && factory.cargoTraffic.spraycoaterPool[entity.spraycoaterId].id < 0)
        blackboxId = -factory.cargoTraffic.spraycoaterPool[entity.spraycoaterId].id;
      if (entity.pilerId > 0 && factory.cargoTraffic.pilerPool[entity.pilerId].id < 0)
        blackboxId = -factory.cargoTraffic.pilerPool[entity.pilerId].id;
      if (entity.monitorId > 0 && factory.cargoTraffic.monitorPool[entity.monitorId].id < 0)
        blackboxId = -factory.cargoTraffic.monitorPool[entity.monitorId].id;
      
      var stationId = entity.stationId;

      if (blackboxId <= 0 && stationId <= 0)
        return;
      
      var blackbox =
        stationId > 0
          ? BlackboxManager.Instance.blackboxes.Find(x => x.Selection.stationIds.Contains(stationId))
          : BlackboxManager.Instance.blackboxes.Find(x => x.Id == blackboxId);

      if (blackbox != null)
      {
        Debug.Log($"Seems to be part of Blackbox#{blackbox.Id}");
        UIRoot.instance.uiGame.ShutAllFunctionWindow();
        BlackboxUIGateway.InspectBlackbox(blackbox);
        if (stationId > 0)
        {
          UIRoot.instance.uiGame.stationWindow.stationId = stationId;
          UIRoot.instance.uiGame.OpenStationWindow();
          UIRoot.instance.uiGame.inspectStationId = stationId;
        }

        __runOriginal = false;
      }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame._OnUpdate))]
    static void UIGame___OnUpdate()
    {
      BlackboxUIGateway.Update();

      var uiBlackboxWindowManager = BlackboxUIGateway.BlackboxManagerWindow?.Component;
      if (uiBlackboxWindowManager != null && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.M))
      {
        if (uiBlackboxWindowManager.active)
          uiBlackboxWindowManager._Close();
        else
          uiBlackboxWindowManager._Open();
      }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame._OnFree))]
    static void UIGame___OnFree()
    {
      BlackboxUIGateway.Free();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame._OnDestroy))]
    static void UIGame___OnDestroy()
    {
      BlackboxUIGateway.DestroyUI();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame.isAnyFunctionWindowActive), MethodType.Getter)]
    static void PatchIsAnyBlackboxWindowActive(ref bool __result)
    {
      var inspectWindow = BlackboxUIGateway.BlackboxInspectWindow.Component;
      var managerWindow = BlackboxUIGateway.BlackboxManagerWindow.Component;
      __result = __result || inspectWindow && inspectWindow.active;
      __result = __result || managerWindow && managerWindow.active;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame.ShutInventoryConflictsWindows))]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame.ShutAllFunctionWindow))]
    static void UIGame__ShutAllFunctionWindow()
    {
      BlackboxUIGateway.BlackboxInspectWindow.Component._Close();
      BlackboxUIGateway.BlackboxInspectWindow.Component._Free();
      BlackboxUIGateway.BlackboxManagerWindow.Component._Close();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SignalProtoSet), nameof(SignalProtoSet.IconSprite))]
    static void SignalProtoSet__IconSprite(int signalId, ref Sprite __result)
    {
      if (signalId == BlackboxHighlight.blackboxSignalId)
        __result = BlackboxUIGateway.iconSprite;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(WarningSystem), nameof(WarningSystem.WarningLogic))]
    static void WarningSystem__WarningLogic(ref WarningSystem __instance)
    {
      var highlight = BlackboxManager.Instance.highlight;
      if (highlight.blackboxId > 0)
      {
        var warningPool = __instance.warningPool;
        foreach (var warningId in highlight.warningIds)
        {
          warningPool[warningId].state = 1;
          warningPool[warningId].signalId = BlackboxHighlight.blackboxSignalId;

          __instance.warningCounts[BlackboxHighlight.blackboxSignalId]++;
        }
      }

      if (__instance.warningCounts[BlackboxHighlight.blackboxSignalId] > 0)
      {
        __instance.warningSignals[__instance.warningSignalCount] = BlackboxHighlight.blackboxSignalId;
        __instance.warningSignalCount++;
      }
    }
  }

  public static class BlackboxUIGateway
  {
    private static ModdedUIBlackboxInspectWindow blackboxInspectWindow;
    private static ModdedUIBlackboxManagerWindow blackboxManagerWindow;
    private static List<IModdedUI> moddedUIs;
    public static Sprite iconSprite;

    public static ModdedUIBlackboxInspectWindow BlackboxInspectWindow => blackboxInspectWindow;
    public static ModdedUIBlackboxManagerWindow BlackboxManagerWindow => blackboxManagerWindow;

    static BlackboxUIGateway()
    {
      blackboxInspectWindow = new ModdedUIBlackboxInspectWindow();
      blackboxManagerWindow = new ModdedUIBlackboxManagerWindow();

      moddedUIs = new List<IModdedUI>
      {
          blackboxInspectWindow
        , blackboxManagerWindow
      };
    }
    
    public static void InspectBlackbox(Blackbox blackbox)
    {
      var uiBlackboxInspectWindow = BlackboxInspectWindow?.Component;
      if (uiBlackboxInspectWindow != null)
      {
        if (uiBlackboxInspectWindow.active && uiBlackboxInspectWindow.data != blackbox)
        {
          uiBlackboxInspectWindow._Close();
          uiBlackboxInspectWindow._Free();
        }
        
        uiBlackboxInspectWindow._Init(blackbox);
        uiBlackboxInspectWindow._Open();
        uiBlackboxInspectWindow.transform.SetAsLastSibling();
      }
    }

    public static void CreateUI()
    {
      {
        var newTex = new Texture2D(1, 1);
        var data = System.IO.File.ReadAllBytes($@"{Path.GetDirectoryName(DysonSphereProgram.Modding.Blackbox.Plugin.Path)}\icon.png");
        newTex.LoadImage(data);
        iconSprite = Sprite.Create(newTex, new Rect(0, 0, 256, 256), new Vector2(0.5f, 0.5f));
      }

      foreach (var moddedUI in moddedUIs)
      {
        if (moddedUI.GameObject != null)
          throw new System.Exception("Blackbox UI mod encountered already created objects");
        moddedUI.CreateUI();
      }
    }

    public static void DestroyUI()
    {
      foreach (var moddedUI in moddedUIs)
      {
        moddedUI.DestroyUI();
      }
    }

    public static void Free()
    {
      foreach (var moddedUI in moddedUIs)
      {
        moddedUI.Free();
      }
    }

    public static void Update()
    {
      foreach (var moddedUI in moddedUIs)
      {
        moddedUI.Update();
      }
    }
  }
}
