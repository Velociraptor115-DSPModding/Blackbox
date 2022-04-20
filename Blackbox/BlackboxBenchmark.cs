using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DysonSphereProgram.Modding.Blackbox
{
  [StructLayout(LayoutKind.Sequential)]
  public struct ProduceConsumePair
  {
    public int Produced;
    public int Consumed;
  }

  public enum BenchmarkPhase
  {
    SpraySaturation,
    SelfSpraySaturation,
    ItemSaturation,
    Benchmarking,
    Averaging
  }

  public record struct StationStorageData(int stationId, int stationIdx, int storageIdx, ELogisticStorage effectiveLogic, int itemId, int itemIdx)
    : IComparable<StationStorageData>
  {
    public bool isSpray = sprayItemIds.Contains(itemId);
    public int CompareTo(StationStorageData other)
    {
      var stationIdxComparison = stationIdx.CompareTo(other.stationIdx);
      if (stationIdxComparison != 0)
        return stationIdxComparison;
      return storageIdx.CompareTo(other.storageIdx);
    }

    private static readonly int[] sprayItemIds = { 1141, 1142, 1143 };
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
    internal StationStorageData[] stationStorages;

    const int TicksPerSecond = 60;
    const int TicksPerMinute = TicksPerSecond * 60;

    public static bool logProfiledData = false;
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
    int stabilizedTick = 0;
    int spraySaturationTick = 0;

    int averagingDataSize;
    long[] averagingDataRaw;
    int[] averagingDataStats;
    int averagingDataStatsIdx;

    public static int analysisVerificationCountConfig = 4;
    public static int analysisDurationMultiplierConfig = 3;
    public static float averagingThresholdConfig = 0.0001f;
    public int analysisVerificationCount ;
    public int analysisDurationMultiplier;
    public float averagingThreshold;
    int timeSpendGCD;
    int timeSpendLCM;
    int timeSpendMaxIndividual;
    int profilingTickCount;
    int profilingEntryCount;
    int observedCycleLength;
    BenchmarkPhase phase;

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
      var tmp_stationStorages = ImmutableSortedSet.CreateBuilder<StationStorageData>();

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

      this.pcSize = (sizeof(long) / sizeof(int)) * 1;
      this.stationSize = 0;
      for (int i = 0; i < stationIds.Count; i++)
      {
        ref readonly var station = ref simulationFactory.transport.stationPool[stationIds[i]];
        for (int j = 0; j < station.storage.Length; j++)
        {
          ref readonly var stationStorage = ref station.storage[j];
          var effectiveLogic = stationStorage.remoteLogic == ELogisticStorage.None ? stationStorage.localLogic : stationStorage.remoteLogic;
          if (effectiveLogic != ELogisticStorage.None && stationStorage.itemId > 0)
          {
            var itemIdx = itemIds.IndexOf(stationStorage.itemId);
            tmp_stationStorages.Add(new StationStorageData(
              stationIds[i],
              i,
              j,
              effectiveLogic,
              stationStorage.itemId,
              itemIdx
            ));
          }
        }
      }
      this.stationStorages = tmp_stationStorages.ToImmutable().ToArray();
      this.stationSize = stationStorages.Length;
      this.factoryStatsSize = itemIds.Count * 2;
      this.stationStatsSize = itemIds.Count * 2;
      this.statsDiffSize = itemIds.Count;

      this.stationOffset = pcOffset + pcSize;
      this.factoryStatsOffset = stationOffset + stationSize;
      this.stationStatsOffset = factoryStatsOffset + factoryStatsSize;
      this.statsDiffOffset = stationStatsOffset + stationStatsSize;
      this.perTickProfilingSize = pcSize + stationSize + factoryStatsSize + stationStatsSize + statsDiffSize;

      this.totalStats = new ProduceConsumePair[itemIds.Count];

      this.analysisVerificationCount = analysisVerificationCountConfig;
      this.analysisDurationMultiplier = analysisDurationMultiplierConfig;
      this.averagingThreshold = averagingThresholdConfig;

      var distinctTimeSpends = tmp_assemblerTimeSpends.Distinct().DefaultIfEmpty(60).ToList();
      this.timeSpendGCD = Math.Max(60, Utils.GCD(distinctTimeSpends));
      this.timeSpendLCM = (int)Utils.LCM(Utils.LCM(distinctTimeSpends) * 4, timeSpendGCD);
      this.timeSpendMaxIndividual = (int)Utils.LCM(distinctTimeSpends.Max(), timeSpendGCD);
      this.profilingTickCount = timeSpendLCM * analysisVerificationCount;
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
      phase = spraycoaterIds.Count > 0 ? BenchmarkPhase.SpraySaturation : BenchmarkPhase.ItemSaturation;

      averagingDataSize = pcSize + stationSize + factoryStatsSize;
      averagingDataRaw = new long[averagingDataSize];
      averagingDataStats = new int[averagingDataSize * analysisVerificationCount];
      
      Plugin.Log.LogDebug("Profiling Mem Requirement: " + (profilingTsData.Data.Length * sizeof(int)));
      Plugin.Log.LogDebug("Aggregation ticks: " + timeSpendGCD);
      Plugin.Log.LogDebug("Per-cycle Analysis ticks: " + timeSpendLCM);

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
      continuousLogger.Write($"Phase,");
      continuousLogger.Write($"Tick,");
      continuousLogger.Write($"PC,");

      for (int i = 0; i < stationSize; i++)
      {
        var inout = stationStorages[i].effectiveLogic == ELogisticStorage.Demand ? "O" : "I";
        var itemId = stationStorages[i].itemId;
        var itemName = LDB.ItemName(itemId).Replace(" ", "").Trim();
        continuousLogger.Write($"S{i}_{inout}_{itemName},");
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
      var alternativeEntry =
        phase == BenchmarkPhase.Averaging
          ? new Span<int>(averagingDataStats, averagingDataStatsIdx % analysisVerificationCount, averagingDataSize)
          : entry;
      
      continuousLogger.Write(phase.ToString());
      continuousLogger.Write(',');
      continuousLogger.Write(profilingTick);
      continuousLogger.Write(',');

      var pcData = MemoryMarshal.Cast<int, long>(alternativeEntry.Slice(pcOffset, pcSize));
      long pcDataTotal = pcData[0];
      continuousLogger.Write(pcDataTotal);
      continuousLogger.Write(',');

      var stationData = alternativeEntry.Slice(stationOffset, stationSize);
      for (int i = 0; i < stationSize; i++)
      {
        continuousLogger.Write(stationData[i]);
        continuousLogger.Write(',');
      }

      var factoryStatsData = MemoryMarshal.Cast<int, ProduceConsumePair>(alternativeEntry.Slice(factoryStatsOffset, factoryStatsSize));
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

      if (phase == BenchmarkPhase.Averaging)
      {
        continuousLogger.Write("Averaging Total");
        continuousLogger.Write(',');
        continuousLogger.Write(profilingTick);
        continuousLogger.Write(',');
        
        continuousLogger.Write(averagingDataRaw[0]);
        continuousLogger.Write(',');

        var averagingDataRawSpan = new Span<long>(averagingDataRaw);
        
        var stationDataTotal = averagingDataRawSpan.Slice(stationOffset, stationSize);
        for (int i = 0; i < stationSize; i++)
        {
          continuousLogger.Write(stationData[i]);
          continuousLogger.Write(',');
        }

        var factoryStatsDataTotal = averagingDataRawSpan.Slice(factoryStatsOffset, factoryStatsSize);
        for (int i = 0; i < itemIds.Count; i++)
        {
          continuousLogger.Write(factoryStatsDataTotal[2 * i]);
          continuousLogger.Write(',');
          continuousLogger.Write(factoryStatsDataTotal[2 * i + 1]);
          continuousLogger.Write(',');
          continuousLogger.Write(0);
          continuousLogger.Write(',');
          continuousLogger.Write(0);
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
      for (int i = 0; i < stationSize; i++)
      {
        var itemIdx = stationStorages[i].itemIdx;
        var effectiveLogic = stationStorages[i].effectiveLogic;
        if (effectiveLogic == ELogisticStorage.Supply)
          stationStatsData[itemIdx].Consumed += -stationData[i];
        if (effectiveLogic == ELogisticStorage.Demand)
          stationStatsData[itemIdx].Produced += stationData[i];
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

    private void InitializeStabilizationData(int value)
    {
      for (int i = 0; i < stabilityDetectionData.Length; i++)
        stabilityDetectionData[i] = value;
    }

    private void CheckStabilization_Max()
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
    
    private void CheckStabilization_Min()
    {
      var entrySpan = profilingTsData.LevelEntryOffset(0, profilingTick);
      var totalStatsDiffSpan = entrySpan.Slice(statsDiffOffset, statsDiffSize);
      for (int i = 0; i < totalStatsDiffSpan.Length; i++)
        if (totalStatsDiffSpan[i] < stabilityDetectionData[i])
        {
          this.stabilizedTick = this.profilingTick;
          stabilityDetectionData[i] = totalStatsDiffSpan[i];
        }
    }

    private void ClearItemStats()
    {
      var pcSpan = profilingTsData.LevelEntryOffset(0, profilingTick).Slice(pcOffset, pcSize);
      pcSpan.Clear();
      var factoryStatsSpan = profilingTsData.LevelEntryOffset(0, profilingTick).Slice(factoryStatsOffset, factoryStatsSize);
      factoryStatsSpan.Clear();
      var stationStatsSpan = profilingTsData.LevelEntryOffset(0, profilingTick).Slice(stationStatsOffset, stationStatsSize);
      stationStatsSpan.Clear();
    }

    private void BoostSpraySaturation()
    {
      for (int i = 0; i < spraycoaterIds.Count; i++)
      {
        ref var spraycoater = ref simulationFactory.cargoTraffic.spraycoaterPool[spraycoaterIds[i]];
        if (spraycoater.incCount > 0)
        {
          spraycoater.incCount = spraycoater.incCapacity;
          spraycoater.extraIncCount = 0;
        }
      }
    }

    private void BoostProductionSaturation()
    {
      for (int i = 0; i < assemblerIds.Count; i++)
      {
        ref var assembler = ref simulationFactory.factorySystem.assemblerPool[assemblerIds[i]];
        if (assembler.replicating)
        {
          for (int j = 0; j < assembler.served.Length; j++)
          {
            assembler.served[j] = assembler.requireCounts[j] * 3;
          }
          for (int j = 0; j < assembler.produced.Length; j++)
          {
            assembler.produced[j] = assembler.productCounts[j] * 5;
          }
        }
      }
      
      for (int i = 0; i < labIds.Count; i++)
      {
        ref var lab = ref simulationFactory.factorySystem.labPool[labIds[i]];
        if (lab.replicating)
        {
          for (int j = 0; j < lab.served.Length; j++)
          {
            lab.served[j] = lab.requireCounts[j] * 3;
          }
          for (int j = 0; j < lab.produced.Length; j++)
          {
            lab.produced[j] = lab.productCounts[j] * 5;
          }
        }
      }
    }

    private void EndGameTick_SpraySaturation()
    {
      profilingTsData.SummarizeAtHigherGranularity(profilingTick);
      if (continuousLogging && (profilingTick + 1) % timeSpendGCD == 0)  WriteContinuousLoggingData(1);
      profilingTick += 1;
      ClearItemStats();
      
      BoostSpraySaturation();

      if (profilingTick % timeSpendGCD == 0)
      {
        Plugin.Log.LogDebug("Spray Saturation Tick: " + profilingTick);

        if (profilingTick > timeSpendGCD * analysisVerificationCount)
        {
          // Check if saturation occurred
          var endIndex = (profilingTick / timeSpendGCD) - 1;
          var circularOffset = 0;
          if (endIndex > profilingEntryCount)
          {
            circularOffset = (endIndex - profilingEntryCount) % profilingEntryCount;
            endIndex = profilingEntryCount - 1;
          }

          var saturated = true;
          for (int i = endIndex; saturated && i >= endIndex - analysisVerificationCount; i--)
          {
            var stationData = profilingTsData.Level(1).Entry((i + circularOffset) % profilingEntryCount).Slice(stationOffset, stationSize);
            for (int j = 0; j < stationSize; j++)
              if (stationData[j] != 0)
              {
                saturated = false;
                break;
              }
          }

          if (saturated)
          {
            spraySaturationTick = profilingTick;
            phase = BenchmarkPhase.SelfSpraySaturation;
            profilingTick = 0;
            ClearItemStats();
          }
        }
      }
      if (profilingTick >= totalTicks)
      {
        profilingTick = 0;
        Plugin.Log.LogDebug($"Analysis Failed");
        blackbox.NotifyAnalysisFailed();
      }
    }

    private void EndGameTick_SelfSpraySaturation()
    {
      profilingTick += 1;
      
      BoostSpraySaturation();
      
      if (profilingTick % timeSpendGCD == 0)
      {
        Plugin.Log.LogDebug("Self Spray Saturation Tick: " + profilingTick);
      }
      if (profilingTick < spraySaturationTick)
      {
        for (int i = 0; i < spraycoaterIds.Count; i++)
        {
          ref var spraycoater = ref simulationFactory.cargoTraffic.spraycoaterPool[spraycoaterIds[i]];
          spraycoater.incCount = 0;
          spraycoater.extraIncCount = 0;
        }
      }
      if (profilingTick >= spraySaturationTick * 2)
      {
        phase = BenchmarkPhase.ItemSaturation;
        profilingTick = 0;
        ClearItemStats();
      }
    }
    
    private void EndGameTick_ItemSaturation()
    {
      profilingTsData.SummarizeAtHigherGranularity(profilingTick);
      if (continuousLogging && (profilingTick + 1) % timeSpendGCD == 0)  WriteContinuousLoggingData(1);
      profilingTick += 1;
      ClearItemStats();
      
      BoostSpraySaturation();
      BoostProductionSaturation();

      if (profilingTick % timeSpendGCD == 0)
      {
        Plugin.Log.LogDebug("Item Saturation Tick: " + profilingTick);
        
      }
      if (profilingTick % timeSpendMaxIndividual == 0 && profilingTick > timeSpendMaxIndividual * analysisVerificationCount)
      {
        // Check if saturation occurred
        
        var endIndex = (profilingTick / timeSpendGCD) - 1;
        var circularOffset = 0;
        if (endIndex > profilingEntryCount)
        {
          circularOffset = (endIndex - profilingEntryCount) % profilingEntryCount;
          endIndex = profilingEntryCount - 1;
        }

        int beginIndex = ((timeSpendMaxIndividual / timeSpendGCD) + 1) * analysisVerificationCount;

        var saturated = true;
        for (int i = endIndex; saturated && i >= beginIndex; i--)
        {
          var stationData = profilingTsData.Level(1).Entry((i + circularOffset) % profilingEntryCount).Slice(stationOffset, stationSize);
          for (int j = 0; j < stationSize; j++)
            if (stationData[j] != 0)
            {
              saturated = false;
              break;
            }
        }

        if (saturated)
        {
          InitializeStabilizationData(int.MaxValue);
          phase = BenchmarkPhase.Benchmarking;
          profilingTick = 0;
          ClearItemStats();
        }
      }
      if (profilingTick >= totalTicks)
      {
        profilingTick = 0;
        Plugin.Log.LogDebug($"Analysis Failed");
        blackbox.NotifyAnalysisFailed();
      }
    }

    private void BenchmarkingCycleDetection(int circularOffset, out Func<int, int, bool> indexEquals, out Func<int, int, int, bool> summarizeEquals)
    {
      indexEquals = new Func<int, int, bool>((int i1, int i2) =>
      {
        var span1 = profilingTsData.Level(1).Entry((i1 + circularOffset) % profilingEntryCount);
        var span2 = profilingTsData.Level(1).Entry((i2 + circularOffset) % profilingEntryCount);

        for (int i = this.factoryStatsOffset; i < this.factoryStatsOffset + this.factoryStatsSize; i++)
          if (span1[i] != span2[i])
            return false;

        return true;
      });

      summarizeEquals = new Func<int, int, int, bool>((int i1, int i2, int stride) =>
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

        for (int i = this.factoryStatsOffset; i < this.factoryStatsOffset + this.factoryStatsSize; i++)
          if (span1Summary[i] != span2Summary[i])
            return false;

        return true;
      });
    }

    private void EndGameTick_Benchmarking()
    {
      LogItemStats();
      LogTotalItemStats();
      CheckStabilization_Min();
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

        BenchmarkingCycleDetection(circularOffset, out var indexEquals, out var summarizeEquals);

        if (CycleDetection.TryDetectCycles(endIndex, 0, analysisVerificationCount, indexEquals, summarizeEquals, out int cycleLength))
        {
          this.observedCycleLength = cycleLength * timeSpendGCD;
          Plugin.Log.LogDebug($"Cycle Length of {this.observedCycleLength} detected");
          profilingTick = 0;
          ClearItemStats();
          phase = BenchmarkPhase.Averaging;
        }
      }
      if (profilingTick >= totalTicks)
      {
        const int seconds120 = 120 * TicksPerSecond;
        observedCycleLength = Math.Min(timeSpendLCM, Math.Max(timeSpendMaxIndividual, seconds120));
        Plugin.Log.LogDebug($"Cycle Length of {this.observedCycleLength} assumed");
        profilingTick = 0;
        ClearItemStats();
        phase = BenchmarkPhase.Averaging;
      }
    }
    
    private void EndGameTick_Averaging()
    {
      LogItemStats();
      LogTotalItemStats();
      profilingTsData.SummarizeAtHigherGranularity(profilingTick);
      if (continuousLogging && (profilingTick + 1) % timeSpendGCD == 0)  WriteContinuousLoggingData(1);
      profilingTick += 1;
      ClearItemStats();

      if (profilingTick % timeSpendGCD == 0)
      {
        Plugin.Log.LogDebug("Averaging Tick: " + profilingTick);
        
        var endIndex = (profilingTick / timeSpendGCD) - 1;
        var circularOffset = 0;
        if (endIndex > profilingEntryCount)
        {
          circularOffset = (endIndex - profilingEntryCount) % profilingEntryCount;
          endIndex = profilingEntryCount - 1;
        }

        var multFactor = observedCycleLength;

        var dataSpan = profilingTsData.Level(1).Entry((endIndex + circularOffset) % profilingEntryCount);
        var averagingRawSpan = new Span<long>(averagingDataRaw);

        var effectiveIdx = averagingDataStatsIdx % analysisVerificationCount;
        var averagingSpan = new Span<int>(averagingDataStats, effectiveIdx * averagingDataSize, averagingDataSize);
        
        var pcCount = 1;
        var pcData = MemoryMarshal.Cast<int, long>(dataSpan.Slice(pcOffset, pcCount * 2));
        var averagingRawPcData = averagingRawSpan.Slice(pcOffset, pcCount);
        for (int i = 0; i < pcCount; i++)
          averagingRawPcData[i] += pcData[i];
        
        var stationData = dataSpan.Slice(stationOffset, stationSize);
        var averagingRawStationData = averagingRawSpan.Slice(stationOffset, stationSize);
        for (int i = 0; i < stationSize; i++)
          averagingRawStationData[i] += stationData[i];
        
        var factoryStatsData = dataSpan.Slice(factoryStatsOffset, factoryStatsSize);
        var averagingRawFactoryStatsData = averagingRawSpan.Slice(factoryStatsOffset, factoryStatsSize);
        for (int i = 0; i < factoryStatsSize; i++)
          averagingRawFactoryStatsData[i] += factoryStatsData[i];
        
        var averagingStatsPcData = MemoryMarshal.Cast<int, long>(averagingSpan.Slice(pcOffset, pcCount * 2));
        for (int i = 0; i < pcCount; i++)
          // averagingStatsPcData[i] = (long)Math.Ceiling((averagingRawPcData[i] * multFactor) / (double)profilingTick);
          averagingStatsPcData[i] = (long)Math.Round((averagingRawPcData[i] * multFactor) / (double)profilingTick);
        
        var averagingStatsStationData = averagingSpan.Slice(stationOffset, stationSize);
        for (int i = 0; i < stationSize; i++)
        {
          if (stationStorages[i].effectiveLogic == ELogisticStorage.Demand)
            // averagingStatsStationData[i] = (int)Math.Ceiling((averagingRawStationData[i] * multFactor) / (double)profilingTick);
            averagingStatsStationData[i] = (int)Math.Round((averagingRawStationData[i] * multFactor) / (double)profilingTick);
          else if (stationStorages[i].effectiveLogic == ELogisticStorage.Supply)
            // averagingStatsStationData[i] = (int)Math.Floor((-averagingRawStationData[i] * multFactor) / (double)profilingTick);
            averagingStatsStationData[i] = (int)Math.Round((-averagingRawStationData[i] * multFactor) / (double)profilingTick);
        }
        
        var averagingStatsFactoryStatsData = MemoryMarshal.Cast<int, ProduceConsumePair>(averagingSpan.Slice(factoryStatsOffset, factoryStatsSize));
        for (int i = 0; i < factoryStatsSize; i++)
        {
          var value = (averagingRawFactoryStatsData[i] * multFactor) / (double)profilingTick;
          if (i % 2 == 0)
            // averagingStatsFactoryStatsData[i / 2].Produced = (int)Math.Floor(value);
            averagingStatsFactoryStatsData[i / 2].Produced = (int)Math.Round(value);
          else
            // averagingStatsFactoryStatsData[i / 2].Consumed = (int)Math.Ceiling(value);
            averagingStatsFactoryStatsData[i / 2].Consumed = (int)Math.Round(value);
        }
        
        Plugin.Log.LogDebug(averagingRawPcData[0]);
        Plugin.Log.LogDebug(averagingStatsPcData[0]);

        averagingDataStatsIdx++;

        var stable = true;
        double maxRatio = 0;
        for (int i = 0; stable && i < analysisVerificationCount; i++)
        {
          var averagingSpanEach = new Span<int>(averagingDataStats, i * averagingDataSize, averagingDataSize);
          var averagingSpanEachPcData = MemoryMarshal.Cast<int, long>(averagingSpanEach.Slice(pcOffset, pcCount * 2));
          for (int j = 0; j < pcCount; j++)
          {
            var diff = Math.Abs(averagingStatsPcData[j] - averagingSpanEachPcData[j]);
            var threshold = averagingStatsPcData[j] * averagingThreshold;
            if (diff > (long)threshold)
            {
              stable = false;
              var ratio = diff / threshold;
              if (ratio > maxRatio)
                maxRatio = ratio;
            }
          }
            
          
          var averagingSpanItemStats = averagingSpan.Slice(stationOffset, stationSize + factoryStatsSize);
          var averagingSpanEachItemStats = averagingSpanEach.Slice(stationOffset, stationSize + factoryStatsSize);
          for (int j = 0; j < stationSize + factoryStatsSize; j++)
          {
            var diff = Math.Abs(averagingSpanItemStats[j] - averagingSpanEachItemStats[j]);
            var threshold = (int)(averagingSpanItemStats[j] * averagingThreshold);
            if (diff > threshold)
            {
              stable = false;
              var ratio = diff / (averagingSpanItemStats[j] * averagingThreshold);
              if (ratio > maxRatio)
                maxRatio = ratio;
            }
          }
        }

        if (stable)
        {
          Plugin.Log.LogDebug("Stable!");
          GenerateRecipe_V2();
          blackbox.NotifyBlackboxed(this.analysedRecipe);
        }
        else
        {
          Plugin.Log.LogDebug("Deviation: " + maxRatio);
        }
      }
      // if (profilingTick >= totalTicks)
      // {
      //   profilingTick = 0;
      //   Plugin.Log.LogDebug($"Analysis Failed");
      //   blackbox.NotifyAnalysisFailed();
      // }
    }
    public override void EndGameTick()
    {
      if (blackbox.Status == BlackboxStatus.InAnalysis)
      {
        switch (phase)
        {
          case BenchmarkPhase.SpraySaturation:
            EndGameTick_SpraySaturation();
            break;
          case BenchmarkPhase.SelfSpraySaturation:
            EndGameTick_SelfSpraySaturation();
            break;
          case BenchmarkPhase.ItemSaturation:
            EndGameTick_ItemSaturation();
            break;
          case BenchmarkPhase.Benchmarking:
            EndGameTick_Benchmarking();
            break;
          case BenchmarkPhase.Averaging:
            EndGameTick_Averaging();
            break;
        }
      }
    }
    
    void GenerateRecipe_V2()
    {
      long idleEnergyPerTick = 0;
      for (int i = 0; i < pcIds.Count; i++)
        idleEnergyPerTick += simulationFactory.powerSystem.consumerPool[pcIds[i]].idleEnergyPerTick;

      long idleEnergyPerCycle = idleEnergyPerTick * this.observedCycleLength;
      
      var averagingSpan = new Span<int>(averagingDataStats, 0, averagingDataSize);

      long workingEnergyPerCycle = 0;

      var pcData = MemoryMarshal.Cast<int, long>(averagingSpan.Slice(pcOffset, pcSize));
      foreach (var pc in pcData)
        workingEnergyPerCycle += pc;

      var tmp_stationStorageExit = new Dictionary<int, Dictionary<int, int>>();
      var tmp_stationStorageEnter = new Dictionary<int, Dictionary<int, int>>();
      var stationData = averagingSpan.Slice(stationOffset, stationSize);
      for (int i = 0; i < stationSize; i++)
      {
        var stationIdx = stationStorages[i].stationIdx;
        var itemId = stationStorages[i].itemId;
        var effectiveLogic = stationStorages[i].effectiveLogic;
        if (effectiveLogic == ELogisticStorage.Demand && stationData[i] != 0)
        {
          if (!tmp_stationStorageExit.ContainsKey(stationIdx))
            tmp_stationStorageExit[stationIdx] = new Dictionary<int, int>();
          tmp_stationStorageExit[stationIdx][itemId] = stationData[i];
        }
        else if (effectiveLogic == ELogisticStorage.Supply && stationData[i] != 0)
        {
          if (!tmp_stationStorageEnter.ContainsKey(stationIdx))
            tmp_stationStorageEnter[stationIdx] = new Dictionary<int, int>();
          tmp_stationStorageEnter[stationIdx][itemId] = stationData[i];
        }
      }

      var tmp_produces = new Dictionary<int, int>();
      var tmp_consumes = new Dictionary<int, int>();

      var factoryStatsSpan = MemoryMarshal.Cast<int, ProduceConsumePair>(averagingSpan.Slice(factoryStatsOffset, factoryStatsSize));

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
    
    public override bool ShouldInterceptSpraycoater(CargoTraffic cargoTraffic, int spraycoaterId)
      => cargoTraffic == simulationFactory.cargoTraffic && spraycoaterIds.Contains(spraycoaterId);

    public override void AdjustStationStorageCount()
    {
      for (int i = 0; i < stationSize; i++)
      {
        ref readonly var ss = ref stationStorages[i];
        ref var storage = ref simulationFactory.transport.stationPool[ss.stationId].storage[ss.storageIdx];
        var count = 0;
        var max = storage.max;
        
        switch (phase)
        {
          case BenchmarkPhase.SpraySaturation:
          case BenchmarkPhase.SelfSpraySaturation:
            {
              if (ss.effectiveLogic == ELogisticStorage.Demand)
                count = ss.isSpray ? max : 0;
              if (ss.effectiveLogic == ELogisticStorage.Supply)
                count = max;
              break;
            }
          case BenchmarkPhase.ItemSaturation:
            {
              if (ss.effectiveLogic == ELogisticStorage.Demand)
                count = max;
              if (ss.effectiveLogic == ELogisticStorage.Supply)
                count = max;
              break;
            }
          case BenchmarkPhase.Benchmarking:
          case BenchmarkPhase.Averaging:
            {
              if (ss.effectiveLogic == ELogisticStorage.Demand)
                count = max;
              if (ss.effectiveLogic == ELogisticStorage.Supply)
                count = 0;
              break;
            }
        }
        
        storage.count = count;
      }
    }

    public override void LogStationBefore()
    {
      var profilingData = profilingTsData.LevelEntryOffset(0, profilingTick).Slice(stationOffset, stationSize);
      for (int i = 0; i < stationSize; i++)
      {
        var stationId = stationStorages[i].stationId;
        var storageIdx = stationStorages[i].storageIdx;
        profilingData[i] = simulationFactory.transport.stationPool[stationId].storage[storageIdx].count;
      }
    }

    public override void LogStationAfter()
    {
      var profilingData = profilingTsData.LevelEntryOffset(0, profilingTick).Slice(stationOffset, stationSize);
      for (int i = 0; i < stationSize; i++)
      {
        var stationId = stationStorages[i].stationId;
        var storageIdx = stationStorages[i].storageIdx;
        profilingData[i] -= simulationFactory.transport.stationPool[stationId].storage[storageIdx].count;
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

    private int totalTicks => stabilizedTick + (profilingTickCount * analysisDurationMultiplier);
    public override float Progress => this.profilingTick / (float)(totalTicks);
    public override string ProgressText => $"{profilingTick} / {totalTicks}";
  }
}