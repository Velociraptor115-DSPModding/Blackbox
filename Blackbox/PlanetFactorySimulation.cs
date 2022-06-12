using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using HarmonyLib;
using System.Reflection.Emit;

namespace DysonSphereProgram.Modding.Blackbox
{
  internal static class PlanetFactorySimulation
  {
    internal static void ExportImport(Action<BinaryWriter> export, Action<BinaryReader> import)
    {
      using (var ms = new MemoryStream())
      {
        using (var w = new BinaryWriter(ms))
        {
          export(w);
          ms.Position = 0;
          using (var r = new BinaryReader(ms))
          {
            import(r);
          }
        }
      }
    }

    public static PlanetFactory CloneForSimulation(PlanetFactory factory, BlackboxSelection selection)
    {
      var newFactory = new PlanetFactory();
      newFactory.index = -1;
      newFactory.planet = null;
      newFactory.landed = false;
      // Unfortunately, we need the GameData unless we want to rewrite FactorySystem.Import
      newFactory.gameData = factory.gameData;

      newFactory.SetEntityCapacity(factory.entityCapacity);
      newFactory.entityCursor = factory.entityCursor;
      newFactory.entityRecycleCursor = factory.entityRecycleCursor;
      factory.entityPool.CopyTo(newFactory.entityPool, 0);
      // The entityConnPool seems to be only required for FactorySystem.Import
      factory.entityConnPool.CopyTo(newFactory.entityConnPool, 0);
      // The entityMutexs is required by Factory.PickFrom and Factory.InsertInto
      // which are in turn used by inserters, power generators and miners, so we'll have to support it
      for (int i = 1; i < newFactory.entityCursor; i++)
      {
        ref readonly var entity = ref newFactory.entityPool[i];
        if (entity.beltId == 0 && entity.inserterId == 0 && entity.splitterId == 0 && entity.monitorId == 0)
          newFactory.entityMutexs[i] = new Mutex(i);
      }

      // Ensure cargoContainer works as expected
      var newCargoContainer = new CargoContainer(true);
      ExportImport(factory.cargoContainer.Export, newCargoContainer.Import);
      newFactory.cargoContainer = newCargoContainer;

      var newCargoTraffic = new CargoTraffic();
      newCargoTraffic.factory = newFactory;
      newCargoTraffic.container = newCargoContainer;
      ExportImport(factory.cargoTraffic.Export, newCargoTraffic.Import);
      //// Clear the CargoPaths
      //for (int i = 0; i < selection.cargoPathIds.Count; i++)
      //{
      //  ref var path = ref newCargoTraffic.pathPool[selection.cargoPathIds[i]];
      //  path.RemoveCargosInSegment(0, 0);
      //  path.updateLen = path.bufferLength;
      //}
			newFactory.cargoTraffic = newCargoTraffic;

      var newPowerSystem = new PowerSystem(factory.planet, true);
      newPowerSystem.planet = null;
      newPowerSystem.factory = newFactory;
      //ExportImport(factory.powerSystem.Export, newPowerSystem.Import);
      newPowerSystem.SetConsumerCapacity(factory.powerSystem.consumerCapacity);
      newPowerSystem.consumerCursor = factory.powerSystem.consumerCursor;
      newPowerSystem.consumerRecycleCursor = factory.powerSystem.consumerRecycleCursor;
      factory.powerSystem.consumerPool.CopyTo(newPowerSystem.consumerPool, 0);
      newPowerSystem.SetNetworkCapacity(factory.powerSystem.netCapacity);
      newPowerSystem.netCursor = factory.powerSystem.netCursor;
      newPowerSystem.netRecycleCursor = factory.powerSystem.netRecycleCursor;
      for (int i = 0; i < newPowerSystem.netCursor; i++)
      {
        if (factory.powerSystem.netPool[i] != null)
        {
          newPowerSystem.netPool[i] = new PowerNetwork();
          newPowerSystem.netPool[i].Reset(i);
          newPowerSystem.netPool[i].consumerRatio = 1;
          newPowerSystem.networkServes[i] = 1;
        }
        else
        {
          newPowerSystem.netPool[i] = null;
        }
      }
      newFactory.powerSystem = newPowerSystem;

      var newFactorySystem = new FactorySystem(factory.planet, true);
      newFactorySystem.planet = null;
      newFactorySystem.factory = newFactory;
      newFactorySystem.traffic = newCargoTraffic;
      newFactorySystem.storage = null; // This isn't even read anywhere
      
      ExportImport(factory.factorySystem.Export, newFactorySystem.Import);
      //// Clear the assemblers, labs and inserters
      //for (int i = 0; i < selection.assemblerIds.Count; i++)
      //{
      //  ref var assembler = ref newFactorySystem.assemblerPool[selection.assemblerIds[i]];
      //  assembler.served.Initialize();
      //  assembler.produced.Initialize();
      //}
      //for (int i = 0; i < selection.labIds.Count; i++)
      //{
      //  ref var lab = ref newFactorySystem.labPool[selection.labIds[i]];
      //  lab.served.Initialize();
      //  lab.produced.Initialize();
      //}
      //for (int i = 0; i < selection.inserterIds.Count; i++)
      //{
      //  ref var inserter = ref newFactorySystem.inserterPool[selection.inserterIds[i]];
      //  inserter.itemId = 0;
      //  inserter.stackCount = 0;
      //  inserter.time = 0;
      //  inserter.stage = EInserterStage.Picking;
      //}
      newFactory.factorySystem = newFactorySystem;

      var newTransport = new PlanetTransport(factory.gameData, factory.planet, true);
      newTransport.gameData = null;
      newTransport.planet = null;
      newTransport.factory = newFactory;
      newTransport.powerSystem = null;
      if (newTransport.droneRenderer != null)
      {
        newTransport.droneRenderer.Destroy();
        newTransport.droneRenderer = null;
      }
      // Can't use ExportImport, since PlanetTransport.Import affects global state
      {
        newTransport.stationCursor = factory.transport.stationCursor;
        newTransport.SetStationCapacity(factory.transport.stationCapacity);
        newTransport.stationRecycleCursor = factory.transport.stationRecycleCursor;
        for (int i = 1; i < factory.transport.stationCursor; i++)
        {
          var station = factory.transport.stationPool[i];
          if (station != null)
          {
            var newStation = new StationComponent();
            ExportImport(station.Export, newStation.Import);
            newTransport.stationPool[i] = newStation;
          }
        }
        factory.transport.stationRecycle.CopyTo(newTransport.stationRecycle, 0);
      }
      newFactory.transport = newTransport;
      
      // Set entity needs
      for (int i = 0; i < selection.assemblerIds.Count; i++)
      {
        var assemblerId = selection.assemblerIds[i];
        ref var assembler = ref newFactorySystem.assemblerPool[assemblerId];
        newFactory.entityNeeds[assembler.entityId] = assembler.needs;
      }
      for (int i = 0; i < selection.labIds.Count; i++)
      {
        var labId = selection.labIds[i];
        ref var lab = ref newFactorySystem.labPool[labId];
        newFactory.entityNeeds[lab.entityId] = lab.needs;
      }
      for (int i = 0; i < selection.stationIds.Count; i++)
      {
        var stationId = selection.stationIds[i];
        var station = newTransport.stationPool[stationId];
        newFactory.entityNeeds[station.entityId] = station.needs;
      }

      return newFactory;
		}

