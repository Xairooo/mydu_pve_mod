﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mod.DynamicEncounters.Features.Common.Interfaces;
using NQ;

namespace Mod.DynamicEncounters.Features.Common.Services;

public class CachedConstructElementsService(
    IConstructElementsService service, 
    TimeSpan expirationTimeSpan,
    TimeSpan coreUnitCacheTimeSpan,
    TimeSpan powerCheckTimeSpan
)
    : IConstructElementsService
{
    private readonly TemporaryMemoryCache<ulong, IEnumerable<ElementId>> _pvpRadarUnits = new(expirationTimeSpan);
    private readonly TemporaryMemoryCache<ulong, IEnumerable<ElementId>> _weaponUnits = new(expirationTimeSpan);
    private readonly TemporaryMemoryCache<ulong, IEnumerable<ElementId>> _pvpSeatUnits = new(expirationTimeSpan);
    private readonly TemporaryMemoryCache<ulong, IEnumerable<ElementId>> _spaceEngineUnits = new(powerCheckTimeSpan);
    private readonly TemporaryMemoryCache<ulong, double> _spaceEnginePowers = new(powerCheckTimeSpan);
    private readonly TemporaryMemoryCache<ulong, int> _functionalWeaponCount = new(powerCheckTimeSpan);
    private readonly TemporaryMemoryCache<ulong, ElementId> _coreUnits = new(coreUnitCacheTimeSpan);
    private readonly TemporaryMemoryCache<ulong, ElementInfo> _elementInfos = new(expirationTimeSpan);

    public Task<IEnumerable<ElementId>> GetPvpRadarElements(ulong constructId)
    {
        return _pvpRadarUnits.TryGetValue(
            constructId,
            () => service.GetPvpRadarElements(constructId),
            ids => !ids.Any()
        );
    }

    public Task<IEnumerable<ElementId>> GetPvpSeatElements(ulong constructId)
    {
        return _pvpSeatUnits.TryGetValue(
            constructId,
            () => service.GetPvpSeatElements(constructId),
            ids => !ids.Any()
        );
    }

    public Task<IEnumerable<ElementId>> GetWeaponUnits(ulong constructId)
    {
        return _weaponUnits.TryGetValue(
            constructId,
            () => service.GetWeaponUnits(constructId),
            ids => !ids.Any()
        );
    }

    public Task<IEnumerable<ElementId>> GetSpaceEngineUnits(ulong constructId)
    {
        return _spaceEngineUnits.TryGetValue(
            constructId,
            () => service.GetSpaceEngineUnits(constructId),
            ids => !ids.Any()
        );
    }

    public Task<double> GetAllSpaceEnginesPower(ulong constructId)
    {
        return _spaceEnginePowers.TryGetValue(
            constructId,
            () => service.GetAllSpaceEnginesPower(constructId),
            val => val <= 0
        );
    }

    public Task<int> GetFunctionalDamageWeaponCount(ulong constructId)
    {
        return _functionalWeaponCount.TryGetValue(
            constructId,
            () => service.GetFunctionalDamageWeaponCount(constructId),
            val => val <= 0
        );
    }

    public Task<ElementInfo> GetElement(ulong constructId, ElementId elementId)
    {
        return _elementInfos.TryGetValue(
            constructId,
            () => service.GetElement(constructId, elementId),
            info => info == null
        );
    }

    public Task<ElementId> GetCoreUnit(ulong constructId)
    {
        return _coreUnits.TryGetValue(
            constructId,
            () => service.GetCoreUnit(constructId)
        );
    }

    public IConstructElementsService NoCache()
    {
        return service;
    }
}