using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using HarmonyLib;

namespace DysonSphereProgram.Modding.Blackbox
{ 
  public class BlackboxSimulation
  {
    private Blackbox blackbox;
    private WeakReference<PlanetFactory> factoryRef;

    public BlackboxSimulation(Blackbox blackbox)
    {
      this.blackbox = blackbox;
      this.factoryRef = blackbox.FactoryRef;
    }

    Dictionary<int, Dictionary<int, CNT>> parsedInputs;
    Dictionary<int, Dictionary<int, CNTINC>> parsedOutputs;

    Dictionary<int, Dictionary<int, CNT>> bufferInputs;
    Dictionary<int, Dictionary<int, CNTINC>> bufferOutputs;

    long[] idleEnergyRestore;
    long[] workEnergyRestore;
    long[] requiredEnergyRestore;

    int timeIdx = 0;
    public bool isBlackboxSimulating = false;
    bool isWorking = false;

    const bool continuousStats = true;

    public float CycleProgress => timeIdx / (float)(blackbox.Recipe.timeSpend - 1);
    public string CycleProgressText => $"{timeIdx} / {blackbox.Recipe.timeSpend}";

    public void CreateBlackboxingResources()
    {
      if (!factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under a blackbox simulation in " + nameof(CreateBlackboxingResources));
        return;
      }

      var pcCount = blackbox.Selection.pcIds.Count;
      idleEnergyRestore = new long[pcCount];
      workEnergyRestore = new long[pcCount];
      requiredEnergyRestore = new long[pcCount];

      parsedInputs = new Dictionary<int, Dictionary<int, CNT>>();
      parsedOutputs = new Dictionary<int, Dictionary<int, CNTINC>>();
      bufferInputs = new Dictionary<int, Dictionary<int, CNT>>();
      bufferOutputs = new Dictionary<int, Dictionary<int, CNTINC>>();

      // WARNING: This will break once Fingerprint (and thereby Recipe) is selection-independent
      // TODO: Figure out a proper way to relate Selection, Fingerprint and Recipe
      var stationIds = blackbox.Selection.stationIds;

      foreach (var station in blackbox.Recipe.inputs)
      {
        
        var stationId = stationIds[station.Key];
        if (!parsedInputs.ContainsKey(stationId))
        {
          parsedInputs[stationId] = new Dictionary<int, CNT>();
          bufferInputs[stationId] = new Dictionary<int, CNT>();
        }
        var stationStorage = factory.transport.stationPool[stationId].storage;
        foreach (var stationItemProduction in station.Value)
        {
          for (int j = 0; j < stationStorage.Length; j++)
            if (stationStorage[j].itemId == stationItemProduction.Key)
            {
              parsedInputs[stationId][j] = stationItemProduction.Value;
              bufferInputs[stationId][j] = new CNT { count = 0 };
              break;
            }
        }
      }

      foreach (var station in blackbox.Recipe.outputs)
      {
        var stationId = stationIds[station.Key];
        if (!parsedOutputs.ContainsKey(stationId))
        {
          parsedOutputs[stationId] = new Dictionary<int, CNTINC>();
          bufferOutputs[stationId] = new Dictionary<int, CNTINC>();
        }
        var stationStorage = factory.transport.stationPool[stationId].storage;
        foreach (var stationItemRequirement in station.Value)
        {
          for (int j = 0; j < stationStorage.Length; j++)
            if (stationStorage[j].itemId == stationItemRequirement.Key)
            {
              parsedOutputs[stationId][j] = stationItemRequirement.Value;
              bufferOutputs[stationId][j] = new CNTINC { count = 0, inc = 0 };
              break;
            }
        }
      }

      isWorking = false;
      timeIdx = 0;
    }

    public void ReleaseBlackboxingResources()
    {
      parsedInputs = null;
      parsedOutputs = null;
      bufferInputs = null;
      bufferOutputs = null;

      idleEnergyRestore = null;
      workEnergyRestore = null;
      requiredEnergyRestore = null;
    }

