﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.API.Common
{
    public class AnimationManager : IAnimationManager
    {
        protected ICoreAPI api;
        protected ICoreClientAPI capi;

        /// <summary>
        /// Are the animations dirty in this AnimationManager?
        /// </summary>
        public bool AnimationsDirty { get; set; }

        /// <summary>
        /// The animator for the animation manager.
        /// </summary>
        public IAnimator Animator { get; set; }

        /// <summary>
        /// The entity head controller for this animator.
        /// </summary>
        public EntityHeadController HeadController { get; set; }

        /// <summary>
        /// The list of currently active animations that should be playing
        /// </summary>
        public Dictionary<string, AnimationMetaData> ActiveAnimationsByAnimCode = new Dictionary<string, AnimationMetaData>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, AnimationMetaData> IAnimationManager.ActiveAnimationsByAnimCode => ActiveAnimationsByAnimCode;

        public List<AnimFrameCallback> Triggers;

        /// <summary>
        /// The entity attached to this Animation Manager.
        /// </summary>
        protected Entity entity;

        public AnimationManager()
        {
            
        }

        /// <summary>
        /// Initializes the Animation Manager.
        /// </summary>
        /// <param name="api">The Core API.</param>
        /// <param name="entity">The entity this manager is attached to.</param>
        public virtual void Init(ICoreAPI api, Entity entity)
        {
            this.api = api;
            this.entity = entity;
            capi = api as ICoreClientAPI;
        }



        public virtual bool IsAnimationActive(params string[] anims)
        {
            foreach (var val in anims)
            {
                if (ActiveAnimationsByAnimCode.ContainsKey(val)) return true;
            }

            return false;
        }

        /// <summary>
        /// If given animation is running, will set its progress to the first animation frame
        /// </summary>
        /// <param name="animCode"></param>
        public virtual void ResetAnimation(string animCode)
        {
            var state = Animator?.GetAnimationState(animCode);
            if (state != null)
            {
                state.EasingFactor = 0; 
                state.CurrentFrame = 0;
            }
        }

        /// <summary>
        /// Client: Starts given animation
        /// Server: Sends all active anims to all connected clients then purges the ActiveAnimationsByAnimCode list
        /// </summary>
        /// <param name="animdata"></param>
        public virtual bool StartAnimation(AnimationMetaData animdata)
        {
            AnimationMetaData activeAnimdata;

            // Already active, won't do anything
            if (ActiveAnimationsByAnimCode.TryGetValue(animdata.Animation, out activeAnimdata) && activeAnimdata == animdata) return false;

            if (animdata.Code == null)
            {
                throw new Exception("anim meta data code cannot be null!");
            }

            AnimationsDirty = true;
            ActiveAnimationsByAnimCode[animdata.Animation] = animdata;
            entity.UpdateDebugAttributes();

            return true;
        }


        /// <summary>
        /// Start a new animation defined in the entity config file. If it's not defined, it won't play.
        /// Use StartAnimation(AnimationMetaData animdata) to circumvent the entity config anim data.
        /// </summary>
        /// <param name="configCode">Anim config code, not the animation code!</param>
        public virtual bool StartAnimation(string configCode)
        {
            if (configCode == null) return false;

            AnimationMetaData animdata;

            if (entity.Properties.Client.AnimationsByMetaCode.TryGetValue(configCode, out animdata))
            {
                StartAnimation(animdata);

                return true;
            }

            return false;
        }


        /// <summary>
        /// Stops given animation
        /// </summary>
        /// <param name="code"></param>
        public virtual void StopAnimation(string code)
        {
            if (code == null || entity == null) return;
            
            if (entity.World.Side == EnumAppSide.Server)
            {
                AnimationsDirty = true;
            }

            if (!ActiveAnimationsByAnimCode.Remove(code) && ActiveAnimationsByAnimCode.Count > 0)
            {
                foreach (var val in ActiveAnimationsByAnimCode)
                {
                    if (val.Value.Code == code)
                    {
                        ActiveAnimationsByAnimCode.Remove(val.Key);
                        break;
                    }
                }
            }

            if (entity.World.EntityDebugMode)
            {
                entity.UpdateDebugAttributes();
            }
        }


        /// <summary>
        /// The event fired when the manager recieves the server animations.
        /// </summary>
        /// <param name="activeAnimations"></param>
        /// <param name="activeAnimationsCount"></param>
        /// <param name="activeAnimationSpeeds"></param>
        public virtual void OnReceivedServerAnimations(int[] activeAnimations, int activeAnimationsCount, float[] activeAnimationSpeeds)
        {
            HashSet<string> toKeep = new HashSet<string>();
            
            string active = "";
            int mask = ~(1 << 31); // Because I fail to get the sign bit transmitted correctly over the network T_T

            for (int i = 0; i < activeAnimationsCount; i++)
            {
                uint crc32 = (uint)(activeAnimations[i] & mask);

                AnimationMetaData animmetadata;
                if (entity.Properties.Client.AnimationsByCrc32.TryGetValue(crc32, out animmetadata))
                {
                    toKeep.Add(animmetadata.Animation);

                    if (ActiveAnimationsByAnimCode.ContainsKey(animmetadata.Code)) continue;
                    animmetadata.AnimationSpeed = activeAnimationSpeeds[i];

                    onReceivedServerAnimation(animmetadata);
                    continue;
                }

                Animation anim;
                if (entity.Properties.Client.LoadedShapeForEntity.AnimationsByCrc32.TryGetValue(crc32, out anim)) {

                    toKeep.Add(anim.Code);

                    if (ActiveAnimationsByAnimCode.ContainsKey(anim.Code)) continue;

                    string code = anim.Code == null ? anim.Name.ToLowerInvariant() : anim.Code;
                    active += ", " + code;
                    AnimationMetaData animmeta;
                    entity.Properties.Client.AnimationsByMetaCode.TryGetValue(code, out animmeta);

                    if (animmeta == null)
                    {
                        animmeta = new AnimationMetaData()
                        {
                            Code = code,
                            Animation = code,
                            CodeCrc32 = anim.CodeCrc32
                        };
                    }

                    animmeta.AnimationSpeed = activeAnimationSpeeds[i];

                    onReceivedServerAnimation(animmeta);
                }
            }


            if (entity.EntityId != (entity.World as IClientWorldAccessor).Player.Entity.EntityId)
            {
                string[] keys = ActiveAnimationsByAnimCode.Keys.ToArray();
                for (int i = 0; i < keys.Length; i++)
                {
                    string key = keys[i];
                    var animMeta = ActiveAnimationsByAnimCode[key];

                    if (!toKeep.Contains(key) && !animMeta.ClientSide)
                    {
                        AnimationMetaData animmetadata;
                        if (entity.Properties.Client.AnimationsByMetaCode.TryGetValue(key, out animmetadata))
                        {
                            if (animmetadata.TriggeredBy != null && animmetadata.WasStartedFromTrigger) continue;
                            var anim = entity.AnimManager.Animator;
                            var runningAnim = anim?.GetAnimationState(animmetadata.Code);
                            if (runningAnim != null && runningAnim.Active && runningAnim.Animation.OnAnimationEnd == EnumEntityAnimationEndHandling.EaseOut) continue; // Let the client ease out this animation
                        }

                        ActiveAnimationsByAnimCode.Remove(key);
                    }
                }
            }

        }

        protected virtual void onReceivedServerAnimation(AnimationMetaData animmetadata)
        {
            ActiveAnimationsByAnimCode[animmetadata.Animation] = animmetadata;
        }

        /// <summary>
        /// Serializes the slots contents to be stored in the SaveGame
        /// </summary>
        /// <param name="tree"></param>
        /// <param name="forClient"></param>
        public virtual void ToAttributes(ITreeAttribute tree, bool forClient)
        {
            if (Animator == null) return;

            ITreeAttribute animtree = new TreeAttribute();
            tree["activeAnims"] = animtree;

            foreach (var val in ActiveAnimationsByAnimCode)
            {
                if (val.Value.Code == null) val.Value.Code = val.Key; // ah wtf.

                if (!forClient && val.Value.Code != "die") continue;

                RunningAnimation anim = Animator.GetAnimationState(val.Value.Animation);
                if (anim != null)
                {
                    val.Value.StartFrameOnce = anim.CurrentFrame;
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    using (BinaryWriter writer = new BinaryWriter(ms))
                    {
                        val.Value.ToBytes(writer);
                    }

                    animtree[val.Key] = new ByteArrayAttribute(ms.ToArray());
                }

                val.Value.StartFrameOnce = 0;
            }
        }


        /// <summary>
        /// Loads the entity from a stored byte array from the SaveGame
        /// </summary>
        /// <param name="tree"></param>
        /// <param name="version"></param>
        public virtual void FromAttributes(ITreeAttribute tree, string version)
        {
            var animtree = tree["activeAnims"] as ITreeAttribute;
            if (animtree != null)
            {
                foreach (var val in animtree)
                {
                    byte[] data = (val.Value as ByteArrayAttribute).value;
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        using (BinaryReader reader = new BinaryReader(ms))
                        {
                            ActiveAnimationsByAnimCode[val.Key] = AnimationMetaData.FromBytes(reader, version);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The event fired at each server tick.
        /// </summary>
        /// <param name="dt"></param>
        public virtual void OnServerTick(float dt)
        {
            Animator?.OnFrame(ActiveAnimationsByAnimCode, dt);
            Animator.CalculateMatrices = !entity.Alive;
            runTriggers();
        }
        
        /// <summary>
        /// The event fired each time the client ticks.
        /// </summary>
        /// <param name="dt"></param>
        public virtual void OnClientFrame(float dt)
        {
            if (capi.IsGamePaused || Animator == null) return;

            if (HeadController != null)
            {
                HeadController.OnFrame(dt);
            }

            if (entity.IsRendered || !entity.Alive)
            {
                Animator.OnFrame(ActiveAnimationsByAnimCode, dt);
                runTriggers();
            }
        }

        public virtual void RegisterFrameCallback(AnimFrameCallback trigger)
        {
            if (Triggers == null) Triggers = new List<AnimFrameCallback>();
            Triggers.Add(trigger);
        }

        private void runTriggers()
        {
            if (Triggers == null) return;
            for (int i = 0; i < Triggers.Count; i++)
            {
                var trigger = Triggers[i];
                if (ActiveAnimationsByAnimCode.ContainsKey(trigger.Animation))
                {
                    var state = Animator.GetAnimationState(trigger.Animation);
                    if (state != null && state.CurrentFrame >= trigger.Frame)
                    {
                        Triggers.RemoveAt(i);
                        trigger.Callback();
                        i--;
                    }
                }
            }
                
        }

        /// <summary>
        /// Disposes of the animation manager.
        /// </summary>
        public void Dispose()
        {
           
        }

        public virtual void OnAnimationStopped(string code)
        {
            
        }
    }
}