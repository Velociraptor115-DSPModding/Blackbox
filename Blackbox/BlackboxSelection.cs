using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEngine;
using HarmonyLib;

namespace DysonSphereProgram.Modding.Blackbox
{
  public class BlackboxSelection
  {
    public readonly int factoryIndex;
    public readonly WeakReference<PlanetFactory> factoryRef;
    public readonly ImmutableSortedSet<int> entityIds;
    public readonly ImmutableSortedSet<int> pcIds;
    public readonly ImmutableSortedSet<int> assemblerIds;
    public readonly ImmutableSortedSet<int> labIds;
    public readonly ImmutableSortedSet<int> inserterIds;
    public readonly ImmutableSortedSet<int> stationIds;
    public readonly ImmutableSortedSet<int> cargoPathIds;
    public readonly ImmutableSortedSet<int> splitterIds;
    public readonly ImmutableSortedSet<int> pilerIds;
    public readonly ImmutableSortedSet<int> spraycoaterIds;
    public readonly ImmutableSortedSet<int> monitorIds;
    public readonly ImmutableSortedSet<int> fractionatorIds;
    public readonly ImmutableSortedSet<int> itemIds;

    private BlackboxSelection(
          PlanetFactory factory
        , ImmutableSortedSet<int> entityIds
        , ImmutableSortedSet<int> pcIds
        , ImmutableSortedSet<int> assemblerIds
        , ImmutableSortedSet<int> labIds
        , ImmutableSortedSet<int> inserterIds
        , ImmutableSortedSet<int> stationIds
        , ImmutableSortedSet<int> cargoPathIds
        , ImmutableSortedSet<int> splitterIds
        , ImmutableSortedSet<int> pilerIds
        , ImmutableSortedSet<int> spraycoaterIds
        , ImmutableSortedSet<int> monitorIds
        , ImmutableSortedSet<int> fractionatorIds
        , ImmutableSortedSet<int> itemIds
      )
    {
      this.factoryIndex = factory.index;
      this.factoryRef = new WeakReference<PlanetFactory>(factory);
      this.entityIds = entityIds;
      this.pcIds = pcIds;
      this.assemblerIds = assemblerIds;
      this.labIds = labIds;
      this.inserterIds = inserterIds;
      this.stationIds = stationIds;
      this.cargoPathIds = cargoPathIds;
      this.splitterIds = splitterIds;
      this.pilerIds = pilerIds;
      this.spraycoaterIds = spraycoaterIds;
      this.monitorIds = monitorIds;
      this.fractionatorIds = fractionatorIds;
      this.itemIds = itemIds;
    }

