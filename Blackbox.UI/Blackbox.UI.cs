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
    [HarmonyPatch(typeof(UIEntityBriefInfo), nameof(UIEntityBriefInfo.SetInfo))]
    static void UIEntityBriefInfo__SetInfo(PlanetFactory _factory, int _entityId, ref bool __runOriginal)
    {
      if (!__runOriginal)
        return;
      
      if (_factory == null || _entityId <= 0)
        return;

      ref readonly var entity = ref _factory.entityPool[_entityId];

      if (BlackboxUtils.QueryBlackboxedEntityForBlackboxId(_factory, in entity) > 0)
        __runOriginal = false;
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame.OnPlayerInspecteeChange))]
    static void UIGame__OnPlayerInspecteeChange(EObjectType objType, int objId, ref bool __runOriginal)
    {
      if (!__runOriginal)
        return;
      var factory = GameMain.mainPlayer.factory;
      if (factory == null || objType != EObjectType.Entity || objId <= 0)
        return;
      ref readonly var entity = ref factory.entityPool[objId];
      var stationId = entity.stationId;

      var blackbox = BlackboxUtils.QueryEntityForAssociatedBlackbox(factory, objId);

      if (blackbox != null)
      {
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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerControlGizmo), nameof(PlayerControlGizmo.SetMouseOverTarget), typeof(EObjectType), typeof(int), typeof(int), typeof(int))]
    static void PlayerControlGizmo__SetMouseOverTarget(PlayerControlGizmo __instance, EObjectType tarType, ref int tarId, ref bool __runOriginal)
    {
      var highlight = BlackboxManager.Instance.highlight;
      if (!__runOriginal)
        goto null_return;
      if (!(tarType == EObjectType.Entity && tarId != 0))
        goto null_return;

      var factory = __instance.player.factory;
      if (factory == null)
        goto null_return;

      var blackbox = BlackboxUtils.QueryEntityForAssociatedBlackbox(factory, tarId);
      if (blackbox == null)
        goto null_return;

      highlight.SetHoverHighlight(blackbox);
      tarId = 0;
      return;
      
      null_return:
        highlight.SetHoverHighlight(null);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIGame), nameof(UIGame._OnUpdate))]
    static void UIGame___OnUpdate()
    {
      BlackboxUIGateway.Update();

      var uiBlackboxWindowManager = BlackboxUIGateway.BlackboxManagerWindow?.Component;
      if (uiBlackboxWindowManager != null && KeyBinds.BlackboxManagerWindow.IsActive)
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
      var alreadyHighlighting = __instance.warningCounts[BlackboxHighlight.blackboxSignalId] > 0; 
      var highlight = BlackboxManager.Instance.highlight;
      if (highlight.blackboxId > 0)
      {
        var warningPool = __instance.warningPool;
        foreach (var warningId in highlight.warningIds)
        {
          ref var warning = ref warningPool[warningId];
          warning.state = 1;
          warning.signalId = BlackboxHighlight.blackboxSignalId;
          if (warning.detailId == 0)
            warning.detailId = __instance.tmpEntityPools[warning.factoryId][warning.objectId].protoId;

          __instance.warningCounts[BlackboxHighlight.blackboxSignalId]++;
        }
      }

      if (!alreadyHighlighting && __instance.warningCounts[BlackboxHighlight.blackboxSignalId] > 0)
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
        if (uiBlackboxInspectWindow.inited && uiBlackboxInspectWindow.data != blackbox)
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