    public void ResumeBelts()
    {
      if (!factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under a blackbox simulation in " + nameof(ResumeBelts));
        return;
      }
      
      var cargoPathIds = blackbox.Selection.cargoPathIds;
      
      for (int i = 0; i < cargoPathIds.Count; i++)
      {
        var cargoPath = factory.cargoTraffic.pathPool[cargoPathIds[i]];
        cargoPath.id = -blackbox.Id;
        for (int j = 0; j < cargoPath.belts.Count; j++)
        {
          var beltId = cargoPath.belts[j];
          factory.cargoTraffic.beltPool[beltId].id = -blackbox.Id;
        }
      }
    }

    public void ResumeBlackboxing()
    {
      if (!factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under a blackbox simulation in " + nameof(ResumeBlackboxing));
        return;
      }

      var pcIds = blackbox.Selection.pcIds;
      var assemblerIds = blackbox.Selection.assemblerIds;
      var labIds = blackbox.Selection.labIds;
      var inserterIds = blackbox.Selection.inserterIds;
      var splitterIds = blackbox.Selection.splitterIds;
      var spraycoaterIds = blackbox.Selection.spraycoaterIds;
      var pilerIds = blackbox.Selection.pilerIds;
      var monitorIds = blackbox.Selection.monitorIds;
      var fractionatorIds = blackbox.Selection.fractionatorIds;

      for (int i = 0; i < pcIds.Count; i++)
      {
        idleEnergyRestore[i] = factory.powerSystem.consumerPool[pcIds[i]].idleEnergyPerTick;
        workEnergyRestore[i] = factory.powerSystem.consumerPool[pcIds[i]].workEnergyPerTick;
        requiredEnergyRestore[i] = factory.powerSystem.consumerPool[pcIds[i]].requiredEnergy;
        factory.powerSystem.consumerPool[pcIds[i]].idleEnergyPerTick = 0;
        factory.powerSystem.consumerPool[pcIds[i]].workEnergyPerTick = 0;
        factory.powerSystem.consumerPool[pcIds[i]].SetRequiredEnergy(false);
      }
      factory.powerSystem.consumerPool[pcIds[0]].idleEnergyPerTick = blackbox.Recipe.idleEnergyPerTick;
      factory.powerSystem.consumerPool[pcIds[0]].workEnergyPerTick = blackbox.Recipe.workingEnergyPerTick;

      for (int i = 0; i < assemblerIds.Count; i++)
      {
        factory.factorySystem.assemblerPool[assemblerIds[i]].id = -blackbox.Id;
      }
      for (int i = 0; i < labIds.Count; i++)
      {
        factory.factorySystem.labPool[labIds[i]].id = -blackbox.Id;
      }
      for (int i = 0; i < inserterIds.Count; i++)
      {
        factory.factorySystem.inserterPool[inserterIds[i]].id = -blackbox.Id;
      }
      ResumeBelts();
      for (int i = 0; i < splitterIds.Count; i++)
      {
        factory.cargoTraffic.splitterPool[splitterIds[i]].id = -blackbox.Id;
      }
      for (int i = 0; i < spraycoaterIds.Count; i++)
      {
        factory.cargoTraffic.spraycoaterPool[spraycoaterIds[i]].id = -blackbox.Id;
      }
      for (int i = 0; i < pilerIds.Count; i++)
      {
        factory.cargoTraffic.pilerPool[pilerIds[i]].id = -blackbox.Id;
      }
      for (int i = 0; i < monitorIds.Count; i++)
      {
        factory.cargoTraffic.monitorPool[monitorIds[i]].id = -blackbox.Id;
      }
      for (int i = 0; i < fractionatorIds.Count; i++)
      {
        factory.factorySystem.fractionatorPool[fractionatorIds[i]].id = -blackbox.Id;
      }

      isBlackboxSimulating = true;
    }

    public void PauseBelts()
    {
      if (!factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under a blackbox simulation in " + nameof(PauseBelts));
        return;
      }
      
      var cargoPathIds = blackbox.Selection.cargoPathIds;
      
      for (int i = 0; i < cargoPathIds.Count; i++)
      {
        var cargoPath = factory.cargoTraffic.pathPool[cargoPathIds[i]];
        cargoPath.id = cargoPathIds[i];
        for (int j = 0; j < cargoPath.belts.Count; j++)
        {
          var beltId = cargoPath.belts[j];
          factory.cargoTraffic.beltPool[beltId].id = beltId;
        }
      }
    }

