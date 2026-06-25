using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Serializes component properties to ComponentPropertyInfo format
    /// Related classes:
    /// - ComponentSerializer: Uses this to serialize properties
    /// </summary>
    public class ComponentPropertySerializer
    {
        
        public ComponentPropertyInfo[] SerializeProperties(Component component)
        {
            if (component == null)
                return new ComponentPropertyInfo[0];
                
            List<ComponentPropertyInfo> propertyInfos = new();
            
            // Use SerializedObject to get only Inspector-visible properties
            SerializedObject serializedObject = new(component);
            SerializedProperty iterator = serializedObject.GetIterator();

            if (iterator.NextVisible(true))
            {
                do
                {
                    // m_Script is an internal reference, not a user-facing property
                    if (iterator.name == "m_Script")
                        continue;

                    object value = GetSerializedPropertyValue(iterator);
                    if (value != null)
                    {
                        ComponentPropertyInfo info = new()                        {
                            Name = iterator.displayName,
                            Type = iterator.propertyType.ToString(),
                            Value = SerializeValue(value)
                        };

                        propertyInfos.Add(info);
                    }
                } while (iterator.NextVisible(false));
            }
            
            return propertyInfos.ToArray();
        }
        
        /// <summary>
        /// Extract value from SerializedProperty based on its type
        /// </summary>
        private object GetSerializedPropertyValue(SerializedProperty property)
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference)
            {
                return GetObjectReferenceValue(property);
            }

            if (property.propertyType == SerializedPropertyType.Enum)
            {
                return property.enumNames[property.enumValueIndex];
            }

            if (property.propertyType == SerializedPropertyType.Integer ||
                property.propertyType == SerializedPropertyType.LayerMask)
            {
                return property.intValue;
            }

            if (IsPrimitiveSerializedPropertyType(property.propertyType))
            {
                return GetPrimitiveSerializedPropertyValue(property);
            }

            return IsUnityStructSerializedPropertyType(property.propertyType)
                ? GetUnityStructSerializedPropertyValue(property)
                : null;
        }

        private static bool IsPrimitiveSerializedPropertyType(SerializedPropertyType propertyType)
        {
            return propertyType == SerializedPropertyType.Boolean ||
                propertyType == SerializedPropertyType.Float ||
                propertyType == SerializedPropertyType.String;
        }

        private static bool IsUnityStructSerializedPropertyType(SerializedPropertyType propertyType)
        {
            return propertyType == SerializedPropertyType.Color ||
                propertyType == SerializedPropertyType.Vector2 ||
                propertyType == SerializedPropertyType.Vector3 ||
                propertyType == SerializedPropertyType.Vector4 ||
                propertyType == SerializedPropertyType.Rect ||
                propertyType == SerializedPropertyType.Bounds ||
                propertyType == SerializedPropertyType.Quaternion;
        }

        private static object GetPrimitiveSerializedPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    return property.floatValue;
                default:
                    UnityEngine.Debug.Assert(
                        property.propertyType == SerializedPropertyType.String,
                        "Primitive property serialization only supports bool, float, and string.");
                    return property.stringValue;
            }
        }

        private static object GetUnityStructSerializedPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Color:
                    return property.colorValue;
                case SerializedPropertyType.Vector2:
                    return property.vector2Value;
                case SerializedPropertyType.Vector3:
                    return property.vector3Value;
                case SerializedPropertyType.Vector4:
                    return property.vector4Value;
                case SerializedPropertyType.Rect:
                    return property.rectValue;
                case SerializedPropertyType.Bounds:
                    return property.boundsValue;
                default:
                    UnityEngine.Debug.Assert(
                        property.propertyType == SerializedPropertyType.Quaternion,
                        "Unity struct property serialization only supports Unity value structs.");
                    return property.quaternionValue;
            }
        }

        private object GetObjectReferenceValue(SerializedProperty property)
        {
            UnityEngine.Debug.Assert(property != null, "SerializedProperty must exist before reading object references.");
            UnityEngine.Debug.Assert(property.propertyType == SerializedPropertyType.ObjectReference, "Object reference serialization requires an ObjectReference property.");

            UnityEngine.Object obj = property.objectReferenceValue;
            if (obj == null)
            {
                if (HasStoredObjectReferenceId(property))
                {
                    return new { name = "Missing", type = "Missing", entityId = GetStoredObjectReferenceId(property) };
                }

                return new { name = "None", type = "None", entityId = "0" };
            }

            return new { name = obj.name, type = obj.GetType().Name, entityId = GetObjectId(obj) };
        }

        private static bool HasStoredObjectReferenceId(SerializedProperty property)
        {
#if UNITY_6000_4_OR_NEWER
            return property.objectReferenceEntityIdValue != UnityEngine.EntityId.None;
#else
            return property.objectReferenceInstanceIDValue != 0;
#endif
        }

        private static string GetStoredObjectReferenceId(SerializedProperty property)
        {
#if UNITY_6000_4_OR_NEWER
            return property.objectReferenceEntityIdValue.ToString();
#else
            int instanceId = property.objectReferenceInstanceIDValue;
            return instanceId.ToString(CultureInfo.InvariantCulture);
#endif
        }

        private static string GetObjectId(UnityEngine.Object obj)
        {
            UnityEngine.Debug.Assert(obj != null, "Unity Object must exist before reading its identifier.");

#if UNITY_6000_4_OR_NEWER
            return obj.GetEntityId().ToString();
#else
            int instanceId = obj.GetInstanceID();
            return instanceId.ToString(CultureInfo.InvariantCulture);
#endif
        }
        
        
        private object SerializeValue(object value)
        {
            if (value == null)
                return null;
                
            Type valueType = value.GetType();
            
            // Unity types need special serialization
            if (valueType == typeof(Vector2))
            {
                Vector2 v = (Vector2)value;
                return new { x = v.x, y = v.y };
            }
            else if (valueType == typeof(Vector3))
            {
                Vector3 v = (Vector3)value;
                return new { x = v.x, y = v.y, z = v.z };
            }
            else if (valueType == typeof(Vector4))
            {
                Vector4 v = (Vector4)value;
                return new { x = v.x, y = v.y, z = v.z, w = v.w };
            }
            else if (valueType == typeof(Quaternion))
            {
                Quaternion q = (Quaternion)value;
                return new 
                { 
                    x = q.x, 
                    y = q.y, 
                    z = q.z, 
                    w = q.w,
                    eulerAngles = new { x = q.eulerAngles.x, y = q.eulerAngles.y, z = q.eulerAngles.z }
                };
            }
            else if (valueType == typeof(Color))
            {
                Color c = (Color)value;
                return new { r = c.r, g = c.g, b = c.b, a = c.a };
            }
            else if (valueType == typeof(Rect))
            {
                Rect r = (Rect)value;
                return new { x = r.x, y = r.y, width = r.width, height = r.height };
            }
            else if (valueType == typeof(Bounds))
            {
                Bounds b = (Bounds)value;
                return new 
                { 
                    center = new { x = b.center.x, y = b.center.y, z = b.center.z },
                    size = new { x = b.size.x, y = b.size.y, z = b.size.z }
                };
            }
            else if (valueType.IsEnum)
            {
                return value.ToString();
            }
            
            return value;
        }
    }
}