    public static BlackboxSelection CreateFrom(PlanetFactory factory, ICollection<int> entityIds)
    {
      var tmp_stationIds = new List<int>();
      var tmp_assemblerIds = new List<int>();
      //var tmp_assemblerTimeSpends = new List<int>();
      var tmp_inserterIds = new List<int>();
      var tmp_labIds = new List<int>();
      var tmp_pcIds = new List<int>();
      var tmp_itemIds = new HashSet<int>();
      var tmp_cargoPathIds = new HashSet<int>();
      var tmp_splitterIds = new List<int>();
      var tmp_pilerIds = new List<int>();
      var tmp_spraycoaterIds = new List<int>();
      var tmp_monitorIds = new List<int>();
      var tmp_fractionatorIds = new List<int>();

      foreach (var entityId in entityIds)
      {
        if (entityId < 0)
          continue;
        ref readonly var entity = ref factory.entityPool[entityId];
        if (entity.stationId > 0)
        {
          tmp_stationIds.Add(entity.stationId);
          ref readonly var station = ref factory.transport.stationPool[entity.stationId];
          for (int j = 0; j < station.storage.Length; j++)
          {
            var stationStorage = station.storage[j];
            var effectiveLogic = stationStorage.remoteLogic == ELogisticStorage.None ? stationStorage.localLogic : stationStorage.remoteLogic;
            if (effectiveLogic != ELogisticStorage.None && stationStorage.itemId > 0)
              tmp_itemIds.Add(stationStorage.itemId);
          }
        }
        if (entity.assemblerId > 0)
        {
          tmp_assemblerIds.Add(entity.assemblerId);
          ref readonly var assembler = ref factory.factorySystem.assemblerPool[entity.assemblerId];
          if (assembler.recipeId > 0)
          {
            //tmp_assemblerTimeSpends.Add(assembler.timeSpend / assembler.speed);
            for (int i = 0; i < assembler.requires.Length; i++)
              tmp_itemIds.Add(assembler.requires[i]);
            for (int i = 0; i < assembler.products.Length; i++)
              tmp_itemIds.Add(assembler.products[i]);
            tmp_pcIds.Add(entity.powerConId);
          }
        }
        if (entity.inserterId > 0)
        {
          tmp_inserterIds.Add(entity.inserterId);
          tmp_pcIds.Add(entity.powerConId);
        }
        if (entity.labId > 0)
        {
          ref readonly var lab = ref factory.factorySystem.labPool[entity.labId];
          if (lab.matrixMode && lab.recipeId > 0)
          {
            tmp_labIds.Add(entity.labId);
            //tmp_assemblerTimeSpends.Add(lab.timeSpend / 10000);
            for (int i = 0; i < lab.requires.Length; i++)
              tmp_itemIds.Add(lab.requires[i]);
            for (int i = 0; i < lab.products.Length; i++)
              tmp_itemIds.Add(lab.products[i]);
            tmp_pcIds.Add(entity.powerConId);
          }
        }
        if (entity.beltId > 0)
        {
          ref readonly var belt = ref factory.cargoTraffic.beltPool[entity.beltId];
          tmp_cargoPathIds.Add(belt.segPathId);
        }
        if (entity.splitterId > 0)
        {
          tmp_splitterIds.Add(entity.splitterId);
        }
        if (entity.pilerId > 0)
        {
          tmp_pilerIds.Add(entity.pilerId);
          tmp_pcIds.Add(entity.powerConId);
        }
        if (entity.spraycoaterId > 0)
        {
          tmp_spraycoaterIds.Add(entity.spraycoaterId);
          tmp_pcIds.Add(entity.powerConId);
        }
        if (entity.monitorId > 0)
        {
          tmp_monitorIds.Add(entity.monitorId);
          tmp_pcIds.Add(entity.powerConId);
        }
        if (entity.fractionatorId > 0)
        {
          tmp_fractionatorIds.Add(entity.fractionatorId);
          tmp_pcIds.Add(entity.powerConId);
        }
      }

      var pcIds = tmp_pcIds.ToImmutableSortedSet();
      var assemblerIds = tmp_assemblerIds.ToImmutableSortedSet();
      var stationIds = tmp_stationIds.ToImmutableSortedSet();
      var inserterIds = tmp_inserterIds.ToImmutableSortedSet();
      var labIds = tmp_labIds.ToImmutableSortedSet();
      var itemIds = tmp_itemIds.ToImmutableSortedSet();
      var cargoPathIds = tmp_cargoPathIds.ToImmutableSortedSet();
      var splitterIds = tmp_splitterIds.ToImmutableSortedSet();
      var pilerIds = tmp_pilerIds.ToImmutableSortedSet();
      var spraycoaterIds = tmp_spraycoaterIds.ToImmutableSortedSet();
      var monitorIds = tmp_monitorIds.ToImmutableSortedSet();
      var fractionatorIds = tmp_fractionatorIds.ToImmutableSortedSet();
      var entityIdsSet = entityIds.ToImmutableSortedSet();

      return new BlackboxSelection(
            factory
          , entityIdsSet
          , pcIds
          , assemblerIds
          , labIds
          , inserterIds
          , stationIds
          , cargoPathIds
          , splitterIds
          , pilerIds
          , spraycoaterIds
          , monitorIds
          , fractionatorIds
          , itemIds
        );
    }