    public void PauseBlackboxing()
    {
      if (!factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under a blackbox simulation in " + nameof(PauseBlackboxing));
        return;
      }

      var pcIds = blackbox.Selection.pcIds;
      var assemblerIds = blackbox.Selection.assemblerIds;
      var labIds = blackbox.Selection.labIds;
      var inserterIds = blackbox.Selection.inserterIds;
      var splitterIds = blackbox.Selection.splitterIds;
      var spraycoaterIds = blackbox.Selection.spraycoaterIds;
      var pilerIds = blackbox.Selection.pilerIds;
      var monitorIds = blackbox.Selection.monitorIds;
      var fractionatorIds = blackbox.Selection.fractionatorIds;

      for (int i = 0; i < pcIds.Count; i++)
      {
        factory.powerSystem.consumerPool[pcIds[i]].idleEnergyPerTick = idleEnergyRestore[i];
        factory.powerSystem.consumerPool[pcIds[i]].workEnergyPerTick = workEnergyRestore[i];
        factory.powerSystem.consumerPool[pcIds[i]].requiredEnergy = requiredEnergyRestore[i];
      }

      for (int i = 0; i < assemblerIds.Count; i++)
      {
        factory.factorySystem.assemblerPool[assemblerIds[i]].id = assemblerIds[i];
      }
      for (int i = 0; i < labIds.Count; i++)
      {
        factory.factorySystem.labPool[labIds[i]].id = labIds[i];
      }
      for (int i = 0; i < inserterIds.Count; i++)
      {
        factory.factorySystem.inserterPool[inserterIds[i]].id = inserterIds[i];
      }
      PauseBelts();
      for (int i = 0; i < splitterIds.Count; i++)
      {
        factory.cargoTraffic.splitterPool[splitterIds[i]].id = splitterIds[i];
      }
      for (int i = 0; i < spraycoaterIds.Count; i++)
      {
        factory.cargoTraffic.spraycoaterPool[spraycoaterIds[i]].id = spraycoaterIds[i];
      }
      for (int i = 0; i < pilerIds.Count; i++)
      {
        factory.cargoTraffic.pilerPool[pilerIds[i]].id = pilerIds[i];
      }
      for (int i = 0; i < monitorIds.Count; i++)
      {
        factory.cargoTraffic.monitorPool[monitorIds[i]].id = monitorIds[i];
      }
      for (int i = 0; i < fractionatorIds.Count; i++)
      {
        factory.factorySystem.fractionatorPool[fractionatorIds[i]].id = fractionatorIds[i];
      }

      isBlackboxSimulating = false;
    }

    void TakeBackUnusedItems()
    {
      if (!factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under a blackbox simulation in " + nameof(TakeBackUnusedItems));
        return;
      }

      var stationPool = factory.transport.stationPool;
      var totalTimeSpend = (float)blackbox.Recipe.timeSpend;
      var curPercent = timeIdx / totalTimeSpend;

      foreach (var (stationId, inputRequirements) in parsedInputs)
      {
        foreach (var (storageIdx, required) in inputRequirements)
        {
          ref var storage = ref stationPool[stationId].storage[storageIdx];

          if (timeIdx != 0)
          {
            var count = required.count;

            var consumedCount = (int)(curPercent * count);
            var countToReturn = count - consumedCount;

            storage.count += countToReturn;
          }

          var buffer = bufferInputs[stationId][storageIdx];
          storage.count += buffer.count;
          bufferInputs[stationId][storageIdx] = new CNT { count = 0 };
        }
      }

      foreach (var (stationId, outputProduction) in parsedOutputs)
      {
        foreach (var (storageIdx, produced) in outputProduction)
        {
          ref var storage = ref stationPool[stationId].storage[storageIdx];

          if (timeIdx != 0)
          {
            var (count, inc) = produced;

            var (_, _, producedCount, producedInc) = split_inc(count, inc, (int)(curPercent * count));

            storage.count += producedCount;
            storage.inc += producedInc;
          }

          var buffer = bufferOutputs[stationId][storageIdx];
          storage.count += buffer.count;
          storage.inc += buffer.inc;
          bufferOutputs[stationId][storageIdx] = new CNTINC { count = 0, inc = 0 };
        }
      }
    }

