using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Utility class to generate ToolParameterSchema from DTO classes
    /// Eliminates duplication between DTO default values and schema definitions
    /// </summary>
    public static class UnityCliLoopToolParameterSchemaGenerator
    {
        /// <summary>
        /// Generate ToolParameterSchema from DTO type using reflection
        /// </summary>
        /// <typeparam name="TDto">DTO type</typeparam>
        /// <returns>Generated parameter schema</returns>
        public static ToolParameterSchema FromDto<TDto>() where TDto : class, new()
        {
            Type dtoType = typeof(TDto);
            Dictionary<string, ParameterInfo> properties = new();
            List<string> required = new();

            // Create instance to get default values
            TDto defaultInstance = new();

            foreach (PropertyInfo property in dtoType.GetProperties())
            {
                // Skip properties without public setters
                if (!property.CanWrite || property.SetMethod?.IsPublic != true)
                    continue;

                BrowsableAttribute browsableAttribute = property.GetCustomAttribute<BrowsableAttribute>();
                if (browsableAttribute != null && !browsableAttribute.Browsable)
                    continue;

                // Get JSON property name (fallback to property name)
                string parameterName = GetJsonPropertyName(property);
                
                // Get property type information
                string parameterType = GetParameterType(property.PropertyType);
                
                // Get description from attributes
                string description = GetDescription(property);
                
                // Get default value from instance
                object defaultValue = property.GetValue(defaultInstance);
                
                // Get enum values if applicable
                string[] enumValues = GetEnumValues(property.PropertyType);
                
                // Check if property is required
                bool isRequired = IsRequired(property);
                if (isRequired)
                {
                    required.Add(parameterName);
                }

                // Create parameter info
                ParameterInfo paramInfo = new(
                    parameterType,
                    description,
                    defaultValue,
                    enumValues
                );

                properties[parameterName] = paramInfo;
            }

            return new ToolParameterSchema(properties, required.ToArray());
        }

        /// <summary>
        /// Get JSON property name from JsonProperty attribute or property name
        /// </summary>
        private static string GetJsonPropertyName(PropertyInfo property)
        {
            JsonPropertyAttribute jsonAttr = property.GetCustomAttribute<JsonPropertyAttribute>();
            return jsonAttr?.PropertyName ?? property.Name;
        }

        /// <summary>
        /// Convert .NET type to parameter type string
        /// </summary>
        private static string GetParameterType(Type type)
        {
            // Handle nullable types
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            if (underlyingType == typeof(string))
                return "string";
            if (underlyingType == typeof(bool))
                return "boolean";
            if (underlyingType == typeof(int) || underlyingType == typeof(long) || 
                underlyingType == typeof(float) || underlyingType == typeof(double) ||
                underlyingType == typeof(decimal))
                return "number";
            if (underlyingType.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
                return "array";
            if (underlyingType.IsEnum)
                return "string"; // Enums are treated as strings with enum constraints
            if (IsDictionaryType(underlyingType))
                return "object";

            return "string"; // Default fallback
        }

        /// <summary>
        /// Check if type is a dictionary type (Dictionary, IDictionary, IReadOnlyDictionary)
        /// </summary>
        private static bool IsDictionaryType(Type type)
        {
            if (type.IsGenericType)
            {
                Type genericDef = type.GetGenericTypeDefinition();
                if (genericDef == typeof(Dictionary<,>) ||
                    genericDef == typeof(IDictionary<,>) ||
                    genericDef == typeof(IReadOnlyDictionary<,>))
                    return true;
            }
            return type.GetInterfaces().Any(i =>
                i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                 i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        }

        /// <summary>
        /// Get description from Description attribute or generate default
        /// </summary>
        private static string GetDescription(PropertyInfo property)
        {
            DescriptionAttribute descAttr = property.GetCustomAttribute<DescriptionAttribute>();
            if (descAttr != null)
                return descAttr.Description;

            // Generate default description
            return $"Parameter: {property.Name}";
        }

        /// <summary>
        /// Get enum values if property type is enum
        /// </summary>
        private static string[] GetEnumValues(Type type)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            
            if (underlyingType.IsEnum)
            {
                return Enum.GetNames(underlyingType);
            }

            return null;
        }

        /// <summary>
        /// Check if property is required using custom Required attribute
        /// For now, returns false as Unity doesn't support DataAnnotations
        /// Can be extended with custom attributes if needed
        /// </summary>
        private static bool IsRequired(PropertyInfo property)
        {
            // TODO: Implement custom Required attribute if needed
            return false;
        }
    }
} 
