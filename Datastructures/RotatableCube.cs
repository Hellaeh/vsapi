﻿using Vintagestory.API.MathTools;

namespace Vintagestory.API.Datastructures
{
    public class RotatableCube : Cuboidf
    {
        public float RotateX = 0;
        public float RotateY = 0;
        public float RotateZ = 0;

        public Vec3d Origin = new Vec3d(0.5, 0.5, 0.5);


        public RotatableCube()
        {

        }

        public Cuboidi ToHitboxCuboidi(float rotateY, Vec3d origin = null)
        {
            return RotatedCopy(0, rotateY, 0, origin ?? new Vec3d(8,8,8)).ConvertToCuboidi();
        }

        public RotatableCube(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) : base(MinX, MinY, MinZ, MaxX, MaxY, MaxZ)
        {

        }

        public Cuboidf RotatedCopy()
        {
            return RotatedCopy(RotateX, RotateY, RotateZ, Origin);
        }

        public new RotatableCube Clone()
        {
            RotatableCube cloned = (RotatableCube)MemberwiseClone();
            return cloned;
        }

        
    }
}
