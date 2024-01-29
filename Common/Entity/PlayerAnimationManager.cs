﻿using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Common
{
    public class PlayerAnimationManager : AnimationManager
    {
        public bool UseFpAnmations=true;

        bool useFpAnimSet => UseFpAnmations && api.Side == EnumAppSide.Client && capi.World.Player.Entity.EntityId == entity.EntityId && capi.World.Player.CameraMode == EnumCameraMode.FirstPerson;

        string fpEnding => UseFpAnmations && capi?.World.Player.CameraMode == EnumCameraMode.FirstPerson ? ((api as ICoreClientAPI)?.Settings.Bool["immersiveFpMode"] == true ? "-ifp" : "-fp") : "";

        EntityPlayer plrEntity;

        public override void Init(ICoreAPI api, Entity entity)
        {
            base.Init(api, entity);

            plrEntity = entity as EntityPlayer;
        }

        public override void OnClientFrame(float dt)
        {
            base.OnClientFrame(dt);

            if (useFpAnimSet)
            {
                plrEntity.TpAnimManager.OnClientFrame(dt);
            }
        }


        public override void ResetAnimation(string animCode)
        {
            base.ResetAnimation(animCode);
            base.ResetAnimation(animCode + "-ifp");
            base.ResetAnimation(animCode + "-fp");
        }

        public override bool StartAnimation(string configCode)
        {
            if (configCode == null) return false;

            if (useFpAnimSet)
            {
                plrEntity.TpAnimManager.StartAnimation(configCode);
            }

            AnimationMetaData animdata;

            if (useFpAnimSet && entity.Properties.Client.AnimationsByMetaCode.TryGetValue(configCode + fpEnding, out animdata))
            {
                StartAnimation(animdata);
                return true;
            }

            return base.StartAnimation(configCode);
        }

        public override bool StartAnimation(AnimationMetaData animdata)
        {
            if (useFpAnimSet && !animdata.Code.EndsWith(fpEnding))
            {
                plrEntity.TpAnimManager.StartAnimation(animdata);

                if (entity.Properties.Client.AnimationsByMetaCode.TryGetValue(animdata.Code + fpEnding, out var animdatafp))
                {
                    if (ActiveAnimationsByAnimCode.TryGetValue(animdatafp.Animation, out var activeAnimdata) && activeAnimdata == animdatafp) return false;
                    return base.StartAnimation(animdatafp);
                }
            }

            return base.StartAnimation(animdata);
        }

        public override void RegisterFrameCallback(AnimFrameCallback trigger)
        {
            if (useFpAnimSet && !trigger.Animation.EndsWith(fpEnding) && entity.Properties.Client.AnimationsByMetaCode.ContainsKey(trigger.Animation + fpEnding))
            {
                trigger.Animation += fpEnding;
            }
            base.RegisterFrameCallback(trigger);
        }

        public override void StopAnimation(string code)
        {
            if (code == null) return;

            if (api.Side == EnumAppSide.Client) (plrEntity.OtherAnimManager as PlayerAnimationManager).StopSelfAnimation(code);
            StopSelfAnimation(code);
        }

        public void StopSelfAnimation(string code)
        {
            string[] anims = new string[] { code, code + "-ifp", code + "-fp" };
            foreach (var anim in anims)
            {
                base.StopAnimation(anim);
            }
        }

        public override bool IsAnimationActive(params string[] anims)
        {
            if (useFpAnimSet)
            {
                foreach (var val in anims)
                {
                    if (ActiveAnimationsByAnimCode.ContainsKey(val + fpEnding)) return true;
                }
            }

            return base.IsAnimationActive(anims);
        }

        public bool IsAnimationActiveOrRunning(string anim)
        {
            if (anim == null || Animator == null) return false;
            return IsAnimationMostlyRunning(anim) || IsAnimationMostlyRunning(anim + fpEnding) || IsAnimationActive(anim);
        }

        protected bool IsAnimationMostlyRunning(string anim)
        {
            var ranim = Animator.GetAnimationState(anim);
            return ranim != null && ranim.Running; // && ranim.AnimProgress < 0.9;
        }

        protected override void onReceivedServerAnimation(AnimationMetaData animmetadata)
        {
            StartAnimation(animmetadata);
        }

        public override void OnReceivedServerAnimations(int[] activeAnimations, int activeAnimationsCount, float[] activeAnimationSpeeds)
        {
            base.OnReceivedServerAnimations(activeAnimations, activeAnimationsCount, activeAnimationSpeeds);
        }


        protected string lastActiveHeldReadyAnimation;
        protected string lastActiveRightHeldIdleAnimation;
        protected string lastActiveLeftHeldIdleAnimation;

        protected string lastActiveHeldHitAnimation;
        protected string lastActiveHeldUseAnimation;

        public string lastRunningHeldHitAnimation;
        public string lastRunningHeldUseAnimation;

        public void OnActiveSlotChanged(ItemSlot slot)
        {
            string beginholdAnim = slot.Itemstack?.Collectible?.GetHeldReadyAnimation(slot, entity, EnumHand.Right);
            if (beginholdAnim != null) StartHeldReadyAnim(beginholdAnim);
        }
        

        public void StartHeldReadyAnim(string heldReadyAnim, bool force = false)
        {
            if (!force && (IsHeldHitActive() || IsHeldUseActive())) return;
            
            if (lastActiveHeldReadyAnimation != null) StopAnimation(lastActiveHeldReadyAnimation);

            ResetAnimation(heldReadyAnim);
            StartAnimation(heldReadyAnim);

            lastActiveHeldReadyAnimation = heldReadyAnim;
        }
        

        public void StartHeldUseAnim(string animCode)
        {
            StopHeldReadyAnim();
            StopAnimation(lastActiveRightHeldIdleAnimation);
            StopAnimation(lastActiveHeldHitAnimation);
            StartAnimation(animCode);
            lastActiveHeldUseAnimation = animCode;
            lastRunningHeldUseAnimation = animCode;
        }

        public void StartHeldHitAnim(string animCode)
        {
            StopHeldReadyAnim();
            StopAnimation(lastActiveRightHeldIdleAnimation);
            StopAnimation(lastActiveHeldUseAnimation);
            StartAnimation(animCode);
            lastActiveHeldHitAnimation = animCode;
            lastRunningHeldHitAnimation= animCode;
        }

        public void StartRightHeldIdleAnim(string animCode)
        {
            StopAnimation(lastActiveRightHeldIdleAnimation);
            StopAnimation(lastActiveHeldUseAnimation);
            StartAnimation(animCode);
            lastActiveRightHeldIdleAnimation = animCode;
        }


        public void StartLeftHeldIdleAnim(string animCode)
        {
            StopAnimation(lastActiveLeftHeldIdleAnimation);
            StartAnimation(animCode);
            lastActiveLeftHeldIdleAnimation = animCode;
        }



        public void StopHeldReadyAnim()
        {
            if (!plrEntity.RightHandItemSlot.Empty)
            {
                if (plrEntity.RightHandItemSlot.Itemstack.ItemAttributes?.IsTrue("alwaysPlayHeldReady") == true) return;
            }
            StopAnimation(lastActiveHeldReadyAnimation);
            lastActiveHeldReadyAnimation = null;
        }

        public void StopHeldUseAnim()
        {
            StopAnimation(lastActiveHeldUseAnimation);
            lastActiveHeldUseAnimation = null;
        }

        public void StopHeldAttackAnim()
        {
            if (lastActiveHeldHitAnimation != null && entity.Properties.Client.AnimationsByMetaCode.TryGetValue(lastActiveHeldHitAnimation, out var animData))
            {
                if (animData.Attributes?.IsTrue("authorative") == true)
                {
                    if (IsHeldHitActive()) return;
                }
            }

            StopAnimation(lastActiveHeldHitAnimation);
            lastActiveHeldHitAnimation = null;
        }

        public void StopRightHeldIdleAnim()
        {
            StopAnimation(lastActiveRightHeldIdleAnimation);
            lastActiveRightHeldIdleAnimation = null;
        }
        public void StopLeftHeldIdleAnim()
        {
            StopAnimation(lastActiveLeftHeldIdleAnimation);
            lastActiveLeftHeldIdleAnimation = null;
        }


        public bool IsHeldHitAuthorative()
        {
            return IsAuthorative(lastActiveHeldHitAnimation);
        }

        private bool IsAuthorative(string anim)
        {
            if (anim == null) return false;
            if (entity.Properties.Client.AnimationsByMetaCode.TryGetValue(anim, out var animData))
            {
                return animData.Attributes?.IsTrue("authorative") == true;
            }

            return false;
        }

        public bool IsHeldUseActive()
        {
            return lastActiveHeldUseAnimation != null && IsAnimationActiveOrRunning(lastActiveHeldUseAnimation);
        }

        public bool IsHeldHitActive()
        {
            return lastActiveHeldHitAnimation != null && IsAnimationActiveOrRunning(lastActiveHeldHitAnimation);
        }

        public bool IsLeftHeldActive()
        {
            return lastActiveLeftHeldIdleAnimation != null && IsAnimationActiveOrRunning(lastActiveLeftHeldIdleAnimation);
        }

        public bool IsRightHeldActive()
        {
            return lastActiveRightHeldIdleAnimation != null && IsAnimationActiveOrRunning(lastActiveRightHeldIdleAnimation);
        }

        public bool IsRightHeldReadyActive()
        {
            return lastActiveHeldReadyAnimation != null && IsAnimationActiveOrRunning(lastActiveHeldReadyAnimation);
        }

        public bool HeldRightReadyAnimChanged(string nowHeldRightReadyAnim)
        {
            return lastActiveHeldReadyAnimation != null && nowHeldRightReadyAnim != lastActiveHeldReadyAnimation;
        }

        public bool HeldUseAnimChanged(string nowHeldRightUseAnim)
        {
            return lastActiveHeldUseAnimation != null && nowHeldRightUseAnim != lastActiveHeldUseAnimation;
        }

        public bool HeldHitAnimChanged(string nowHeldRightHitAnim)
        {
            return lastActiveHeldHitAnimation != null && nowHeldRightHitAnim != lastActiveHeldHitAnimation;
        }

        public bool RightHeldIdleChanged(string nowHeldRightIdleAnim)
        {
            return lastActiveRightHeldIdleAnimation != null && nowHeldRightIdleAnim != lastActiveRightHeldIdleAnimation;
        }

        public bool LeftHeldIdleChanged(string nowHeldLeftIdleAnim)
        {
            return lastActiveLeftHeldIdleAnimation != null && nowHeldLeftIdleAnim != lastActiveLeftHeldIdleAnimation;
        }


        public override void FromAttributes(ITreeAttribute tree, string version)
        {
            base.FromAttributes(tree, version);

            lastActiveHeldUseAnimation = tree.GetString("lrHeldUseAnim");
            lastActiveHeldHitAnimation = tree.GetString("lrHeldHitAnim");
            // Can we not have this line? It breaks fp hands when loading up with a world with a block in hands - the shoulds of the hands become visible when walking and looking down
            //lastRunningRightHeldIdleAnimation = tree.GetString("lrRightHeldIdleAnim");
        }

        public override void ToAttributes(ITreeAttribute tree, bool forClient)
        {
            base.ToAttributes(tree, forClient);

            if (lastActiveHeldUseAnimation != null)
            {
                tree.SetString("lrHeldUseAnim", lastActiveHeldUseAnimation);
            }
            if (lastActiveHeldHitAnimation != null)
            {
                tree.SetString("lrHeldHitAnim", lastActiveHeldHitAnimation);
            }
            if (lastActiveRightHeldIdleAnimation != null)
            {
                tree.SetString("lrRightHeldIdleAnim", lastActiveRightHeldIdleAnimation);
            }
        }

    }
}
