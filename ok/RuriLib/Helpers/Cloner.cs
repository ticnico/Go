using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace RuriLib.Helpers
{
    /// <summary>
    /// Takes care of deep cloning objects.
    /// </summary>
    public static class Cloner
    {
        private static readonly JsonSerializerSettings settings = new()
        {
            TypeNameHandling = TypeNameHandling.All,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new NoReadOnlyCollectionsContractResolver()
        };

        /// <summary>
        /// Deep clones an object by serializing and deserializing it.
        /// </summary>
        public static T Clone<T>(T obj)
            => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj, settings), settings);

        /// <summary>
        /// Custom contract resolver that ignores properties returning 
        /// read-only dictionary collections (KeyCollection/ValueCollection) 
        /// which cannot be deserialized.
        /// </summary>
        private class NoReadOnlyCollectionsContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);

                // Ignore properties that return read-only dictionary collections
                var typeName = property.PropertyType.FullName;
                if (typeName != null &&
                    (typeName.Contains("Dictionary`2+KeyCollection") ||
                     typeName.Contains("Dictionary`2+ValueCollection")))
                {
                    property.Ignored = true;
                }

                return property;
            }
        }
    }
}