    public static void FreeSimulationFactory(PlanetFactory factory)
    {
      var powerSystem = factory.powerSystem;
      powerSystem.consumerPool = null;
      powerSystem.consumerCursor = 1;
      powerSystem.consumerCapacity = 0;
      powerSystem.consumerRecycle = null;
      powerSystem.consumerRecycleCursor = 0;
      for (int i = 0; i < powerSystem.netPool.Length; i++)
      {
        if (powerSystem.netPool[i] != null)
        {
          powerSystem.netPool[i].Free();
          powerSystem.netPool[i] = null;
        }
      }
      powerSystem.netPool = null;
      powerSystem.netCursor = 1;
      powerSystem.netCapacity = 0;
      powerSystem.netRecycle = null;
      powerSystem.netRecycleCursor = 0;
      factory.powerSystem = null;
      
      for (int i = 0; i < factory.transport.stationPool.Length; i++)
      {
        if (factory.transport.stationPool[i] != null)
        {
          factory.transport.stationPool[i].Free();
          factory.transport.stationPool[i] = null;
        }
      }
      factory.transport.stationPool = null;
      factory.transport.stationCursor = 1;
      factory.transport.stationCapacity = 0;
      factory.transport.stationRecycle = null;
      factory.transport.stationRecycleCursor = 0;
      factory.transport = null;
      
      factory.Free();
    }

