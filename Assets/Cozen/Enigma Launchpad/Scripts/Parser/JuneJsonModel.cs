using System;
using System.Collections.Generic;

/// <summary>
/// Pure data representations of the June JSON model used by the editor.
/// Designed to mirror the June migration schema without Unity dependencies.
/// </summary>
[Serializable]
public class JuneModel
{
    public List<JuneModule> modules = new List<JuneModule>();
}

[Serializable]
public class JuneModule
{
    public string name;
    public string keyword;
    public string keywordDefine;
    public List<JuneSection> sections = new List<JuneSection>();
    public List<JuneProperty> properties = new List<JuneProperty>();
}

[Serializable]
public class JuneSection
{
    public string name;
    public string foldoutName;
    public string parentSection;
    public bool isRootSection;
    public int indentLevel;
    public int displayOrder = -1;
    public List<int> propertyIndices = new List<int>();
}

[Serializable]
public class JuneProperty
{
    public string name;
    public string displayName;
    public string shaderPropertyName;
    public string rawShaderPropertyName;
    public string propertyType;
    public float? min;
    public float? max;
    public float? defaultValue;
    public int? defaultIntValue;
    public float[] defaultColor;
    public float[] defaultVector;
    public string enumTypeName;
    public List<string> enumValues = new List<string>();
    public bool isToggle;
    public bool indented;
    public List<string> hints = new List<string>();
    public List<ConditionalRule> conditions = new List<ConditionalRule>();
}

[Serializable]
public class ConditionalRule
{
    public List<string> paths = new List<string>();
    public List<object> values = new List<object>();
}