    public void BeginBlackboxing()
    {
      CreateBlackboxingResources();
      ResumeBlackboxing();
    }

    public void EndBlackboxing()
    {
      PauseBlackboxing();
      TakeBackUnusedItems();
      ReleaseBlackboxingResources();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int remainingCount, int remainingInc, int splitCount, int splitInc) split_inc(in int mainCount, in int mainInc, in int splitCount)
    {
      if (mainCount == 0)
        return (0, 0, 0, 0);
      int q = mainInc / mainCount;
      int r = mainInc % mainCount;
      int remainingCount = mainCount - splitCount;
      int leftOverInc = (r > remainingCount) ? r - remainingCount : 0;
      int splitInc = q * splitCount + leftOverInc;
      return (remainingCount, mainInc - splitInc, splitCount, splitInc);
    }

    public void Simulate()
    {
      if (!isBlackboxSimulating)
        return;

      if (!factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under a blackbox simulation in " + nameof(Simulate));
        return;
      }

      // Set the power consumption for the previous tick
      factory.powerSystem.consumerPool[blackbox.Selection.pcIds[0]].SetRequiredEnergy(isWorking);
      
      var insufficientInput = false;
      var outputStacking = false;
      var stationPool = factory.transport.stationPool;

      // Move to internal input buffer
      foreach (var (stationId, inputRequirement) in parsedInputs)
      {
        foreach (var (storageIdx, required) in inputRequirement)
        {
          ref var storage = ref stationPool[stationId].storage[storageIdx];
          var bufferCount = bufferInputs[stationId][storageIdx].count;
          var neededCount = required.count - bufferCount;
          var transferCount = storage.count > neededCount ? neededCount : storage.count;
          (storage.count, storage.inc, _, _) = split_inc(storage.count, storage.inc, transferCount);
          bufferInputs[stationId][storageIdx] = new CNT { count = bufferCount + transferCount };
          if (bufferInputs[stationId][storageIdx].count < required.count)
            insufficientInput = true;
        }
      }
      
      // Move to internal output buffer
      foreach (var (stationId, outputProduction) in parsedOutputs)
      {
        foreach (var (storageIdx, _) in outputProduction)
        {
          if (bufferOutputs[stationId][storageIdx].count <= 0)
            continue;
          ref var storage = ref stationPool[stationId].storage[storageIdx];
          var availableStorage = storage.max - storage.count;
          var buffer = bufferOutputs[stationId][storageIdx];
          var transferCount = buffer.count > availableStorage ? availableStorage : buffer.count;
          (buffer.count, buffer.inc, _, int transferInc) = split_inc(buffer.count, buffer.inc, transferCount);
          storage.count += transferCount;
          storage.inc += transferInc;
          bufferOutputs[stationId][storageIdx] = buffer;
          if (bufferOutputs[stationId][storageIdx].count > 0)
            outputStacking = true;
        }
      }

      if (timeIdx == 0)
      {
        // Check if we can simulate a cycle. Else return and wait till we can.
        if (insufficientInput)
        {
          isWorking = false;
          return;
        }

        // Remove items and begin the cycle
        foreach (var (stationId, inputRequirement) in parsedInputs)
        {
          foreach (var (storageIdx, _) in inputRequirement)
          {
            bufferInputs[stationId][storageIdx] = new CNT { count = 0 };
          }
        }
      }

      isWorking = true;
      var Recipe = blackbox.Recipe;

      if (timeIdx == Recipe.timeSpend - 1)
      {
        // Check if stations can handle the outputs.
        // Else don't make any progress
        if (outputStacking)
        {
          isWorking = false;
          return;
        }

        foreach (var (stationId, outputProduction) in parsedOutputs)
        {
          foreach (var (storageIdx, produced) in outputProduction)
          {
            bufferOutputs[stationId][storageIdx] = produced;
          }
        }

        if (!continuousStats)
        {
          var factoryStatPool = GameMain.data.statistics.production.factoryStatPool[factory.index];
          foreach (var production in Recipe.produces)
            factoryStatPool.productRegister[production.Key] += production.Value;
          foreach (var consumption in Recipe.consumes)
            factoryStatPool.consumeRegister[consumption.Key] += consumption.Value;
        }
      }

      if (continuousStats)
      {
        var totalTimeSpend = (float)Recipe.timeSpend;
        var curPercent = timeIdx / totalTimeSpend;
        var nextPercent = (timeIdx + 1) / totalTimeSpend;
        var factoryStatPool = GameMain.data.statistics.production.factoryStatPool[factory.index];

        foreach (var production in Recipe.produces)
        {
          var countToAdd = (int)(nextPercent * production.Value) - (int)(curPercent * production.Value);
          factoryStatPool.productRegister[production.Key] += countToAdd;
        }
        foreach (var consumption in Recipe.consumes)
        {
          var countToAdd = (int)(nextPercent * consumption.Value) - (int)(curPercent * consumption.Value);
          factoryStatPool.consumeRegister[consumption.Key] += countToAdd;
        }
      }

      timeIdx = (timeIdx + 1) % Recipe.timeSpend;
    }

