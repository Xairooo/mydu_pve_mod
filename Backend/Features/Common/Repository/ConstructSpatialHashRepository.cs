﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Mod.DynamicEncounters.Common;
using Mod.DynamicEncounters.Common.Vector;
using Mod.DynamicEncounters.Database.Interfaces;
using Mod.DynamicEncounters.Features.Common.Interfaces;
using Mod.DynamicEncounters.Features.Sector.Services;
using Mod.DynamicEncounters.Helpers;
using NQ;

namespace Mod.DynamicEncounters.Features.Common.Repository;

public class ConstructSpatialHashRepository(IServiceProvider serviceProvider) : IConstructSpatialHashRepository
{
    private readonly IPostgresConnectionFactory _factory =
        serviceProvider.GetRequiredService<IPostgresConnectionFactory>();

    public async Task<IEnumerable<ulong>> FindPlayerLiveConstructsOnSector(Vec3 sector)
    {
        using var db = _factory.Create();
        db.Open();
        
        var result = (await db.QueryAsync<ulong>(
            $"""
            SELECT C.id FROM public.construct C
            LEFT JOIN public.ownership O ON (C.owner_entity_id = O.id)
            WHERE C.sector_x = @x AND C.sector_y = @y AND C.sector_z = @z AND
                  C.deleted_at IS NULL AND
                  C.owner_entity_id IS NOT NULL AND (O.player_id NOT IN({StaticPlayerId.Aphelia}, {StaticPlayerId.Unknown}) OR (O.player_id IS NULL AND O.organization_id IS NOT NULL))
            """,
            new
            {
                sector.x,
                sector.y,
                sector.z,
            }
        )).ToList();

        return result;
    }

    public async Task<IEnumerable<ConstructSectorRow>> FindPlayerLiveConstructsOnSectorInstances()
    {
        using var db = _factory.Create();
        db.Open();

        var result = (await db.QueryAsync<ConstructSectorRow>(
            $"""
             SELECT C.id, C.name, CH.sector_x, CH.sector_y, CH.sector_z FROM public.construct C
              INNER JOIN public.mod_npc_construct_handle CH ON (CH.sector_x = C.sector_x AND CH.sector_y = C.sector_y AND CH.sector_z = C.sector_z)
              LEFT JOIN public.ownership O ON (C.owner_entity_id = O.id)
              WHERE C.deleted_at IS NULL AND
              	C.owner_entity_id IS NOT NULL AND
              	CH.deleted_at IS NULL AND
             	C.owner_entity_id IS NOT NULL AND (
             		O.player_id NOT IN({StaticPlayerId.Aphelia}, {StaticPlayerId.Unknown}) OR 
             		(O.player_id IS NULL AND O.organization_id IS NOT NULL)
             	)
             """
        )).ToList();

        return result;
    }
    
    public struct ConstructSectorRow
    {
        public long id { get; set; }
        public string name { get; set; }
        public long sector_x { get; set; }
        public long sector_y { get; set; }
        public long sector_z { get; set; }

        public ulong ConstructId() => (ulong)id;
        public LongVector3 GetLongVector() => new(new Vec3{x = sector_x, y = sector_y, z = sector_z}.GridSnap(SectorPoolManager.SectorGridSnap));
    }
}