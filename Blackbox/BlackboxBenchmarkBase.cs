using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HarmonyLib;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace DysonSphereProgram.Modding.Blackbox
{
  public abstract class BlackboxBenchmarkBase : BlackboxAnalysis
  {
    protected BlackboxBenchmarkBase(Blackbox blackbox) : base(blackbox)
    {
    }

    public readonly int[] productRegister = new int[12000];
    public readonly int[] consumeRegister = new int[12000];

    public void BeginGameTick()
    {
      Array.Clear(productRegister, 0, 12000);
      Array.Clear(consumeRegister, 0, 12000);
    }

    public virtual void EndGameTick() { }

    public virtual void LogPowerConsumer() { }

    public virtual void LogStationBefore() { }

    public virtual void LogStationAfter() { }

    public virtual void DoInserterAdaptiveStacking() { }

    public virtual bool ShouldInterceptAssembler(FactorySystem factorySystem, int assemblerId)
    {
      return false;
    }

    public virtual bool ShouldInterceptLab(FactorySystem factorySystem, int labId)
    {
      return false;
    }
    
    public virtual bool ShouldInterceptSpraycoater(CargoTraffic cargoTraffic, int spraycoaterId)
    {
      return false;
    }
  }

  public class BlackboxGatewayMethods
  {
    private static List<BlackboxBenchmarkBase> benchmarks = new List<BlackboxBenchmarkBase>();

    public static void GameTick_AfterPowerConsumerComponents(PlanetFactory factory)
    {
      if (DSPGame.IsMenuDemo)
        return;
      
      foreach (var benchmark in benchmarks)
      {
        if (benchmark.blackbox.Status != BlackboxStatus.InAnalysis)
          continue;
        
        if (!benchmark.factoryRef.TryGetTarget(out var benchmarkFactory))
        {
          Plugin.Log.LogError("PlanetFactory instance pulled out from under " + nameof(BlackboxBenchmark) + " in " + nameof(GameTick_AfterPowerConsumerComponents));
          continue;
        }

        if (benchmarkFactory == factory)
        {
          benchmark.LogPowerConsumer();
          //Debug.Log("Setting up initial values");
        }
      }
    }

    public static void GameTick_AfterFactorySystem(PlanetFactory factory)
    {
      if (DSPGame.IsMenuDemo)
        return;
      
      foreach (var benchmark in benchmarks)
      {
        if (benchmark.blackbox.Status != BlackboxStatus.InAnalysis)
          continue;
        
        if (!benchmark.factoryRef.TryGetTarget(out var benchmarkFactory))
        {
          Plugin.Log.LogError("PlanetFactory instance pulled out from under " + nameof(BlackboxBenchmark) + " in " + nameof(GameTick_AfterFactorySystem));
          continue;
        }

        if (benchmarkFactory == factory)
        {
          benchmark.LogStationBefore();
          //Debug.Log("Noting production and consumption");
        }
      }
    }

    public static void GameTick_AfterStationBeltOutput(PlanetFactory factory)
    {
      if (DSPGame.IsMenuDemo)
        return;
      
      foreach (var benchmark in benchmarks)
      {
        if (benchmark.blackbox.Status != BlackboxStatus.InAnalysis)
          continue;
        
        if (!benchmark.factoryRef.TryGetTarget(out var benchmarkFactory))
        {
          Plugin.Log.LogError("PlanetFactory instance pulled out from under " + nameof(BlackboxBenchmark) + " in " + nameof(GameTick_AfterStationBeltOutput));
          continue;
        }

        if (benchmarkFactory == factory)
        {
          benchmark.LogStationAfter();
          benchmark.DoInserterAdaptiveStacking();
        }
      }
    }
    
    public static void GameTick_Begin()
    {
      benchmarks = (
          from x in BlackboxManager.Instance.blackboxes
          where (x.Status == BlackboxStatus.InAnalysis && !x.analyseInBackground && x.Analysis is BlackboxBenchmarkBase)
          select x.Analysis as BlackboxBenchmarkBase
        ).ToList();

      foreach (var benchmark in benchmarks)
      {
        benchmark.BeginGameTick();
      }
    }

    public static void GameTick_End()
    {
      foreach (var benchmark in benchmarks)
      {
        if (benchmark.blackbox.Status != BlackboxStatus.InAnalysis)
          continue;
        benchmark.EndGameTick();
      }
    }

    public static uint InterceptAssemblerInternalUpdate
      (FactorySystem factorySystem, int assemblerId, float power, int[] productRegister, int[] consumeRegister)
    {
      foreach (var benchmark in benchmarks)
      {
        if (benchmark.blackbox.Status != BlackboxStatus.InAnalysis)
          continue;
        if (!benchmark.ShouldInterceptAssembler(factorySystem, assemblerId))
          continue;

        ref var assembler = ref factorySystem.assemblerPool[assemblerId];
        return PlanetFactorySimulation.IsAssemblerStacking(ref assembler) ? 0 : assembler.InternalUpdate(power, benchmark.productRegister, benchmark.consumeRegister);
      }
      
      return factorySystem.assemblerPool[assemblerId].InternalUpdate(power, productRegister, consumeRegister);
    }
    
    public static uint InterceptLabInternalUpdateAssemble
      (FactorySystem factorySystem, int labId, float power, int[] productRegister, int[] consumeRegister)
    {
      foreach (var benchmark in benchmarks)
      {
        if (benchmark.blackbox.Status != BlackboxStatus.InAnalysis)
          continue;
        if (!benchmark.ShouldInterceptLab(factorySystem, labId))
          continue;
        
        return factorySystem.labPool[labId].InternalUpdateAssemble(power, benchmark.productRegister, benchmark.consumeRegister);
      }
      
      return factorySystem.labPool[labId].InternalUpdateAssemble(power, productRegister, consumeRegister);
    }
    
    public static void InterceptSpraycoaterInternalUpdate
      (CargoTraffic cargoTraffic, int spraycoaterId, AnimData[] animPool, int[] consumeRegister)
    {
      foreach (var benchmark in benchmarks)
      {
        if (benchmark.blackbox.Status != BlackboxStatus.InAnalysis)
          continue;
        if (!benchmark.ShouldInterceptSpraycoater(cargoTraffic, spraycoaterId))
          continue;
        
        cargoTraffic.spraycoaterPool[spraycoaterId].InternalUpdate(cargoTraffic, animPool, benchmark.consumeRegister);
        return;
      }
      
      cargoTraffic.spraycoaterPool[spraycoaterId].InternalUpdate(cargoTraffic, animPool, consumeRegister);
    }
  }

  [HarmonyPatch]
  class BlackboxBenchmarkPatch
  {
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTickBeforePower))]
    public static void FactorySystem__GameTickBeforePower(FactorySystem __instance)
    {
      BlackboxGatewayMethods.GameTick_AfterPowerConsumerComponents(__instance.factory);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MultithreadSystem), nameof(MultithreadSystem.PreparePowerSystemFactoryData))]
    public static void MultithreadSystem__PreparePowerSystemFactoryData(PlanetFactory[] _factories)
    {
      foreach (var factory in _factories)
        BlackboxGatewayMethods.GameTick_AfterPowerConsumerComponents(factory);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTickLabProduceMode), typeof(long), typeof(bool))]
    public static void FactorySystem__GameTickLabProduceMode(FactorySystem __instance)
    {
      BlackboxGatewayMethods.GameTick_AfterFactorySystem(__instance.factory);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MultithreadSystem), nameof(MultithreadSystem.PrepareLabOutput2NextData))]
    public static void MultithreadSystem__PrepareLabOutput2NextData(PlanetFactory[] _factories)
    {
      foreach (var factory in _factories)
        BlackboxGatewayMethods.GameTick_AfterFactorySystem(factory);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlanetTransport), nameof(PlanetTransport.GameTick_OutputToBelt))]
    public static void PlanetTransport__GameTick_OutputToBelt(PlanetTransport __instance)
    {
      BlackboxGatewayMethods.GameTick_AfterStationBeltOutput(__instance.factory);
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameStatData), nameof(GameStatData.PrepareTick))]
    public static void GameStatData_PrepareTick()
    {
      BlackboxGatewayMethods.GameTick_Begin();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameStatData), nameof(GameStatData.GameTick))]
    public static void GameStatData__GameTick()
    {
      BlackboxGatewayMethods.GameTick_End();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTick), typeof(long), typeof(bool))]
    [HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTick), typeof(long), typeof(bool), typeof(int), typeof(int), typeof(int))]
    static IEnumerable<CodeInstruction> 
      InterceptAssemblerInternalUpdatePatch(IEnumerable<CodeInstruction> code, ILGenerator generator)
    {
      var matcher = new CodeMatcher(code, generator);

      matcher.MatchForward(false
        , new CodeMatch(OpCodes.Ldarg_0)
        , new CodeMatch(ci => ci.LoadsField(AccessTools.Field(typeof(FactorySystem), nameof(FactorySystem.assemblerPool))))
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(OpCodes.Ldelema)
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(ci => ci.Calls(AccessTools.Method(typeof(AssemblerComponent), nameof(AssemblerComponent.InternalUpdate))))
      );

      matcher.Advance(1);
      Plugin.Log.LogDebug(matcher.Opcode);
      matcher.RemoveInstruction();
      Plugin.Log.LogDebug(matcher.Opcode);
      matcher.Advance(1);
      Plugin.Log.LogDebug(matcher.Opcode);
      matcher.RemoveInstruction();
      Plugin.Log.LogDebug(matcher.Opcode);

      matcher.Advance(3);
      Plugin.Log.LogDebug(matcher.Opcode);

      matcher.SetOperandAndAdvance(AccessTools.Method(typeof(BlackboxGatewayMethods),
        nameof(BlackboxGatewayMethods.InterceptAssemblerInternalUpdate)));

      return matcher.InstructionEnumeration();
    }
    
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTickLabProduceMode), typeof(long), typeof(bool))]
    [HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTickLabProduceMode), typeof(long), typeof(bool), typeof(int), typeof(int), typeof(int))]
    static IEnumerable<CodeInstruction> 
      InterceptLabInternalUpdateAssemblePatch(IEnumerable<CodeInstruction> code, ILGenerator generator)
    {
      var matcher = new CodeMatcher(code, generator);

      matcher.MatchForward(false
        , new CodeMatch(OpCodes.Ldarg_0)
        , new CodeMatch(ci => ci.LoadsField(AccessTools.Field(typeof(FactorySystem), nameof(FactorySystem.labPool))))
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(OpCodes.Ldelema)
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(ci => ci.Calls(AccessTools.Method(typeof(LabComponent), nameof(LabComponent.InternalUpdateAssemble))))
      );

      matcher.Advance(1);
      Plugin.Log.LogDebug(matcher.Opcode);
      matcher.RemoveInstruction();
      Plugin.Log.LogDebug(matcher.Opcode);
      matcher.Advance(1);
      Plugin.Log.LogDebug(matcher.Opcode);
      matcher.RemoveInstruction();
      Plugin.Log.LogDebug(matcher.Opcode);

      matcher.Advance(3);
      Plugin.Log.LogDebug(matcher.Opcode);

      matcher.SetOperandAndAdvance(AccessTools.Method(typeof(BlackboxGatewayMethods),
        nameof(BlackboxGatewayMethods.InterceptLabInternalUpdateAssemble)));

      return matcher.InstructionEnumeration();
    }
    
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.SpraycoaterGameTick))]
    static IEnumerable<CodeInstruction> 
      InterceptSpraycoaterInternalUpdatePatch(IEnumerable<CodeInstruction> code, ILGenerator generator)
    {
      var matcher = new CodeMatcher(code, generator);

      matcher.MatchForward(false
        , new CodeMatch(OpCodes.Ldarg_0)
        , new CodeMatch(ci => ci.LoadsField(AccessTools.Field(typeof(CargoTraffic), nameof(CargoTraffic.spraycoaterPool))))
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(OpCodes.Ldelema)
        , new CodeMatch(ci => ci.IsLdarg())
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(ci => ci.IsLdloc())
        , new CodeMatch(ci => ci.Calls(AccessTools.Method(typeof(SpraycoaterComponent), nameof(SpraycoaterComponent.InternalUpdate))))
      );

      matcher.Advance(1);
      Plugin.Log.LogDebug(matcher.Opcode);
      matcher.RemoveInstruction();
      Plugin.Log.LogDebug(matcher.Opcode);
      matcher.Advance(1);
      Plugin.Log.LogDebug(matcher.Opcode);
      matcher.RemoveInstruction();
      matcher.RemoveInstruction();
      Plugin.Log.LogDebug(matcher.Opcode);

      matcher.Advance(2);
      Plugin.Log.LogDebug(matcher.Opcode);

      matcher.SetOperandAndAdvance(AccessTools.Method(typeof(BlackboxGatewayMethods),
        nameof(BlackboxGatewayMethods.InterceptSpraycoaterInternalUpdate)));

      return matcher.InstructionEnumeration();
    }
  }
}