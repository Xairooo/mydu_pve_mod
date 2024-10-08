﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mod.DynamicEncounters.Common.Vector;
using Mod.DynamicEncounters.Features.Common.Interfaces;
using Mod.DynamicEncounters.Features.Common.Services;
using Mod.DynamicEncounters.Features.Scripts.Actions.Interfaces;
using Mod.DynamicEncounters.Features.Sector.Services;
using Mod.DynamicEncounters.Features.Spawner.Behaviors.Interfaces;
using Mod.DynamicEncounters.Features.Spawner.Data;
using Mod.DynamicEncounters.Helpers;
using NQ;
using NQ.Interfaces;
using NQutils.Def;
using Orleans;

namespace Mod.DynamicEncounters.Features.Spawner.Behaviors;

public class SelectTargetBehavior(ulong constructId, IPrefab prefab) : IConstructBehavior
{
    private bool _active = true;
    private IClusterClient _orleans;
    private ILogger<SelectTargetBehavior> _logger;
    private IConstructGrain _constructGrain;
    private IConstructService _constructService;
    private IConstructElementsService _constructElementsService;

    public bool IsActive() => _active;

    public BehaviorTaskCategory Category => BehaviorTaskCategory.MediumPriority;

    public async Task InitializeAsync(BehaviorContext context)
    {
        var provider = context.ServiceProvider;

        _orleans = provider.GetOrleans();
        _logger = provider.CreateLogger<SelectTargetBehavior>();
        _constructGrain = _orleans.GetConstructGrain(constructId);
        _constructService = provider.GetRequiredService<IConstructService>();
        _constructElementsService = provider.GetRequiredService<IConstructElementsService>();
        
        var radarElementId = (await _constructElementsService.GetPvpRadarElements(constructId)).FirstOrDefault();
        var gunnerSeatElementId = (await _constructElementsService.GetPvpSeatElements(constructId)).FirstOrDefault();

        context.ExtraProperties.TryAdd("RADAR_ID", radarElementId.elementId);
        context.ExtraProperties.TryAdd("SEAT_ID", gunnerSeatElementId.elementId);
    }

