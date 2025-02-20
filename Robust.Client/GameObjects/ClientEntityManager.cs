﻿using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Client.Interfaces.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Client.GameObjects
{
    /// <summary>
    /// Manager for entities -- controls things like template loading and instantiation
    /// </summary>
    public sealed class ClientEntityManager : EntityManager, IClientEntityManager, IDisposable
    {
#pragma warning disable 649
        [Dependency] private readonly IMapManager _mapManager;
#pragma warning restore 649

        private int NextClientEntityUid = EntityUid.ClientUid + 1;

        public IEnumerable<IEntity> GetEntitiesInRange(GridCoordinates position, float Range)
        {
            var AABB = new Box2(position.Position - new Vector2(Range / 2, Range / 2), position.Position + new Vector2(Range / 2, Range / 2));
            return GetEntitiesIntersecting(_mapManager.GetGrid(position.GridID).ParentMapId, AABB);
        }

        public IEnumerable<IEntity> GetEntitiesIntersecting(MapId mapId, Box2 position)
        {
            foreach (var entity in GetEntities())
            {
                var transform = entity.Transform;
                if (transform.MapID != mapId)
                    continue;

                if (entity.TryGetComponent<BoundingBoxComponent>(out var component))
                {
                    if (position.Intersects(component.WorldAABB))
                        yield return entity;
                }
                else
                {
                    if (position.Contains(transform.WorldPosition))
                    {
                        yield return entity;
                    }
                }
            }
        }

        public IEnumerable<IEntity> GetEntitiesIntersecting(MapId mapId, Vector2 position)
        {
            foreach (var entity in GetEntities())
            {
                var transform = entity.Transform;
                if (transform.MapID != mapId)
                    continue;

                if (entity.TryGetComponent<BoundingBoxComponent>(out var component))
                {
                    if (component.WorldAABB.Contains(position))
                        yield return entity;
                }
                else
                {
                    if (FloatMath.CloseTo(transform.GridPosition.X, position.X) && FloatMath.CloseTo(transform.GridPosition.Y, position.Y))
                    {
                        yield return entity;
                    }
                }
            }
        }

        public bool AnyEntitiesIntersecting(MapId mapId, Box2 box)
        {
            foreach (var entity in GetEntities())
            {
                var transform = entity.Transform;
                if (transform.MapID != mapId)
                    continue;

                if (entity.TryGetComponent<BoundingBoxComponent>(out var component))
                {
                    if (box.Intersects(component.WorldAABB))
                        return true;
                }
                else
                {
                    if (box.Contains(transform.WorldPosition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public override void Startup()
        {
            base.Startup();

            if (Started)
            {
                throw new InvalidOperationException("Startup() called multiple times");
            }

            EntitySystemManager.Initialize();
            Started = true;
        }

        public void ApplyEntityStates(List<EntityState> curEntStates, IEnumerable<EntityUid> deletions, List<EntityState> nextEntStates)
        {
            var toApply = new Dictionary<IEntity, (EntityState, EntityState)>();
            var toInitialize = new List<Entity>();
            deletions = deletions ?? new EntityUid[0];

            if (curEntStates != null && curEntStates.Count != 0)
            {
                foreach (var es in curEntStates)
                {
                    //Known entities
                    if (Entities.TryGetValue(es.Uid, out var entity))
                    {
                        toApply.Add(entity, (es, null));
                    }
                    else //Unknown entities
                    {
                        var metaState = (MetaDataComponentState)es.ComponentStates.First(c => c.NetID == NetIDs.META_DATA);
                        var newEntity = CreateEntity(metaState.PrototypeId, es.Uid);
                        toApply.Add(newEntity, (es, null));
                        toInitialize.Add(newEntity);
                    }
                }
            }

            if (nextEntStates != null && nextEntStates.Count != 0)
            {
                foreach (var es in nextEntStates)
                {
                    if (Entities.TryGetValue(es.Uid, out var entity))
                    {
                        if (toApply.TryGetValue(entity, out var state))
                        {
                            toApply[entity] = (state.Item1, es);
                        }
                        else
                        {
                            toApply[entity] = (null, es);
                        }
                    }
                }
            }

            // Make sure this is done after all entities have been instantiated.
            foreach (var kvStates in toApply)
            {
                var ent = kvStates.Key;
                ((Entity)ent).HandleEntityState(kvStates.Value.Item1, kvStates.Value.Item2);
            }

            foreach (var id in deletions)
            {
                DeleteEntity(id);
            }

            foreach (var entity in toInitialize)
            {
                InitializeEntity(entity);
            }

            foreach (var entity in toInitialize)
            {
                StartEntity(entity);
            }
        }

        public void Dispose()
        {
            Shutdown();
        }

        public override IEntity SpawnEntity(string protoName)
        {
            var ent = CreateEntity(protoName, NewClientEntityUid());
            InitializeAndStartEntity(ent);
            return ent;
        }

        public override IEntity ForceSpawnEntityAt(string entityType, GridCoordinates coordinates)
        {
            var entity = CreateEntity(entityType, NewClientEntityUid());
            entity.Transform.GridPosition = coordinates;
            InitializeAndStartEntity(entity);

            return entity;
        }

        public override IEntity ForceSpawnEntityAt(string entityType, Vector2 position, MapId argMap)
        {
            if (!_mapManager.TryGetMap(argMap, out var map))
            {
                map = _mapManager.DefaultMap;
            }

            return ForceSpawnEntityAt(entityType, new GridCoordinates(position, map.FindGridAt(position)));

        }

        public override bool TrySpawnEntityAt(string entityType, Vector2 position, MapId argMap, out IEntity entity)
        {
            // TODO: check collisions here?
            entity = ForceSpawnEntityAt(entityType, position, argMap);
            return true;
        }

        public override bool TrySpawnEntityAt(string entityType, GridCoordinates coordinates, out IEntity entity)
        {
            // TODO: check collisions here?
            entity = ForceSpawnEntityAt(entityType, coordinates);
            return true;
        }

        EntityUid NewClientEntityUid()
        {
            return new EntityUid(NextClientEntityUid++);
        }

    }
}
