using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DysonSphereProgram.Modding.Blackbox
{
  public class BlackboxHighlight
  {
    public int blackboxId;
    public List<int> warningIds = new List<int>();

    public const int blackboxSignalId = 60001;

    public int hoverBlackboxId;
    private List<BoxGizmo> hoverGizmos = new();

    public void RequestHighlight(Blackbox blackbox)
    {
      if (blackbox == null)
      {
        ClearHighlight();
        return;
      }

      if (blackboxId == blackbox.Id)
        return;

      if (blackboxId > 0 && blackboxId != blackbox.Id)
        ClearHighlight();

      blackboxId = blackbox.Id;
      DoHighlight(blackbox);
    }

    public void DoHighlight(Blackbox blackbox)
    {
      if (!blackbox.FactoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under a blackbox simulation in " + nameof(DoHighlight));
        return;
      }
      var entityIds = blackbox.Selection.entityIds;

      var warningSystem = GameMain.data.warningSystem;
      var entityPool = factory.entityPool;
      foreach (var entityId in entityIds)
      {
        ref readonly var entity = ref entityPool[entityId];
        if (entity.beltId > 0 || entity.inserterId > 0)
          continue;
        var warningId = warningSystem.NewWarningData(factory.index, entityId, blackboxSignalId);
        warningIds.Add(warningId);
      }
    }

    public void StopHighlight()
    {
      var warningSystem = GameMain.data.warningSystem;
      foreach (var warningId in warningIds)
        warningSystem.RemoveWarningData(warningId);
      warningIds.Clear();
    }

    public void ClearHighlight()
    {
      StopHighlight();
      blackboxId = 0;
    }

    public void SetHoverHighlight(Blackbox blackbox)
    {
      var blackboxIdToSet = blackbox?.Id ?? 0;
      if (blackboxIdToSet == hoverBlackboxId)
      {
        DoHoverHighlightForBelts(blackbox);
        return;
      }
      hoverBlackboxId = blackboxIdToSet;
      if (blackbox == null)
      {
        foreach (var gizmo in hoverGizmos)
          if (gizmo)
            gizmo.Close();
        hoverGizmos.Clear();
      }
      else if (blackbox.FactoryRef.TryGetTarget(out var factory))
      {
        var entityPool = factory.entityPool;

        foreach (var entityId in blackbox.Selection.entityIds)
        {
          ref readonly var entity = ref entityPool[entityId];
          if (entity.inserterId > 0 || entity.beltId > 0)
            continue;

          var prefabDesc = LDB.items.Select(entity.protoId).prefabDesc;
          var gizmo = BoxGizmo.Create(entity.pos, entity.rot, prefabDesc.selectCenter, prefabDesc.selectSize);
          gizmo.multiplier = 1f;
          gizmo.alphaMultiplier = prefabDesc.selectAlpha;
          gizmo.fadeInScale = 1.3f;
          gizmo.fadeInTime = 0.05f;
          gizmo.fadeInFalloff = 0.5f;
          gizmo.fadeOutScale = 1.3f;
          gizmo.fadeOutTime = 0.05f;
          gizmo.fadeOutFalloff = 0.5f;
          gizmo.color = Color.white;
          gizmo.Open();
          hoverGizmos.Add(gizmo);
        }
        
        DoHoverHighlightForBelts(blackbox);
      }
    }

    private void DoHoverHighlightForBelts(Blackbox blackbox)
    {
      if (blackbox == null)
        return;

      if (!blackbox.FactoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under a blackbox simulation in " + nameof(DoHoverHighlightForBelts));
        return;
      }
      
      var entityPool = factory.entityPool;
      var cargoTraffic = factory.cargoTraffic;
      var beltPool = cargoTraffic.beltPool;
      var pathPool = cargoTraffic.pathPool;
      
      foreach (var entityId in blackbox.Selection.entityIds)
      {
        ref readonly var entity = ref entityPool[entityId];
        var beltId = entity.beltId;
        if (beltId <= 0)
          continue;
        ref var belt = ref beltPool[beltId];
        ref var path = ref pathPool[belt.segPathId];
        var prevBeltId = belt.id;
        var prevPathId = path.id;
        belt.id = beltId;
        path.id = belt.segPathId;
        cargoTraffic.SetBeltState(beltId, 100);
        belt.id = prevBeltId;
        path.id = prevPathId;
      }
    }

    const int saveLogicVersion = 1;

    public void PreserveVanillaSaveBefore()
    {
      StopHighlight();
    }

    public void PreserveVanillaSaveAfter()
    {
      if (blackboxId > 0)
      {
        var blackbox = BlackboxManager.Instance.blackboxes.Find(x => x.Id == blackboxId);
        if (blackbox != null)
          DoHighlight(blackbox);
      }
    }

    public void Export(BinaryWriter w)
    {
      w.Write(saveLogicVersion);
      w.Write(blackboxId);
    }

    public void Import(BinaryReader r)
    {
      var saveLogicVersion = r.ReadInt32();
      blackboxId = r.ReadInt32();
      PreserveVanillaSaveAfter();
    }
  }
}