    public async Task TickAsync(BehaviorContext context)
    {
        if (!context.IsAlive)
        {
            _active = false;
            return;
        }

        var targetSpan = DateTime.UtcNow - context.TargetSelectedTime;
        if (targetSpan < TimeSpan.FromSeconds(10))
        {
            context.TargetMovePosition = await GetTargetMovePosition(context);
            
            return;
        }

        var sw = new Stopwatch();
        sw.Start();

        _logger.LogInformation("Construct {Construct} Selecting a new Target", constructId);

        if (!context.Position.HasValue)
        {
            var npcConstructInfo = await _constructService.GetConstructInfoAsync(constructId);
            if (npcConstructInfo == null)
            {
                return;
            }
            
            context.Position = npcConstructInfo.rData.position;
        }
        
        var npcPos = context.Position.Value;

        var sectorPos = npcPos.GridSnap(SectorPoolManager.SectorGridSnap);
        var sectorGrid = new LongVector3(sectorPos);

        var foundValue = SectorGridConstructCache.Data.TryGetValue(sectorGrid, out var constructsOnSectorBag);
        if (!foundValue)
        {
            constructsOnSectorBag = [];
        }

        var constructsOnSector = constructsOnSectorBag.ToHashSet();
        
        // var constructsOnSector = await _spatialHashRepo.FindPlayerLiveConstructsOnSector(sectorPos);

        var result = new List<ConstructInfo>();
        foreach (var id in constructsOnSector)
        {
            try
            {
                result.Add(await _constructService.GetConstructInfoAsync(id));
            }
            catch (Exception)
            {
                _logger.LogError("Failed to fetch construct info for {Construct}", id);
            }
        }

        var playerConstructs = result
            .Where(r => r.mutableData.ownerId.IsPlayer() || r.mutableData.ownerId.IsOrg())
            .ToList();

        _logger.LogInformation("Construct {Construct} Found {Count} PLAYER constructs around {List}. Time = {Time}ms",
            constructId,
            playerConstructs.Count,
            string.Join(", ", playerConstructs.Select(x => x.rData.constructId)),
            sw.ElapsedMilliseconds
        );

        ulong targetId = 0;
        var distance = double.MaxValue;
        int maxIterations = 10;
        int counter = 0;

        var targetingDistance = 5 * DistanceHelpers.OneSuInMeters;

        foreach (var construct in playerConstructs)
        {
            if (counter > maxIterations)
            {
                break;
            }

            // Adds to the list of players involved
            if (construct.mutableData.pilot.HasValue)
            {
                context.PlayerIds.TryAdd(
                    construct.mutableData.pilot.Value.id,
                    construct.mutableData.pilot.Value.id
                );
            }
            
            var pos = construct.rData.position;

            var delta = Math.Abs(pos.Distance(npcPos));

            _logger.LogInformation("Construct {Construct} Distance: {Distance}su. Time = {Time}ms", 
                construct.rData.constructId, 
                delta / DistanceHelpers.OneSuInMeters,
                sw.ElapsedMilliseconds
            );

            if (delta > targetingDistance)
            {
                continue;
            }

            if (delta < distance)
            {
                distance = delta;
                targetId = construct.rData.constructId;
            }

            counter++;
        }

        context.TargetConstructId = targetId == 0 ? null : targetId;
        context.TargetSelectedTime = DateTime.UtcNow;

        if (!context.TargetConstructId.HasValue)
        {
            return;
        }
        
        var targetMovePositionTask = GetTargetMovePosition(context);
        var cacheTargetElementPositionsTask = CacheTargetElementPositions(context);

        await Task.WhenAll([targetMovePositionTask, cacheTargetElementPositionsTask]);

        context.TargetMovePosition = await targetMovePositionTask;
        
        _logger.LogInformation("Selected a new Target: {Target}; {Time}ms", targetId, sw.ElapsedMilliseconds);

        if (!context.ExtraProperties.TryGetValue<ulong>("RADAR_ID", out var radarElementId))
        {
            return;
        }

        if (!context.ExtraProperties.TryGetValue<ulong>("SEAT_ID", out var seatElementId))
        {
            return;
        }

        try
        {
            var constructInfo = await _constructService.GetConstructInfoAsync(context.TargetConstructId.Value);
            if (constructInfo == null)
            {
                return;
            }

            var identifyTask = _orleans.GetRadarGrain(radarElementId)
                .IdentifyStart(ModBase.Bot.PlayerId, new RadarIdentifyTarget
                {
                    playerId = constructInfo.mutableData.pilot ?? 0,
                    sourceConstructId = constructId,
                    targetConstructId = context.TargetConstructId.Value,
                    sourceRadarElementId = radarElementId,
                    sourceSeatElementId = seatElementId
                });

            var pilotTakeOverTask = PilotingTakeOverAsync();

            await Task.WhenAll([identifyTask, pilotTakeOverTask]);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to Identity Target");
        }
    }

    private async Task CacheTargetElementPositions(BehaviorContext context)
    {
        if (!context.TargetConstructId.HasValue)
        {
            return;
        }
        
        var constructElementsGrain = _orleans.GetConstructElementsGrain(context.TargetConstructId.Value);
        var elements = (await constructElementsGrain.GetElementsOfType<ConstructElement>()).ToList();
        
        var elementInfoListTasks = elements
            .Select(constructElementsGrain.GetElement);

        var elementInfoList = await Task.WhenAll(elementInfoListTasks);
        context.TargetElementPositions = elementInfoList.Select(x => x.position);
    }

    private async Task PilotingTakeOverAsync()
    {
        if (!await _constructService.IsBeingControlled(constructId))
        {
            await _constructGrain.PilotingTakeOver(ModBase.Bot.PlayerId, true);
        }
    }

    private async Task<Vec3> GetTargetMovePosition(BehaviorContext context)
    {
        if (!context.TargetConstructId.HasValue)
        {
            return new Vec3();
        }
        
        var targetConstructInfo = await _constructService.GetConstructInfoAsync(context.TargetConstructId.Value);
        if (targetConstructInfo == null)
        {
            _logger.LogError("Construct {Construct} Target construct info {Target} is null", constructId, context.TargetConstructId.Value);
            return new Vec3();
        }
        
        var distanceGoal = prefab.DefinitionItem.TargetDistance;
        var offset = new Vec3 { y = distanceGoal };
        
        return targetConstructInfo.rData.position + offset;
    }
}