using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEngine;
using HarmonyLib;

namespace DysonSphereProgram.Modding.Blackbox
{
  public struct CNT
  {
    public int count;
    
    public override string ToString() => count.ToString();
  }

  public struct CNTINC
  {
    public int count;
    public int inc;
    
    public override string ToString() => $"({count}, {inc})";
    
    public void Deconstruct(out int count, out int inc)
    {
      count = this.count;
      inc = this.inc;
    }
  }

  public class BlackboxRecipe
  {
    public long idleEnergyPerTick;
    public long workingEnergyPerTick;
    public int timeSpend;
    // TODO: The "StationId" here needs to be linked to the Fingerprint and then through to the Selection
    // Dictionary<StationId, Dictionary<ItemId, CountPerCycle>>
    public Dictionary<int, Dictionary<int, CNT>> inputs;
    public Dictionary<int, Dictionary<int, CNTINC>> outputs;
    // Dictionary<ItemId, CountPerCycle>
    public Dictionary<int, int> produces;
    public Dictionary<int, int> consumes;

    const int saveLogicVersion = 2;

    public void Export(BinaryWriter w)
    {
      w.Write(saveLogicVersion);
      w.Write(idleEnergyPerTick);
      w.Write(workingEnergyPerTick);
      w.Write(timeSpend);
      
      w.Write(inputs.Count);
      foreach (var stationEntry in inputs)
      {
        w.Write(stationEntry.Key);
        var itemEntries = stationEntry.Value;
        w.Write(itemEntries.Count);
        foreach (var itemEntry in itemEntries)
        {
          w.Write(itemEntry.Key);
          w.Write(itemEntry.Value.count);
        }
      }

      w.Write(outputs.Count);
      foreach (var stationEntry in outputs)
      {
        w.Write(stationEntry.Key);
        var itemEntries = stationEntry.Value;
        w.Write(itemEntries.Count);
        foreach (var itemEntry in itemEntries)
        {
          w.Write(itemEntry.Key);
          w.Write(itemEntry.Value.count);
          w.Write(itemEntry.Value.inc);
        }
      }

      w.Write(produces.Count);
      foreach (var itemEntry in produces)
      {
        w.Write(itemEntry.Key);
        w.Write(itemEntry.Value);
      }

      w.Write(consumes.Count);
      foreach (var itemEntry in consumes)
      {
        w.Write(itemEntry.Key);
        w.Write(itemEntry.Value);
      }
    }

    public static BlackboxRecipe Import(BinaryReader r)
    {
      var saveLogicVersion = r.ReadInt32();
      var recipe = new BlackboxRecipe();
      recipe.idleEnergyPerTick = r.ReadInt64();
      recipe.workingEnergyPerTick = r.ReadInt64();
      recipe.timeSpend = r.ReadInt32();

      var inputsCount = r.ReadInt32();
      recipe.inputs = new Dictionary<int, Dictionary<int, CNT>>();
      for (int i = 0; i < inputsCount; i++)
      {
        var stationEntryKey = r.ReadInt32();
        var itemEntriesCount = r.ReadInt32();
        var itemEntries = new Dictionary<int, CNT>();
        for (int j = 0; j < itemEntriesCount; j++)
        {
          var itemEntryKey = r.ReadInt32();
          var count = r.ReadInt32();
          itemEntries.Add(itemEntryKey, new CNT { count = count });
        }
        recipe.inputs.Add(stationEntryKey, itemEntries);
      }

      var outputsCount = r.ReadInt32();
      recipe.outputs = new Dictionary<int, Dictionary<int, CNTINC>>();
      for (int i = 0; i < outputsCount; i++)
      {
        var stationEntryKey = r.ReadInt32();
        var itemEntriesCount = r.ReadInt32();
        var itemEntries = new Dictionary<int, CNTINC>();
        for (int j = 0; j < itemEntriesCount; j++)
        {
          var itemEntryKey = r.ReadInt32();
          var count = r.ReadInt32();
          var inc = 0;
          if (saveLogicVersion >= 2)
            inc = r.ReadInt32();
          itemEntries.Add(itemEntryKey, new CNTINC { count = count, inc = inc });
        }
        recipe.outputs.Add(stationEntryKey, itemEntries);
      }

      var producesCount = r.ReadInt32();
      recipe.produces = new Dictionary<int, int>();
      for (int i = 0; i < producesCount; i++)
      {
        var itemEntryKey = r.ReadInt32();
        var itemEntryValue = r.ReadInt32();
        recipe.produces.Add(itemEntryKey, itemEntryValue);
      }

      var consumesCount = r.ReadInt32();
      recipe.consumes = new Dictionary<int, int>();
      for (int i = 0; i < consumesCount; i++)
      {
        var itemEntryKey = r.ReadInt32();
        var itemEntryValue = r.ReadInt32();
        recipe.consumes.Add(itemEntryKey, itemEntryValue);
      }

      return recipe;
    }
  }
}