    public static BlackboxSelection Expand(BlackboxSelection selection)
    {
      if (!selection.factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under " + nameof(BlackboxSelection) + " in " + nameof(Expand));
        return selection;
      }

      var entityPool = factory.entityPool;
      var stationPool = factory.transport.stationPool;
      var beltPool = factory.cargoTraffic.beltPool;

      var yetToExpandEntityIds = new Queue<int>();
      var expandedEntityIds = ImmutableSortedSet.CreateBuilder<int>();

      var stations =
        from x in selection.stationIds
        select factory.transport.stationPool[x]
        ;

      foreach (var station in stations)
        yetToExpandEntityIds.Enqueue(station.entityId);

      BFS(yetToExpandEntityIds, expandedEntityIds, factory);

      var invalidEntityIds = new HashSet<int>();
      var stationEntityIds = new HashSet<int>();
      var expandedStations = new HashSet<StationComponent>();
      foreach (var entityId in expandedEntityIds)
      {
        if (IsInvalid(entityId, factory))
          invalidEntityIds.Add(entityId);
        ref readonly var entity = ref entityPool[entityId];
        if (entity.stationId > 0)
        {
          stationEntityIds.Add(entityId);
          expandedStations.Add(stationPool[entity.stationId]);
        }
      }

      // Remove invalid entities from the expanded selection 
      var yetToExpandInvalidEntityIds = new Queue<int>();
      var expandedInvalidEntityIds = new HashSet<int>();
      while (invalidEntityIds.Count != 0)
      {
        var invalidEntityId = invalidEntityIds.First();
        
        yetToExpandInvalidEntityIds.Clear();
        yetToExpandInvalidEntityIds.Enqueue(invalidEntityId);
        
        expandedInvalidEntityIds.Clear();
        expandedInvalidEntityIds.UnionWith(stationEntityIds);
        BFS(yetToExpandInvalidEntityIds, expandedInvalidEntityIds, factory);
        expandedInvalidEntityIds.ExceptWith(stationEntityIds);
        
        expandedEntityIds.ExceptWith(expandedInvalidEntityIds);
        invalidEntityIds.ExceptWith(expandedInvalidEntityIds);
      }

      const int warperItemId = 1210;

      // Remove warper belts
      foreach (var station in expandedStations)
      {
        foreach (var slot in station.slots)
        {
          if (slot.storageIdx <= 0 || slot.dir != IODir.Output || slot.beltId <= 0)
            continue;
          var storageIdx = slot.storageIdx - 1;
          var itemId = storageIdx < station.storage.Length ? station.storage[storageIdx].itemId : warperItemId;
          if (itemId != warperItemId)
            continue;

          var beltEntityId = beltPool[slot.beltId].entityId;

          yetToExpandInvalidEntityIds.Clear();
          yetToExpandInvalidEntityIds.Enqueue(beltEntityId);

          expandedInvalidEntityIds.Clear();
          expandedInvalidEntityIds.UnionWith(stationEntityIds);
          BFS(yetToExpandInvalidEntityIds, expandedInvalidEntityIds, factory);
          expandedInvalidEntityIds.ExceptWith(stationEntityIds);

          expandedEntityIds.ExceptWith(expandedInvalidEntityIds);
        }
      }

      return BlackboxSelection.CreateFrom(factory, expandedEntityIds);
    }

    private static void BFS(Queue<int> yetToExpandEntityIds, ISet<int> expandedEntityIds, PlanetFactory factory)
    {
      while (yetToExpandEntityIds.Count != 0)
      {
        var entityIdToExpand = yetToExpandEntityIds.Dequeue();
        expandedEntityIds.Add(entityIdToExpand);

        for (int i = 0; i < 16; i++)
        {
          factory.ReadObjectConn(entityIdToExpand, i, out _, out var otherEntityId, out _);
          if (otherEntityId == 0)
            continue;
          if (!expandedEntityIds.Contains(otherEntityId) && !yetToExpandEntityIds.Contains(otherEntityId))
            yetToExpandEntityIds.Enqueue(otherEntityId);
        }
      }
    }

    public static bool IsInvalid(BlackboxSelection selection)
    {
      if (!selection.factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under " + nameof(BlackboxSelection) + " in " + nameof(IsInvalid));
        return true;
      }

      foreach (var entityId in selection.entityIds)
      {
        if (IsInvalid(entityId, factory))
          return true;
      }

      if (selection.stationIds.Count == 0)
        return true;
      if (selection.assemblerIds.Count == 0 && selection.labIds.Count == 0 && selection.fractionatorIds.Count == 0)
        return true;

      return false;
    }

    private static bool IsInvalid(int entityId, PlanetFactory factory)
    {
      if (entityId < 0)
        return true;

      ref readonly var entity = ref factory.entityPool[entityId];

      if (entity.minerId > 0)
        return true;

      if (entity.beltId > 0)
        return false;

      if (entity.splitterId > 0)
        return false;
      
      if (entity.pilerId > 0)
        return false;
      
      if (entity.spraycoaterId > 0)
        return false;

      if (entity.monitorId > 0)
        return false;

      if (entity.fractionatorId > 0)
        return false;

      if (entity.inserterId > 0)
        return false;

      if (entity.assemblerId > 0)
        return false;

      if (entity.labId > 0)
      {
        ref readonly var lab = ref factory.factorySystem.labPool[entity.labId];
        if (!lab.researchMode)
          return false;
      }

      if (entity.stationId > 0)
        return false;

      return true;
    }

    const int saveLogicVersion = 1;

    public void Export(BinaryWriter w)
    {
      w.Write(saveLogicVersion);
      w.Write(factoryIndex);
      w.Write(entityIds.Count);
      for (int i = 0; i < entityIds.Count; i++)
        w.Write(entityIds[i]);
    }

    public static BlackboxSelection Import(BinaryReader r)
    {
      var saveLogicVersion = r.ReadInt32();
      var factoryIndex = r.ReadInt32();
      var entityIdsCount = r.ReadInt32();
      var entityIds = new int[entityIdsCount];
      for (int i = 0; i < entityIdsCount; i++)
      {
        entityIds[i] = r.ReadInt32();
      }
      var factory = GameMain.data.factories[factoryIndex];
      return BlackboxSelection.CreateFrom(factory, entityIds);
    }
  }
}