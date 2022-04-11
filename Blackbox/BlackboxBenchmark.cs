using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEngine;
using HarmonyLib;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DysonSphereProgram.Modding.Blackbox
{
  public class AnalysisData
  {
    public const int fo_storage0delta = 0;
    public const int fo_storage1delta = 1;
    public const int fo_storage2delta = 2;
    public const int fo_storage3delta = 3;
    public const int fo_storage4delta = 4;
    public const int size_station = 5;

    public const int fo_served0 = 0;
    public const int fo_served1 = 1;
    public const int fo_served2 = 2;
    public const int fo_served3 = 3;
    public const int fo_served4 = 4;
    public const int fo_served5 = 5;
    public const int fo_produced0 = 6;
    public const int fo_produced1 = 7;
    public const int size_assembler = 8;

    //public const int fo_itemId = 0;
    //public const int fo_stackCount = 1;
    //public const int fo_stage = 2;
    //public const int size_inserter = 3;
    public const int fo_stackCount = 0;
    public const int fo_stage = 1;
    public const int fo_idleTick = 2;
    public const int size_inserter = 3;

    public const int fo_requiredEnergy_low = 0;
    public const int fo_requiredEnergy_high = 1;
    public const int size_powerConsumer = 2;
  }

  public struct AnalysisStationInfo
  {
    int storage0delta;
    int storage1delta;
    int storage2delta;
    int storage3delta;
    int storage4delta;
  }

  public struct AnalysisAssemblerInfo
  {
    int served0;
    int served1;
    int served2;
    int served3;
    int served4;
    int served5;
    int produced0;
    int produced1;
  }

  public struct AnalysisInserterInfo
  {
    //int itemId;
    int stackCount;
    EInserterStage stage;
  }

  public struct AnalysisPowerConsumerInfo
  {
    long requiredEnergy;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct ProduceConsumePair
  {
    public int Produced;
    public int Consumed;
  }

  public class BlackboxBenchmark: BlackboxBenchmarkBase
  {
    internal readonly ImmutableSortedSet<int> entityIds;
    internal readonly ImmutableSortedSet<int> pcIds;
    internal readonly ImmutableSortedSet<int> assemblerIds;
    internal readonly ImmutableSortedSet<int> labIds;
    internal readonly ImmutableSortedSet<int> inserterIds;
    internal readonly ImmutableSortedSet<int> stationIds;
    internal readonly ImmutableSortedSet<int> cargoPathIds;
    internal readonly ImmutableSortedSet<int> splitterIds;
    internal readonly ImmutableSortedSet<int> pilerIds;
    internal readonly ImmutableSortedSet<int> spraycoaterIds;
    internal readonly ImmutableSortedSet<int> itemIds;

    const int TicksPerSecond = 60;
    const int TicksPerMinute = TicksPerSecond * 60;

    const bool profileInserters = false;
    public static bool logProfiledData = false;
    const bool analyzeInserterStackEffect = false;
    public static bool forceNoStackingConfig = false;
    public static bool adaptiveStackingConfig = false;
    public bool adaptiveStacking;
    public bool forceNoStacking;

    public static bool continuousLogging = false;
    StreamWriter continuousLogger;

    ProduceConsumePair[] totalStats;
    ISummarizer<int> summarizer;
    int[] cycleDetectionData;
    TimeSeriesData<int> profilingTsData;

    const int pcOffset = 0;
    int pcSize;
    int stationOffset;
    int[] stationOffsets;
    int stationSize;
    int factoryStatsOffset;
    int factoryStatsSize;
    int stationStatsOffset;
    int stationStatsSize;
    int statsDiffOffset;
    int statsDiffSize;

    int perTickProfilingSize;
    int profilingTick = 0;
    int[] stabilityDetectionData;
    int stabilizedTick = -1;

    const int analysisVerificationCount = 4;
    int timeSpendGCD;
    int timeSpendLCM;
    int profilingTickCount;
    int profilingEntryCount;
    int observedCycleLength;

    BlackboxRecipe analysedRecipe;
    public override BlackboxRecipe EffectiveRecipe => analysedRecipe;

    internal PlanetFactory simulationFactory;
    internal Task profilingTask;
    internal CancellationTokenSource profilingTaskCancel;

    public static string FileLogPath
    {
      //get => Path.GetDirectoryName(@"D:\Raptor\Workspace\Personal\Projects\DSPMods\Blackbox\");
      get => Path.GetDirectoryName(Plugin.Path);
    }

    public BlackboxBenchmark(Blackbox blackbox) : base(blackbox)
    {
      this.entityIds = blackbox.Selection.entityIds;
      this.pcIds = blackbox.Selection.pcIds;
      this.assemblerIds = blackbox.Selection.assemblerIds;
      this.labIds = blackbox.Selection.labIds;
      this.inserterIds = blackbox.Selection.inserterIds;
      this.stationIds = blackbox.Selection.stationIds;
      this.cargoPathIds = blackbox.Selection.cargoPathIds;
      this.splitterIds = blackbox.Selection.splitterIds;
      this.pilerIds = blackbox.Selection.pilerIds;
      this.spraycoaterIds = blackbox.Selection.spraycoaterIds;
      this.itemIds = blackbox.Selection.itemIds;
    }

    class BlackboxBenchmarkSummarizer : ISummarizer<int>
    {
      public BlackboxBenchmark analysis;

      public void Initialize(Span<int> data)
      {
        data.Clear();
      }

      public void Summarize(Span<int> detailed, Span<int> summary)
      {
        var pcCount = 1; // analysis.pcIds.Count;
        var pcDetailed = MemoryMarshal.Cast<int, long>(detailed.Slice(pcOffset, pcCount * 2));
        var pcSummary = MemoryMarshal.Cast<int, long>(summary.Slice(pcOffset, pcCount * 2));
        for (int i = 0; i < pcSummary.Length; i++)
          pcSummary[i] += pcDetailed[i];

        var restDetailed = detailed.Slice(analysis.stationOffset, analysis.stationSize + analysis.factoryStatsSize + analysis.stationStatsSize);
        var restSummary = summary.Slice(analysis.stationOffset, analysis.stationSize + analysis.factoryStatsSize + analysis.stationStatsSize);
        for (int i = 0; i < restSummary.Length; i++)
          restSummary[i] += restDetailed[i];

        var statsDiffDetailed = detailed.Slice(analysis.statsDiffOffset, analysis.statsDiffSize);
        var statsDiffSummary = summary.Slice(analysis.statsDiffOffset, analysis.statsDiffSize);
        for (int i = 0; i < statsDiffSummary.Length; i++)
          statsDiffSummary[i] = Math.Max(statsDiffSummary[i], statsDiffDetailed[i]);
      }
    }

    public override void Begin()
    {
      if (!factoryRef.TryGetTarget(out var factory))
      {
        Plugin.Log.LogError("PlanetFactory instance pulled out from under " + nameof(BlackboxBenchmark) + " in " + nameof(Begin));
        return;
      }

      this.simulationFactory = blackbox.analyseInBackground ? PlanetFactorySimulation.CloneForSimulation(factory, blackbox.Selection) : factory;

      var tmp_assemblerTimeSpends = new List<int>();

      foreach (var entityId in entityIds)
      {
        ref readonly var entity = ref simulationFactory.entityPool[entityId];
        if (entity.assemblerId > 0)
        {
          ref readonly var assembler = ref simulationFactory.factorySystem.assemblerPool[entity.assemblerId];
          tmp_assemblerTimeSpends.Add(assembler.timeSpend / assembler.speed);
        }
        if (entity.labId > 0)
        {
          ref readonly var lab = ref simulationFactory.factorySystem.labPool[entity.labId];
          if (lab.matrixMode)
          {
            tmp_assemblerTimeSpends.Add(lab.timeSpend / 10000);
          }
        }
      }

      this.stationOffsets = new int[stationIds.Count];

      this.pcSize = AnalysisData.size_powerConsumer * 1; // pcIds.Count;
      this.stationSize = 0;
      for (int i = 0; i < stationIds.Count; i++)
      {
        stationOffsets[i] = stationSize;
        ref readonly var station = ref simulationFactory.transport.stationPool[stationIds[i]];
        this.stationSize += station.storage.Length;
      }
      this.factoryStatsSize = itemIds.Count * 2;
      this.stationStatsSize = itemIds.Count * 2;
      this.statsDiffSize = itemIds.Count;

      this.stationOffset = pcOffset + pcSize;
      this.factoryStatsOffset = stationOffset + stationSize;
      this.stationStatsOffset = factoryStatsOffset + factoryStatsSize;
      this.statsDiffOffset = stationStatsOffset + stationStatsSize;
      this.perTickProfilingSize = pcSize + stationSize + factoryStatsSize + stationStatsSize + statsDiffSize;

      this.totalStats = new ProduceConsumePair[itemIds.Count];

      var distinctTimeSpends = tmp_assemblerTimeSpends.Distinct().DefaultIfEmpty(60);
      this.timeSpendGCD = Utils.GCD(distinctTimeSpends);
      this.timeSpendLCM = Utils.LCM(distinctTimeSpends) * 4;
      this.profilingTickCount = timeSpendLCM * ((analysisVerificationCount * 2 * /* to account for sorter stacking */ 6) + 2);
      this.profilingEntryCount = profilingTickCount / timeSpendGCD;

      var mlg = new MultiLevelGranularity();
      mlg.levels = 2;
      mlg.entryCounts = new[] { timeSpendGCD, this.profilingEntryCount };
      mlg.ratios = new[] { timeSpendGCD };

      this.summarizer = new BlackboxBenchmarkSummarizer() { analysis = this };

      profilingTsData = new TimeSeriesData<int>(this.perTickProfilingSize, mlg, summarizer);
      this.cycleDetectionData = new int[this.perTickProfilingSize * 2];
      this.stabilityDetectionData = new int[this.statsDiffSize];
      profilingTick = 0;

      this.forceNoStacking = forceNoStackingConfig;
      this.adaptiveStacking = adaptiveStackingConfig;

      if (continuousLogging)
      {
        Directory.CreateDirectory($@"{FileLogPath}\DataAnalysis");
        continuousLogger = new StreamWriter($@"{FileLogPath}\DataAnalysis\BenchmarkV4_CL_{blackbox.Id}.csv");

        WriteContinuousLoggingHeader();
      }

      if (blackbox.analyseInBackground)
      {
        profilingTaskCancel = new CancellationTokenSource();
        var ct = profilingTaskCancel.Token;
        profilingTask = Task.Factory.StartNew(() => SimulateTillProfilingDone(ct), profilingTaskCancel.Token, TaskCreationOptions.PreferFairness, TaskScheduler.Default);
      }
    }
    
    private void SimulateTillProfilingDone(CancellationToken ct)
    {
      while (blackbox.Status == BlackboxStatus.InAnalysis)
      {
        ct.ThrowIfCancellationRequested();
        PlanetFactorySimulation.SimulateGameTick(this);
      }
    }

    public override void Free()
    {
      if (continuousLogging)
      {
        continuousLogger.Dispose();
        continuousLogger = null;
      }

      analysedRecipe = null;
      if (blackbox.analyseInBackground)
      {
        if (profilingTask != null && profilingTaskCancel != null)
        {
          try
          {
            if (!profilingTask.IsCompleted)
            {
              profilingTaskCancel.Cancel();
              profilingTask.Wait();
            }
          }
          catch (AggregateException ex)
          {
            if (ex.InnerException is OperationCanceledException)
              Plugin.Log.LogInfo("Blackbox #" + blackbox.Id + " benchmarking was cancelled");
            else
              Plugin.Log.LogError(ex);
          }
          catch (Exception ex)
          {
            Plugin.Log.LogError(ex);
          }
          finally
          {
            profilingTaskCancel.Dispose();
            profilingTaskCancel = null;
          }
        }

        PlanetFactorySimulation.FreeSimulationFactory(simulationFactory);
      }
      if (logProfiledData)
      {
        DumpAnalysisToFile();
      }
      simulationFactory = null;
    }

    private void DumpAnalysisToFile()
    {
      Directory.CreateDirectory($@"{FileLogPath}\DataAnalysis");
      using (var f = new FileStream($@"{FileLogPath}\DataAnalysis\BenchmarkV4_{blackbox.Id}.txt", FileMode.Create))
      {
        using (var s = new StreamWriter(f))
        {
          var dataSize = profilingTsData.DataSize;
          s.WriteLine(dataSize);
          var data = profilingTsData.Data;
          var entries = data.Length / dataSize;
          var cursor = 0;
          for (int i = 0; i < entries; i++)
          {
            var pcs = MemoryMarshal.Cast<int, long>(new Span<int>(data, i * dataSize, pcSize));
            foreach (var pc in pcs)
            {
              s.Write(pc);
              s.Write(" ");
            }
            cursor += pcSize;

            for (int j = pcSize; j < dataSize; j++)
            {
              s.Write(data[cursor++]);
              s.Write(" ");
            }
            s.WriteLine();
          }
          s.WriteLine();
          for (int i = 0; i < itemIds.Count; i++)
          {
            var itemName = LDB.ItemName(itemIds[i]);
            s.WriteLine(itemName);
            var stats = totalStats[i];
            s.WriteLine($"  Produced: {stats.Produced}");
            s.WriteLine($"  Consumed: {stats.Consumed}");
            s.WriteLine($"  Difference: {stats.Produced - stats.Consumed}");
          }
        }
      }
    }

    private void WriteContinuousLoggingHeader()
    {
      continuousLogger.Write($"PC,");
      // for (int i = 0; i < pcIds.Count; i++)
      //   continuousLogger.Write($"PC{i},");

      for (int i = 0; i < stationIds.Count; i++)
      {
        ref readonly var station = ref simulationFactory.transport.stationPool[stationIds[i]];
        for (int j = 0; j < station.storage.Length; j++)
        {
          continuousLogger.Write($"S{i}_{j},");
        }
      }

      for (int i = 0; i < itemIds.Count; i++)
      {
        var itemId = itemIds[i];
        var itemName = LDB.ItemName(itemId).Replace(" ", "").Trim();
        continuousLogger.Write($"F_P_{itemName},");
        continuousLogger.Write($"F_C_{itemName},");
        continuousLogger.Write($"S_P_{itemName},");
        continuousLogger.Write($"S_C_{itemName},");
      }
      
      for (int i = 0; i < itemIds.Count; i++)
      {
        var itemId = itemIds[i];
        var itemName = LDB.ItemName(itemId).Replace(" ", "").Trim();
        continuousLogger.Write($"T_P_{itemName},");
        continuousLogger.Write($"T_C_{itemName},");
        continuousLogger.Write($"T_D_{itemName},");
      }
      continuousLogger.WriteLine($"EOL");
    }

    private void WriteContinuousLoggingData(int level)
    {
      var entry = profilingTsData.LevelEntryOffset(level, profilingTick);

      var pcData = MemoryMarshal.Cast<int, long>(entry.Slice(pcOffset, pcSize));
      // for (int i = 0; i < pcIds.Count; i++)
      // {
      //   continuousLogger.Write(pcData[i]);
      //   continuousLogger.Write(',');
      // }
      // long pcDataTotal = 0;
      // for (int i = 0; i < pcIds.Count; i++)
      //   pcDataTotal += pcData[i];
      long pcDataTotal = pcData[0];
      continuousLogger.Write(pcDataTotal);
      continuousLogger.Write(',');

      var stationData = entry.Slice(stationOffset, stationSize);
      var curStationOffset = 0;
      for (int i = 0; i < stationIds.Count; i++)
      {
        ref readonly var station = ref simulationFactory.transport.stationPool[stationIds[i]];
        for (int j = 0; j < station.storage.Length; j++)
        {
          continuousLogger.Write(stationData[curStationOffset]);
          continuousLogger.Write(',');
          curStationOffset++;
        }
      }

      var factoryStatsData = MemoryMarshal.Cast<int, ProduceConsumePair>(entry.Slice(factoryStatsOffset, factoryStatsSize));
      var stationStatsData = MemoryMarshal.Cast<int, ProduceConsumePair>(entry.Slice(stationStatsOffset, stationStatsSize));
      for (int i = 0; i < itemIds.Count; i++)
      {
        continuousLogger.Write(factoryStatsData[i].Produced);
        continuousLogger.Write(',');
        continuousLogger.Write(factoryStatsData[i].Consumed);
        continuousLogger.Write(',');
        continuousLogger.Write(stationStatsData[i].Produced);
        continuousLogger.Write(',');
        continuousLogger.Write(stationStatsData[i].Consumed);
        continuousLogger.Write(',');
      }
      
      for (int i = 0; i < itemIds.Count; i++)
      {
        continuousLogger.Write(totalStats[i].Produced);
        continuousLogger.Write(',');
        continuousLogger.Write(totalStats[i].Consumed);
        continuousLogger.Write(',');
        continuousLogger.Write(totalStats[i].Produced - totalStats[i].Consumed);
        continuousLogger.Write(',');
      }
      continuousLogger.WriteLine(0);
    }

    private void LogItemStats()
    {
      var levelEntrySpan = profilingTsData.LevelEntryOffset(0, profilingTick);

      var factoryStatsData = MemoryMarshal.Cast<int, ProduceConsumePair>(levelEntrySpan.Slice(factoryStatsOffset, factoryStatsSize));
      var stationStatsData = MemoryMarshal.Cast<int, ProduceConsumePair>(levelEntrySpan.Slice(stationStatsOffset, stationStatsSize));

      for (int i = 0; i < itemIds.Count; i++)
      {
        factoryStatsData[i].Consumed += consumeRegister[itemIds[i]];
        factoryStatsData[i].Produced += productRegister[itemIds[i]];
      }

      var stationData = levelEntrySpan.Slice(stationOffset, stationSize);
      var curStationOffset = 0;
      for (int i = 0; i < stationIds.Count; i++)
      {
        ref readonly var station = ref simulationFactory.transport.stationPool[stationIds[i]];
        for (int j = 0; j < station.storage.Length; j++)
        {
          var stationStorage = station.storage[j];
          var effectiveLogic = stationStorage.remoteLogic == ELogisticStorage.None ? stationStorage.localLogic : stationStorage.remoteLogic;
          if (effectiveLogic != ELogisticStorage.None && stationStorage.itemId > 0)
          {
            var itemIdx = itemIds.IndexOf(stationStorage.itemId);
            if (effectiveLogic == ELogisticStorage.Supply)
              stationStatsData[itemIdx].Consumed += -stationData[curStationOffset];
            if (effectiveLogic == ELogisticStorage.Demand)
              stationStatsData[itemIdx].Produced += stationData[curStationOffset];
          }
          curStationOffset++;
        }
      }
    }

    private void LogTotalItemStats()
    {
      var entrySpan = profilingTsData.LevelEntryOffset(0, profilingTick);
      var factoryStatsSpan = entrySpan.Slice(factoryStatsOffset, factoryStatsSize);
      var stationStatsSpan = entrySpan.Slice(stationStatsOffset, stationStatsSize);
      var totalStatsSpan = MemoryMarshal.Cast<ProduceConsumePair, int>(new Span<ProduceConsumePair>(totalStats));
      for (int i = 0; i < totalStatsSpan.Length; i++)
      {
        totalStatsSpan[i] += factoryStatsSpan[i];
        totalStatsSpan[i] += stationStatsSpan[i];
      }
      var totalStatsDiffSpan = entrySpan.Slice(statsDiffOffset, statsDiffSize);
      for (int i = 0; i < totalStats.Length; i++)
        totalStatsDiffSpan[i] = totalStats[i].Produced - totalStats[i].Consumed;
    }

    private void CheckStabilization()
    {
      var entrySpan = profilingTsData.LevelEntryOffset(0, profilingTick);
      var totalStatsDiffSpan = entrySpan.Slice(statsDiffOffset, statsDiffSize);
      for (int i = 0; i < totalStatsDiffSpan.Length; i++)
        if (totalStatsDiffSpan[i] > stabilityDetectionData[i])
        {
          this.stabilizedTick = this.profilingTick;
          stabilityDetectionData[i] = totalStatsDiffSpan[i];
        }
    }

    private void ClearItemStats()
    {
      var factoryStatsSpan = profilingTsData.LevelEntryOffset(0, profilingTick).Slice(factoryStatsOffset, factoryStatsSize);
      factoryStatsSpan.Clear();
      var stationStatsSpan = profilingTsData.LevelEntryOffset(0, profilingTick).Slice(stationStatsOffset, stationStatsSize);
      stationStatsSpan.Clear();
    }

    private void EndGameTick_Profiling()
    {
      LogItemStats();
      LogTotalItemStats();
      CheckStabilization();
      profilingTsData.SummarizeAtHigherGranularity(profilingTick);
      if (continuousLogging && (profilingTick + 1) % timeSpendGCD == 0)  WriteContinuousLoggingData(1);
      profilingTick += 1;
      ClearItemStats();

      if (profilingTick % timeSpendGCD == 0)
      {
        Plugin.Log.LogDebug("Profiling Tick: " + profilingTick);
      }
      if (profilingTick - stabilizedTick > timeSpendLCM && profilingTick % timeSpendLCM == 0)
      {
        Plugin.Log.LogDebug("Checking cycles");
        
        var endIndex = (profilingTick / timeSpendGCD) - 1;
        var circularOffset = 0;
        if (endIndex > profilingEntryCount)
        {
          circularOffset = (endIndex - profilingEntryCount) % profilingEntryCount;
          endIndex = profilingEntryCount - 1;
        }

        var indexEquals = new Func<int, int, bool>((int i1, int i2) =>
        {
          var span1 = profilingTsData.Level(1).Entry((i1 + circularOffset) % profilingEntryCount);
          var span2 = profilingTsData.Level(1).Entry((i2 + circularOffset) % profilingEntryCount);

          // for (int i = this.statsDiffOffset; i < this.statsDiffOffset + this.statsDiffSize; i++)
          //   if (span1[i] != span2[i])
          //     return false;

          for (int i = this.factoryStatsOffset; i < this.factoryStatsOffset + this.factoryStatsSize; i++)
            if (span1[i] != span2[i])
              return false;
          
          // for (int i = this.stationStatsOffset; i < this.stationStatsOffset + this.stationStatsSize; i++)
          //   if (span1[i] != span2[i])
          //     return false;
          //
          // for (int i = this.stationOffset; i < this.stationOffset + this.stationSize; i++)
          //   if (span1[i] != span2[i])
          //     return false;

          return true;
        });

        var summarizeEquals = new Func<int, int, int, bool>((int i1, int i2, int stride) =>
        {
          var span1Summary = new Span<int>(cycleDetectionData, 0, perTickProfilingSize);
          var span2Summary = new Span<int>(cycleDetectionData, perTickProfilingSize, perTickProfilingSize);

          summarizer.Initialize(span1Summary);
          summarizer.Initialize(span2Summary);

          for (int j = stride - 1; j >= 0; j--)
          {
            var span1 = profilingTsData.Level(1).Entry((i1 - j + circularOffset) % profilingEntryCount);
            var span2 = profilingTsData.Level(1).Entry((i2 - j + circularOffset) % profilingEntryCount);

            summarizer.Summarize(span1, span1Summary);
            summarizer.Summarize(span2, span2Summary);
          }

          // for (int i = this.statsDiffOffset; i < this.statsDiffOffset + this.statsDiffSize; i++)
          //   if (span1Summary[i] != span2Summary[i])
          //     return false;

          for (int i = this.factoryStatsOffset; i < this.factoryStatsOffset + this.factoryStatsSize; i++)
            if (span1Summary[i] != span2Summary[i])
              return false;
          
          // for (int i = this.stationStatsOffset; i < this.stationStatsOffset + this.stationStatsSize; i++)
          //   if (span1Summary[i] != span2Summary[i])
          //     return false;
          //
          // for (int i = this.stationOffset; i < this.stationOffset + this.stationSize; i++)
          //   if (span1Summary[i] != span2Summary[i])
          //     return false;

          return true;
        });

        if (CycleDetection.TryDetectCycles(endIndex, 0, analysisVerificationCount, indexEquals, summarizeEquals, out int cycleLength))
        {
          this.observedCycleLength = cycleLength * timeSpendGCD;
          Debug.Log($"Cycle Length of {this.observedCycleLength} detected");
          this.GenerateRecipe(endIndex, circularOffset, cycleLength);
          blackbox.NotifyBlackboxed(this.analysedRecipe);
        }
      }
      if (profilingTick >= this.profilingTickCount * 40)
      {
        profilingTick = 0;
        Plugin.Log.LogDebug($"Analysis Failed");
        blackbox.NotifyAnalysisFailed();
      }
    }

    public override void EndGameTick()
    {
      if (blackbox.Status == BlackboxStatus.InAnalysis)
      {
        EndGameTick_Profiling();
      }
    }

    void GenerateRecipe(int endIndex, int circularOffset, int cycleLength)
    {
      long idleEnergyPerTick = 0;
      for (int i = 0; i < pcIds.Count; i++)
        idleEnergyPerTick += simulationFactory.powerSystem.consumerPool[pcIds[i]].idleEnergyPerTick;

      long idleEnergyPerCycle = idleEnergyPerTick * this.observedCycleLength;

      long workingEnergyPerCycle = 0;

      var summarizer = new BlackboxBenchmarkSummarizer() { analysis = this };

      int[] dataPerCycle = new int[perTickProfilingSize];
      var dataPerCycleSpan = new Span<int>(dataPerCycle);
      summarizer.Initialize(dataPerCycleSpan);

      for (int entryIdx = endIndex; entryIdx > endIndex - cycleLength; entryIdx--)
      {
        var entry = profilingTsData.Level(1).Entry((entryIdx + circularOffset) % profilingEntryCount);
        summarizer.Summarize(entry, dataPerCycleSpan);
      }

      var pcData = MemoryMarshal.Cast<int, long>(dataPerCycleSpan.Slice(pcOffset, pcSize));
      foreach (var pc in pcData)
        workingEnergyPerCycle += pc;

      var tmp_stationStorageExit = new Dictionary<int, Dictionary<int, int>>();
      var tmp_stationStorageEnter = new Dictionary<int, Dictionary<int, int>>();
      var stationData = dataPerCycleSpan.Slice(stationOffset, stationSize);
      var curStationOffset = 0;
      for (int i = 0; i < stationIds.Count; i++)
      {
        ref readonly var station = ref simulationFactory.transport.stationPool[stationIds[i]];
        for (int j = 0; j < station.storage.Length; j++)
        {
          var stationStorage = station.storage[j];
          var itemId = stationStorage.itemId;
          //var effectiveLogic = stationStorage.remoteLogic == ELogisticStorage.None ? stationStorage.localLogic : stationStorage.remoteLogic;
          if (itemId > 0 && stationData[curStationOffset] != 0)
          {
            if (stationData[curStationOffset] > 0)
            {
              if (!tmp_stationStorageExit.ContainsKey(i))
                tmp_stationStorageExit[i] = new Dictionary<int, int>();
              tmp_stationStorageExit[i][itemId] = stationData[curStationOffset];
            }
            else
            {
              if (!tmp_stationStorageEnter.ContainsKey(i))
                tmp_stationStorageEnter[i] = new Dictionary<int, int>();
              tmp_stationStorageEnter[i][itemId] = -stationData[curStationOffset];
            }
          }
          curStationOffset++;
        }
      }

      var tmp_produces = new Dictionary<int, int>();
      var tmp_consumes = new Dictionary<int, int>();

      var factoryStatsSpan = MemoryMarshal.Cast<int, ProduceConsumePair>(dataPerCycleSpan.Slice(factoryStatsOffset, factoryStatsSize));

      for (int i = 0; i < itemIds.Count; i++)
      {
        if (factoryStatsSpan[i].Produced > 0)
          tmp_produces[itemIds[i]] = factoryStatsSpan[i].Produced;
        if (factoryStatsSpan[i].Consumed > 0)
          tmp_consumes[itemIds[i]] = factoryStatsSpan[i].Consumed;
      }

      Plugin.Log.LogDebug($"Idle Energy per cycle: {idleEnergyPerCycle}");
      Plugin.Log.LogDebug($"Working Energy per cycle: {workingEnergyPerCycle}");
      Plugin.Log.LogDebug($"Idle Power: {(idleEnergyPerCycle / this.observedCycleLength) * TicksPerSecond}");
      Plugin.Log.LogDebug($"Working Power: {(workingEnergyPerCycle / this.observedCycleLength) * TicksPerSecond}");

      Plugin.Log.LogDebug("Consumed");
      foreach (var item in tmp_consumes)
      {
        var itemName = LDB.ItemName(item.Key);
        Plugin.Log.LogDebug($"  {item.Value} {itemName}");
      }

      Plugin.Log.LogDebug("Produced");
      foreach (var item in tmp_produces)
      {
        var itemName = LDB.ItemName(item.Key);
        Plugin.Log.LogDebug($"  {item.Value} {itemName}");
      }

      Plugin.Log.LogDebug("Inputs");
      foreach (var stationIdx in tmp_stationStorageExit)
      {
        Plugin.Log.LogDebug($"  Station #{stationIdx.Key}:");
        foreach (var itemId in stationIdx.Value)
        {
          var itemName = LDB.ItemName(itemId.Key);
          Plugin.Log.LogDebug($"    {itemId.Value} {itemName}");
        }
      }

      Plugin.Log.LogDebug("Outputs");
      foreach (var stationIdx in tmp_stationStorageEnter)
      {
        Plugin.Log.LogDebug($"  Station #{stationIdx.Key}:");
        foreach (var itemId in stationIdx.Value)
        {
          var itemName = LDB.ItemName(itemId.Key);
          Plugin.Log.LogDebug($"    {itemId.Value} {itemName}");
        }
      }

      Plugin.Log.LogDebug($"Time (in ticks): {this.observedCycleLength}");
      Plugin.Log.LogDebug($"Time (in seconds): {this.observedCycleLength / (float)TicksPerSecond}");

      this.analysedRecipe = new BlackboxRecipe()
      {
        idleEnergyPerTick = idleEnergyPerTick,
        workingEnergyPerTick = workingEnergyPerCycle / this.observedCycleLength,
        timeSpend = this.observedCycleLength,
        produces = tmp_produces,
        consumes = tmp_consumes,
        inputs = tmp_stationStorageExit,
        outputs = tmp_stationStorageEnter
      };
    }

    public override void LogPowerConsumer()
    {
      var profilingData = MemoryMarshal.Cast<int, long>(profilingTsData.LevelEntryOffset(0, profilingTick).Slice(pcOffset, pcSize));
      for (int i = 0; i < pcIds.Count; i++)
      {
        ref readonly var consumer = ref simulationFactory.powerSystem.consumerPool[pcIds[i]];
        // profilingData[i] = consumer.requiredEnergy;
        profilingData[0] += consumer.requiredEnergy;
      }
    }

    public override bool ShouldInterceptAssembler(FactorySystem factorySystem, int assemblerId)
      => factorySystem == simulationFactory.factorySystem && assemblerIds.Contains(assemblerId);

    public override bool ShouldInterceptLab(FactorySystem factorySystem, int labId)
      => factorySystem == simulationFactory.factorySystem && labIds.Contains(labId);

    public override void LogStationBefore()
    {
      var profilingData = profilingTsData.LevelEntryOffset(0, profilingTick).Slice(stationOffset, stationSize);
      var curStationOffset = 0;
      for (int i = 0; i < stationIds.Count; i++)
      {
        ref readonly var station = ref simulationFactory.transport.stationPool[stationIds[i]];

        for (int j = 0; j < station.storage.Length; j++)
        {
          station.storage[j].count = station.storage[j].max / 2;
          profilingData[curStationOffset++] = station.storage[j].count;
        }
      }
    }

    public override void LogStationAfter()
    {
      var profilingData = profilingTsData.LevelEntryOffset(0, profilingTick).Slice(stationOffset, stationSize);
      var curStationOffset = 0;
      for (int i = 0; i < stationIds.Count; i++)
      {
        ref readonly var station = ref simulationFactory.transport.stationPool[stationIds[i]];
        for (int j = 0; j < station.storage.Length; j++)
          profilingData[curStationOffset++] -= station.storage[j].count;
      }
    }

    public override void DoInserterAdaptiveStacking()
    {
      if (adaptiveStacking || forceNoStacking)
      {
        for (int i = 0; i < inserterIds.Count; i++)
        {
          ref var inserter = ref simulationFactory.factorySystem.inserterPool[inserterIds[i]];
          PlanetFactorySimulation.DoAdaptiveStacking(ref inserter, simulationFactory, forceNoStacking);
        }
      }
    }

    public override float Progress => this.profilingTick / (float)(profilingTickCount * 40);
    public override string ProgressText => $"{profilingTick} / {profilingTickCount * 40}";
  }
}