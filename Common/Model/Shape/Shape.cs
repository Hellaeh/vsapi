﻿using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using System.Linq;
using Vintagestory.API.Util;
using System.Runtime.Serialization;

namespace Vintagestory.API.Common
{
    /// <summary>
    /// The base shape for all json objects.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Shape
    {
        /// <summary>
        /// The collection of textures in the shape.
        /// </summary>
        [JsonProperty]
        public Dictionary<string, AssetLocation> Textures;

        /// <summary>
        /// The elements of the shape.
        /// </summary>
        [JsonProperty]
        public ShapeElement[] Elements;

        /// <summary>
        /// The animations for the shape.
        /// </summary>
        [JsonProperty]
        public Animation[] Animations;

        public Dictionary<uint, Animation> AnimationsByCrc32 = new Dictionary<uint, Animation>();

        /// <summary>
        /// The width of the texture. (default: 16)
        /// </summary>
        [JsonProperty]
        public int TextureWidth = 16;

        /// <summary>
        /// The height of the texture (default: 16) 
        /// </summary>
        [JsonProperty]
        public int TextureHeight = 16;

        [JsonProperty]
        public Dictionary<string, int[]> TextureSizes = new Dictionary<string, int[]>();


        public Dictionary<int, AnimationJoint> JointsById = new Dictionary<int, AnimationJoint>();
        public Dictionary<string, AttachmentPoint> AttachmentPointsByCode = new Dictionary<string, AttachmentPoint>();


        [OnDeserialized]
        public void TrimTextureNamesAndResolveFaces(StreamingContext context)
        {
            foreach (ShapeElement el in Elements) el.TrimTextureNamesAndResolveFaces();
        }

        /// <summary>
        /// Attempts to resolve all references within the shape. Logs missing references them to the errorLogger
        /// </summary>
        /// <param name="errorLogger"></param>
        /// <param name="shapeNameForLogging"></param>
        public void ResolveReferences(ILogger errorLogger, string shapeNameForLogging)
        {
            Dictionary<string, ShapeElement> elementsByName = new Dictionary<string, ShapeElement>();
            CollectElements(Elements, elementsByName);

            for (int i = 0; Animations != null && i < Animations.Length; i++)
            {
                Animation anim = Animations[i];
                for (int j = 0; j < anim.KeyFrames.Length; j++)
                {
                    AnimationKeyFrame keyframe = anim.KeyFrames[j];
                    ResolveReferences(errorLogger, shapeNameForLogging, elementsByName, keyframe);

                    foreach (AnimationKeyFrameElement kelem in keyframe.Elements.Values)
                    {
                        kelem.Frame = keyframe.Frame;
                    }
                }

                if (anim.Code == null || anim.Code.Length == 0)
                {
                    anim.Code = anim.Name.ToLowerInvariant().Replace(" ", "");
                }

                AnimationsByCrc32[AnimationMetaData.GetCrc32(anim.Code)] = anim;
            }

            for (int i = 0; i < Elements.Length; i++)
            {
                ShapeElement elem = Elements[i];
                elem.ResolveRefernces();

                CollectAttachmentPoints(elem);
            }
        }

        private void CollectAttachmentPoints(ShapeElement elem)
        {
            if (elem.AttachmentPoints != null)
            {
                for (int j = 0; j < elem.AttachmentPoints.Length; j++)
                {
                    var point = elem.AttachmentPoints[j];
                    AttachmentPointsByCode[point.Code] = point;
                }
            }

            if (elem.Children != null)
            {
                for (int j = 0; j < elem.Children.Length; j++)
                {
                    CollectAttachmentPoints(elem.Children[j]);
                }
            }
        }

        private void ResolveReferences(ILogger errorLogger, string shapeName, Dictionary<string, ShapeElement> elementsByName, AnimationKeyFrame kf)
        {
            if (kf == null) return;

            foreach (var val in kf.Elements)
            {
                ShapeElement elem;
                elementsByName.TryGetValue(val.Key, out elem);

                if (elem == null)
                {
                    errorLogger.Error("Shape {0} has a key frame elmenent for which the referencing shape element {1} cannot be found.", shapeName, val.Key);

                    val.Value.ForElement = new ShapeElement();
                    continue;
                }

                val.Value.ForElement = elem;
            }
        }

        /// <summary>
        /// Collects all the elements in the shape recursively.
        /// </summary>
        /// <param name="elements"></param>
        /// <param name="elementsByName"></param>
        public void CollectElements(ShapeElement[] elements, Dictionary<string, ShapeElement> elementsByName)
        {
            if (elements == null) return;

            for (int i = 0; i < elements.Length; i++)
            {
                ShapeElement elem = elements[i];

                elementsByName[elem.Name] = elem;

                CollectElements(elem.Children, elementsByName);
            }
        }

        [Obsolete("Must call ResolveAndFindJoints(errorLogger, shapeName, joints) instead")]
        public void ResolveAndLoadJoints(params string[] requireJointsForElements)
        {
            ResolveAndFindJoints(null, null, requireJointsForElements);
        }

        /// <summary>
        /// Resolves all joints and loads them.
        /// </summary>
        /// <param name="requireJointsForElements"></param>
        public void ResolveAndFindJoints(ILogger errorLogger, string shapeName, params string[] requireJointsForElements)
        {
            if (Animations == null) return;

            Dictionary<string, ShapeElement> elementsByName = new Dictionary<string, ShapeElement>();
            CollectElements(Elements, elementsByName);

            ShapeElement[] allElements = elementsByName.Values.ToArray();
            
            int jointCount = 0;

            HashSet<string> AnimatedElements = new HashSet<string>();

            HashSet<string> animationCodes = new HashSet<string>();

            int version = -1;
            bool errorLogged = false;

            for (int i = 0; i < Animations.Length; i++)
            {
                Animation anim = Animations[i];

                if (animationCodes.Contains(anim.Code))
                {
                    errorLogger?.Warning("Shape {0}: Two or more animations use the same code '{1}'. This will lead to undefined behavior.", shapeName, anim.Code);
                }
                animationCodes.Add(anim.Code);

                if (version == -1) version = anim.Version;
                else if (version != anim.Version)
                {
                    if (!errorLogged) errorLogger?.Error("Shape {0} has mixed animation versions. This will cause incorrect animation blending.", shapeName);
                    errorLogged = true;
                }

                for (int j = 0; j < anim.KeyFrames.Length; j++)
                {
                    AnimationKeyFrame kf = anim.KeyFrames[j];
                    AnimatedElements.AddRange(kf.Elements.Keys.ToArray());

                    kf.Resolve(allElements);
                }
            }

            foreach (ShapeElement elem in elementsByName.Values)
            {
                elem.JointId = 0;
            }

            int maxDepth = 0;

            foreach (string code in AnimatedElements)
            {
                ShapeElement elem;
                elementsByName.TryGetValue(code, out elem);
                if (elem == null) continue;
                AnimationJoint joint = new AnimationJoint() { JointId = ++jointCount, Element = elem };
                JointsById[joint.JointId] = joint;
                
                maxDepth = Math.Max(maxDepth, elem.GetParentPath().Count);
            }

            // Currently used to require a joint for the head for head control, but not really used because
            // the player head also happens to be using in animations so it has a joint anyway
            foreach (string elemName in requireJointsForElements)
            {
                if (!AnimatedElements.Contains(elemName))
                {
                    ShapeElement elem = GetElementByName(elemName);
                    if (elem == null) continue;

                    AnimationJoint joint = new AnimationJoint() { JointId = ++jointCount, Element = elem };
                    JointsById[joint.JointId] = joint;
                    maxDepth = Math.Max(maxDepth, elem.GetParentPath().Count);
                }
            }
            


            // Iteratively and recursively assign the lowest depth to highest depth joints to all elements
            // prevents that we overwrite a child joint id with a parent joint id
            for (int depth = 0; depth <= maxDepth; depth++)
            {
                foreach (AnimationJoint joint in JointsById.Values)
                {
                    if (joint.Element.GetParentPath().Count != depth) continue;

                    joint.Element.SetJointId(joint.JointId);
                }
            }   
        }

        /// <summary>
        /// Tries to load the shape from the specified JSON file, with error logging
        /// <br/>Returns null if the file could not be found, or if there was an error
        /// </summary>
        /// <param name="api"></param>
        /// <param name="shapePath"></param>
        /// <returns></returns>
        public static Shape TryGet(ICoreAPI api, string shapePath)
        {
            ShapeElement.locationForLogging = shapePath;
            try
            {
                return api.Assets.TryGet(shapePath)?.ToObject<Shape>();
            }
            catch (Exception e)
            {
                api.World.Logger.Error("Exception thrown when trying to load shape file {0}", shapePath);
                api.World.Logger.Error(e);
                return null;
            }
        }

        /// <summary>
        /// Tries to load the shape from the specified JSON file, with error logging
        /// <br/>Returns null if the file could not be found, or if there was an error
        /// </summary>
        /// <param name="api"></param>
        /// <param name="shapePath"></param>
        /// <returns></returns>
        public static Shape TryGet(ICoreAPI api, AssetLocation shapePath)
        {
            ShapeElement.locationForLogging = shapePath;
            try
            {
                return api.Assets.TryGet(shapePath)?.ToObject<Shape>();
            }
            catch (Exception e)
            {
                api.World.Logger.Error("Exception thrown when trying to load shape file {0}\n{1}", shapePath, e.Message);
                return null;
            }
        }

        public void WalkElements(string wildcardpath, Action<ShapeElement> onElement)
        {
            walkElements(Elements, wildcardpath, onElement);
        }

        private void walkElements(ShapeElement[] elements, string wildcardpath, Action<ShapeElement> onElement)
        {
            if (elements == null) return;

            string pathElem;
            string subPath;

            int slashIndex = wildcardpath.IndexOf('/');
            if (slashIndex >= 0) {
                pathElem = wildcardpath.Substring(0, slashIndex);
                subPath = wildcardpath.Substring(slashIndex + 1);
            } else
            {
                pathElem = wildcardpath;
                subPath = "";
                if (pathElem == "*") subPath = "*";
            }

            foreach (ShapeElement elem in elements)
            {
                if (pathElem == "*" || elem.Name.Equals(pathElem, StringComparison.InvariantCultureIgnoreCase))
                {
                    onElement(elem);
                    if (elem.Children != null)
                    {
                        walkElements(elem.Children, subPath, onElement);
                    }
                }
            }
        }

        /// <summary>
        /// Recursively searches the element by name from the shape.
        /// </summary>
        /// <param name="name">The name of the element to get.</param>
        /// <returns>The shape element or null if none was found</returns>
        public ShapeElement GetElementByName(string name, StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
        {
            return GetElementByName(name, Elements, stringComparison);
        }

        ShapeElement GetElementByName(string name, ShapeElement[] elems, StringComparison stringComparison)
        {
            if (elems == null) return null;

            foreach (ShapeElement elem in elems)
            {
                if (elem.Name.Equals(name, stringComparison)) return elem;
                if (elem.Children != null)
                {
                    ShapeElement foundElem = GetElementByName(name, elem.Children, stringComparison);
                    if (foundElem != null) return foundElem;
                }
            }

            return null;
        }

        /// <summary>
        /// Removes *all* elements with given name
        /// </summary>
        /// <param name="name"></param>
        /// <param name="stringComparison"></param>
        /// <returns></returns>
        public bool RemoveElementByName(string name, StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
        {
            return RemoveElementByName(name, ref Elements, stringComparison);
        }


        bool RemoveElementByName(string name, ref ShapeElement[] elems, StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
        {
            if (elems == null) return false;

            bool removed=false;

            for (int i = 0; i < elems.Length; i++)
            {
                if (elems[i].Name.Equals(name, stringComparison))
                {
                    elems = elems.RemoveEntry(i);
                    removed = true;
                    i--;
                    continue;
                }

                if (elems[i].Children != null)
                {
                    if (RemoveElementByName(name, ref elems[i].Children, stringComparison))
                    {
                        removed = true;
                    }
                }
            }

            return removed;
        }

        public ShapeElement[] CloneElements()
        {
            if (Elements == null) return null;

            ShapeElement[] elems = new ShapeElement[Elements.Length];

            for (int i = 0; i < elems.Length; i++)
            {
                elems[i] = Elements[i].Clone();
            }

            return elems;
        }

        public Animation[] CloneAnimations()
        {
            if (Animations == null) return null;

            Animation[] elems = new Animation[Animations.Length];

            for (int i = 0; i < Animations.Length; i++)
            {
                elems[i] = Animations[i].Clone();
            }

            return elems;
        }


        /// <summary>
        /// Creates a deep copy of the shape
        /// </summary>
        /// <returns></returns>
        public Shape Clone()
        {
            return new Shape()
            {
                Elements = CloneElements(),
                Animations = CloneAnimations(),
                AnimationsByCrc32 = AnimationsByCrc32,
                AttachmentPointsByCode = AttachmentPointsByCode,
                JointsById = JointsById,
                TextureWidth = TextureWidth,
                TextureHeight = TextureHeight,
                TextureSizes = TextureSizes,
                Textures = Textures,
            };
        }

    }
}