    const int saveLogicVersion = 2;

    public void PreserveVanillaSaveBefore()
    {
      var wasBlackboxSimulating = isBlackboxSimulating;
      PauseBlackboxing();
      isBlackboxSimulating = wasBlackboxSimulating;
    }

    public void PreserveVanillaSaveAfter()
    {
      if (isBlackboxSimulating)
      {
        ResumeBlackboxing();
      }
    }

    public void Export(BinaryWriter w)
    {
      w.Write(saveLogicVersion);
      w.Write(isBlackboxSimulating);
      w.Write(timeIdx);
      w.Write(isWorking);
      
      w.Write(bufferInputs.Count);
      foreach (var (stationId, storages) in bufferInputs)
      {
        w.Write(stationId);
        w.Write(storages.Count);
        foreach (var (storageIdx, inputBuffer) in storages)
        {
          w.Write(storageIdx);
          w.Write(inputBuffer.count);
        }
      }
      
      w.Write(bufferOutputs.Count);
      foreach (var (stationId, storages) in bufferOutputs)
      {
        w.Write(stationId);
        w.Write(storages.Count);
        foreach (var (storageIdx, outputBuffer) in storages)
        {
          w.Write(storageIdx);
          w.Write(outputBuffer.count);
          w.Write(outputBuffer.inc);
        }
      }
    }

    public void Import(BinaryReader r)
    {
      var saveLogicVersion = r.ReadInt32();
      isBlackboxSimulating = r.ReadBoolean();
      timeIdx = r.ReadInt32();
      isWorking = r.ReadBoolean();
      if (saveLogicVersion >= 2)
      {
        var inputsCount = r.ReadInt32();
        for (int i = 0; i < inputsCount; i++)
        {
          var stationId = r.ReadInt32();
          var storagesCount = r.ReadInt32();
          for (int j = 0; j < storagesCount; j++)
          {
            var storageIdx = r.ReadInt32();
            var count = r.ReadInt32();
            bufferInputs[stationId][storageIdx] = new CNT { count = count };
          }
        }
        
        var outputsCount = r.ReadInt32();
        for (int i = 0; i < outputsCount; i++)
        {
          var stationId = r.ReadInt32();
          var storagesCount = r.ReadInt32();
          for (int j = 0; j < storagesCount; j++)
          {
            var storageIdx = r.ReadInt32();
            var count = r.ReadInt32();
            var inc = r.ReadInt32();
            bufferOutputs[stationId][storageIdx] = new CNTINC { count = count, inc = inc };
          }
        }
      }
      PreserveVanillaSaveAfter();
    }
  }
}