    public static void SimulateGameTick(BlackboxBenchmark benchmark)
    {
      var factory = benchmark.simulationFactory;
      if (factory.factorySystem != null)
      {
        benchmark.BeginGameTick();
        
        factory.factorySystem.GameTickBeforePower(0, false);
        factory.cargoTraffic.GameTickBeforePower(0, false);
        benchmark.LogPowerConsumer();

        // TODO: Assembler game tick
        //   DONE
        for (int i = 0; i < benchmark.assemblerIds.Count; i++)
        {
          var assemblerId = benchmark.assemblerIds[i];
          // Do NOT use ref readonly here as it will not perform the updates
          ref var assembler = ref factory.factorySystem.assemblerPool[assemblerId];

          // Skipping the customary assembler.id == assemblerId check, since we'd have ideally vetted this beforehand

          if (assembler.recipeId != 0)
          {
            assembler.UpdateNeeds();
            if (!IsAssemblerStacking(ref assembler))
              assembler.InternalUpdate(1, benchmark.productRegister, benchmark.consumeRegister);
            
            // factory.entityNeeds[assembler.entityId] = assembler.needs;
          }
        }
        
        // TODO: Fractionator game tick
        //   DONE
        for (int i = 0; i < benchmark.fractionatorIds.Count; i++)
        {
          var fractionatorId = benchmark.fractionatorIds[i];
          // Do NOT use ref readonly here as it will not perform the updates
          ref var fractionator = ref factory.factorySystem.fractionatorPool[fractionatorId];
          fractionator.InternalUpdate(factory, 1f, factory.entitySignPool, benchmark.productRegister, benchmark.consumeRegister);
        }

        // TODO: Lab game tick
        //   DONE
        for (int i = 0; i < benchmark.labIds.Count; i++)
        {
          var labId = benchmark.labIds[i];
          // Do NOT use ref readonly here as it will not perform the updates
          ref var lab = ref factory.factorySystem.labPool[labId];

          if (!lab.researchMode && lab.recipeId > 0)
          {
            lab.UpdateNeedsAssemble();
            lab.InternalUpdateAssemble(1, benchmark.productRegister, benchmark.consumeRegister);
            // factory.entityNeeds[lab.entityId] = lab.needs;
          }
        }

        benchmark.AdjustStationStorageCount();
        benchmark.LogStationBefore();

        for (int i = 0; i < benchmark.labIds.Count; i++)
        {
          var labId = benchmark.labIds[i];
          // Do NOT use ref readonly here as it will not perform the updates
          ref var lab = ref factory.factorySystem.labPool[labId];

          if (lab.nextLabId > 0)
            lab.UpdateOutputToNext(factory.factorySystem.labPool);
        }

        // TODO: Station input from belt
        // TODO: This makes use of factory.entitySignPool
        //   DONE
        factory.transport.GameTick_InputFromBelt();

        // TODO: Inserter game tick
        // TODO: Check the usage of factory's 'InsertInto' and 'PickFrom'
        //   DONE
        for (int i = 0; i < benchmark.inserterIds.Count; i++)
        {
          var inserterId = benchmark.inserterIds[i];
          ref var inserter = ref factory.factorySystem.inserterPool[inserterId];
          inserter.InternalUpdateNoAnim(factory, factory.entityNeeds, 1);
        }

        // TODO: Cargo game tick
        for (int i = 0; i < benchmark.cargoPathIds.Count; i++)
        {
          var cargoPathId = benchmark.cargoPathIds[i];
          var cargoPath = factory.cargoTraffic.pathPool[cargoPathId];
          cargoPath.Update();
        }
        for (int i = 0; i < benchmark.splitterIds.Count; i++)
        {
          var splitterId = benchmark.splitterIds[i];
          // Do NOT use ref readonly here as it will not perform the updates
          ref var splitter = ref factory.cargoTraffic.splitterPool[splitterId];
          splitter.CheckPriorityPreset();
          factory.cargoTraffic
            .UpdateSplitter(
              splitterId,
              splitter.input0, splitter.input1, splitter.input2,
              splitter.output0, splitter.output1, splitter.output2,
              splitter.outFilter
            );
        }

        // TODO: Spraycoater game tick
        for (int i = 0; i < benchmark.spraycoaterIds.Count; i++)
        {
          var spraycoaterId = benchmark.spraycoaterIds[i];
          ref var spraycoater = ref factory.cargoTraffic.spraycoaterPool[spraycoaterId];
          spraycoater.InternalUpdate(factory.cargoTraffic, factory.entityAnimPool, benchmark.consumeRegister);
        }

        // TODO: Piler game tick
        for (int i = 0; i < benchmark.pilerIds.Count; i++)
        {
          var pilerId = benchmark.pilerIds[i];
          ref var piler = ref factory.cargoTraffic.pilerPool[pilerId];
          piler.InternalUpdate(factory.cargoTraffic, factory.entityAnimPool, out _);
        }

        // TODO: Station output to belt
        // TODO: This makes use of factory.entitySignPool
        factory.transport.GameTick_OutputToBelt();

        benchmark.LogStationAfter();
        benchmark.DoInserterAdaptiveStacking();

        benchmark.EndGameTick();
      }
    }

