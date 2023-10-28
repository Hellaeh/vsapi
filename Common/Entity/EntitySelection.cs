﻿using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Common
{
    public class EntitySelection
    {
        /// <summary>
        /// The selected Entity.
        /// </summary>
        public Entity Entity;

        /// <summary>
        /// The position of the selected entity.
        /// </summary>
        public Vec3d Position;

        /// <summary>
        /// the facing of the entity.
        /// </summary>
        public BlockFacing Face;

        /// <summary>
        /// The hit position of the entity.
        /// </summary>
        public Vec3d HitPosition;


        public EntitySelection Clone()
        {
            return new EntitySelection()
            {
                Entity = Entity,
                Position = Position.Clone(),
                Face = Face,
                HitPosition = HitPosition.Clone()
            };
        }
    }

}
