using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgGateway.ADAPT.StandardPlugin
{
    public class TypeMappings
    {
        public TypeMappings()
        {
            List<TypeMapping> Values = new List<TypeMapping>();
            string mappingData = File.ReadAllText("ADAPTStandard/data_type_mappings.json");
            var mappings = JObject.Parse(mappingData);
            foreach(var mapping in mappings)
            {
                Values.Add(new TypeMapping { FrameworkCode = mapping.Key, StandardCode = mapping.Value.ToString() });
            }
        }
    }

    public class TypeMapping
    {
        public string FrameworkCode { get; set; }
        public string StandardCode { get; set; }
    }
    
}