    public static bool IsAssemblerStacking(ref AssemblerComponent assembler)
    {
      for (int j = 0; j < assembler.productCounts.Length; j++)
      {
        if (assembler.produced[j] > assembler.productCounts[j] * 4)
        {
          return true;
        }
      }

      return false;
    }

    public static void DoAdaptiveStacking(ref InserterComponent inserter, PlanetFactory factory, bool forceNoStacking)
    {
      if (inserter.careNeeds && inserter.stage == EInserterStage.Picking && inserter.itemId > 0)
      {
        var needs = factory.entityNeeds[inserter.insertTarget];
        var needIdx = 0;

        //inserter.idleTick = 0;

        for (int i = 0; i < needs.Length; i++)
        {
          if (needs[i] == inserter.itemId)
          {
            needIdx = i;
            break;
          }  
        }

        ref readonly var insertEntity = ref factory.entityPool[inserter.insertTarget];
        var max = 1;
        var served = 0;
        if (insertEntity.assemblerId > 0)
        {
          ref readonly var assembler = ref factory.factorySystem.assemblerPool[insertEntity.assemblerId];
          max = assembler.requireCounts[needIdx] * 3;
          served = assembler.served[needIdx];
        }
        else if (insertEntity.labId > 0)
        {
          ref readonly var lab = ref factory.factorySystem.labPool[insertEntity.labId];
          max = 4;
          served = lab.served[needIdx];

          while (lab.nextLabId > 0)
          {
            lab = ref factory.factorySystem.labPool[lab.nextLabId];
            max += 4;
            served += lab.served[needIdx];
          }
        }
        else
        {
          return;
        }

        if (inserter.stackCount >= (forceNoStacking ? 1 : max - served))
        {
          inserter.time = inserter.speed;
          inserter.stage = EInserterStage.Sending;
        }
      }
    }
  }
}
