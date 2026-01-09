using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Jellyfin2Samsung.Helpers.Converters
{
    public class SingleOrArrayConverter<T> : JsonConverter<List<T>>
    {
        public override List<T> ReadJson(
            JsonReader reader,
            Type objectType,
            List<T>? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            var token = JToken.Load(reader);

            return token switch
            {
                JArray arr => arr.ToObject<List<T>>(serializer)!,
                JObject obj => new List<T> { obj.ToObject<T>(serializer)! },
                _ => new List<T>()
            };
        }

        public override void WriteJson(JsonWriter writer, List<T>? value, JsonSerializer serializer)
            => serializer.Serialize(writer, value);
    }

}
