using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class UnityMessageNames
{
    // Keep in sync with the Unity-message set that PR-5 will document in
    // Packages/src/Editor/FirstPartyTools/HotReload/Skill/SKILL.md.
    public const string AddedMessageWarningFormat =
        "Added Unity message '{0}' on {1} will not be invoked by the engine until 'uloop compile'; "
        + "Unity discovers messages by reflection on the compiled type.";

    private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "Awake",
        "Start",
        "OnEnable",
        "OnDisable",
        "OnDestroy",
        "Update",
        "LateUpdate",
        "FixedUpdate",
        "OnGUI",
        "Reset",
        "OnValidate",
        "OnCollisionEnter",
        "OnCollisionExit",
        "OnCollisionStay",
        "OnTriggerEnter",
        "OnTriggerExit",
        "OnTriggerStay",
        "OnCollisionEnter2D",
        "OnCollisionExit2D",
        "OnCollisionStay2D",
        "OnTriggerEnter2D",
        "OnTriggerExit2D",
        "OnTriggerStay2D",
        "OnMouseDown",
        "OnMouseUp",
        "OnMouseEnter",
        "OnMouseExit",
        "OnMouseOver",
        "OnMouseDrag",
        "OnBecameVisible",
        "OnBecameInvisible",
        "OnApplicationQuit",
        "OnApplicationPause",
        "OnApplicationFocus",
        "OnTransformChildrenChanged",
        "OnTransformParentChanged",
        "OnRectTransformDimensionsChange",
        "OnParticleCollision",
        "OnParticleTrigger",
        "OnControllerColliderHit",
        "OnJointBreak",
        "OnJointBreak2D",
        "OnAnimatorMove",
        "OnAnimatorIK",
        "OnDrawGizmos",
        "OnDrawGizmosSelected"
    };

    public static bool Contains(string methodName)
    {
        return methodName != null && Names.Contains(methodName);
    }
}
