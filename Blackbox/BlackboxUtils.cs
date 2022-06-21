namespace DysonSphereProgram.Modding.Blackbox;

public static class BlackboxUtils
{
  public static int QueryBlackboxedEntityForBlackboxId(PlanetFactory factory, in EntityData entity)
  {
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

    return blackboxId;
  }

  public static Blackbox QueryEntityForAssociatedBlackbox(PlanetFactory factory, int entityId)
  {
    if (entityId <= 0)
      return null;
    ref readonly var entity = ref factory.entityPool[entityId];
    var blackboxId = QueryBlackboxedEntityForBlackboxId(factory, in entity);
    var stationId = entity.stationId;

    if (blackboxId <= 0 && stationId <= 0)
      return null;
      
    var blackbox =
      stationId > 0
        ? BlackboxManager.Instance.blackboxes.Find(x => x.Selection.stationIds.Contains(stationId) && x.FactoryIndex == factory.index)
        : BlackboxManager.Instance.blackboxes.Find(x => x.Id == blackboxId);
    
    return blackbox;
  }
}