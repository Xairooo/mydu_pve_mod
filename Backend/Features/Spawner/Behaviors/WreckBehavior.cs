﻿using System.Threading.Tasks;
using Mod.DynamicEncounters.Features.Spawner.Behaviors.Interfaces;
using Mod.DynamicEncounters.Features.Spawner.Data;

namespace Mod.DynamicEncounters.Features.Spawner.Behaviors;

public class WreckBehavior : IConstructBehavior
{
    public bool IsActive()
    {
        return false;
    }

    public Task InitializeAsync(BehaviorContext context)
    {
        return Task.CompletedTask;
    }

    public Task TickAsync(BehaviorContext context)
    {
        context.IsActiveWreck = true; //TODO
        return Task.CompletedTask;
    }
}