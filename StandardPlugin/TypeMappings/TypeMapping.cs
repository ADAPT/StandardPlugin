using Newtonsoft.Json;

public class TypeMapping
{
    [JsonProperty("source")]
    public string Source { get; set; }

    [JsonProperty("target")]
    public string Target { get; set; }

   [JsonProperty("factor")]    
    public bool ShouldFactor { get; set; }

    [JsonProperty("multiproduct")]    
    public bool IsMultiProductCapable { get; set; }

}