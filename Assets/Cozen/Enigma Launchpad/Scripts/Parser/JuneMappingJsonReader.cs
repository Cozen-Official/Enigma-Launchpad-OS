using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class JuneMappingJsonReader
{
    private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
    {
        Culture = CultureInfo.InvariantCulture,
        FloatParseHandling = FloatParseHandling.Double
    };

    private class CacheEntry
    {
        public string cacheKey;
        public JuneModel model;
    }

    private static CacheEntry cachedMapping;

    public static JuneModel Load(TextAsset jsonAsset)
    {
        return Load(jsonAsset, out _);
    }

    public static JuneModel Load(TextAsset jsonAsset, out string cacheKey)
    {
        cacheKey = null;

        if (jsonAsset == null || string.IsNullOrWhiteSpace(jsonAsset.text))
        {
            return null;
        }

        string guid = null;
        long timestampTicks = 0L;
#if UNITY_EDITOR
        string assetPath = AssetDatabase.GetAssetPath(jsonAsset);
        if (!string.IsNullOrEmpty(assetPath))
        {
            guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (File.Exists(assetPath))
            {
                timestampTicks = File.GetLastWriteTimeUtc(assetPath).Ticks;
            }
        }
#endif

        string contentHash = Hash128.Compute(jsonAsset.text).ToString();
        cacheKey = $"{guid ?? string.Empty}|{timestampTicks}|{contentHash}";

        if (cachedMapping != null && string.Equals(cachedMapping.cacheKey, cacheKey, StringComparison.Ordinal))
        {
            return cachedMapping.model;
        }

        cachedMapping = new CacheEntry
        {
            cacheKey = cacheKey,
            model = JsonConvert.DeserializeObject<JuneModel>(jsonAsset.text, SerializerSettings)
        };

        return cachedMapping.model;
    }

    public static void PopulateDefaultDictionaries(
        JuneModule module,
        IDictionary<string, float> floatDefaults,
        IDictionary<string, Color> colorDefaults)
    {
        if (module == null || floatDefaults == null || colorDefaults == null)
        {
            return;
        }

        foreach (JuneProperty property in module.properties)
        {
            switch (property.propertyType)
            {
                case "Color":
                {
                    Color? color = TryParseColor(property.defaultColor);
                    if (color.HasValue)
                    {
                        colorDefaults[property.name] = color.Value;
                    }
                    break;
                }
                case "Float":
                case "Range":
                case "Enum":
                case "Toggle":
                {
                    if (property.defaultValue.HasValue)
                    {
                        floatDefaults[property.name] = property.defaultValue.Value;
                    }
                    break;
                }
            }
        }
    }

    public static void ApplyDefaultsToMaterial(JuneModule module, Material material)
    {
        if (module == null || material == null)
        {
            return;
        }

        foreach (JuneProperty property in module.properties)
        {
            string shaderProperty = property.shaderPropertyName;
            if (string.IsNullOrEmpty(shaderProperty) || !material.HasProperty(shaderProperty))
            {
                continue;
            }

            switch (property.propertyType)
            {
                case "Color":
                {
                    Color? color = TryParseColor(property.defaultColor);
                    if (color.HasValue)
                    {
                        material.SetColor(shaderProperty, color.Value);
                    }
                    break;
                }
                case "Float":
                case "Range":
                case "Enum":
                case "Toggle":
                {
                    if (property.defaultValue.HasValue)
                    {
                        material.SetFloat(shaderProperty, property.defaultValue.Value);
                    }
                    break;
                }
            }
        }
    }

    private static Color? TryParseColor(float[] values)
    {
        if (values == null || values.Length < 4)
        {
            return null;
        }

        return new Color(values[0], values[1], values[2], values[3]);
    }
}
