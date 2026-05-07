using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Serializes Unity component information to ComponentInfo format
    /// Related classes:
    /// - GameObjectFinderService: Uses this to serialize components
    /// - ComponentPropertySerializer: Serializes individual properties (TODO)
    /// </summary>
    public class ComponentSerializer
    {
        private readonly ComponentPropertySerializer _propertySerializer;
        
        public ComponentSerializer()
        {
            _propertySerializer = new ComponentPropertySerializer();
        }
        
        public ComponentInfo[] SerializeComponents(GameObject gameObject)
        {
            if (gameObject == null)
                return new ComponentInfo[0];
                
            Component[] components = gameObject.GetComponents<Component>();
            List<ComponentInfo> componentInfos = new();
            
            foreach (Component component in components)
            {
                if (component == null)
                    continue;
                
                string componentTypeName = component.GetType().Name;
                ComponentPropertyInfo[] properties = _propertySerializer.SerializeProperties(component);
                
                ComponentInfo info = new()                {
                    type = componentTypeName,
                    fullTypeName = component.GetType().FullName,
                    properties = properties
                };
                
                componentInfos.Add(info);
            }
            
            return componentInfos.ToArray();
        }
    }
}