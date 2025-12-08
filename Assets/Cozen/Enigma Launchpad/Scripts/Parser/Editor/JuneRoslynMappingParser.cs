#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Enigma.Editor
{
    internal static class JuneRoslynMappingParser
    {
        private const string UnknownType = "Unknown";
        private static IReadOnlyDictionary<string, ShaderPropertyInfo> s_shaderPropertyMap =
            new Dictionary<string, ShaderPropertyInfo>(StringComparer.Ordinal);

        public static JuneModel Parse(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Unable to locate June editor source at '{sourcePath}'.");
            }

            s_shaderPropertyMap = CollectShaderPropertyInfo(sourcePath);
            string source = File.ReadAllText(sourcePath);
            var parseOptions = new CSharpParseOptions(preprocessorSymbols: new[] { "UNITY_EDITOR" });
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            var stringConstants = CollectStringConstants(root);
            var numericConstants = CollectNumericConstants(root);
            var enumDefinitions = CollectEnumDefinitions(root, stringConstants);
            MethodDeclarationSyntax onGuiMethod = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => string.Equals(m.Identifier.Text, "OnGUI", StringComparison.Ordinal));

            if (onGuiMethod?.Body == null)
            {
                throw new InvalidOperationException("Could not locate OnGUI method in JuneUI5.cs");
            }

            var modules = ExtractModules(onGuiMethod.Body, stringConstants, numericConstants, enumDefinitions);

            return new JuneModel { modules = modules };
        }

        private static IReadOnlyDictionary<string, ShaderPropertyInfo> CollectShaderPropertyInfo(string sourcePath)
        {
            string editorDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string shaderRoot = Path.GetFullPath(Path.Combine(editorDirectory, "..", ".."));

            var properties = new Dictionary<string, ShaderPropertyInfo>(StringComparer.Ordinal);

            if (!Directory.Exists(shaderRoot))
            {
                return properties;
            }

            var propertyPattern = new Regex(
                @"^\s*(?:\[[^\]]+\]\s*)*(?<name>_[A-Za-z0-9]+)\s*\(""(?<display>[^""]+)""\s*,\s*(?<type>Range\([^\)]*\)|Color|Vector|Float|Int|2D|3D|Cube|Texture)(?:\s*/\*.*?\*/)?\)\s*=\s*(?<default>[^/]+)?",
                RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (string shaderPath in Directory.EnumerateFiles(shaderRoot, "*.shader", SearchOption.AllDirectories))
            {
                foreach (string line in File.ReadLines(shaderPath))
                {
                    Match match = propertyPattern.Match(StripComments(line));
                    if (!match.Success)
                    {
                        continue;
                    }

                    string shaderProperty = match.Groups["name"].Value;
                    string type = match.Groups["type"].Value;
                    string displayName = match.Groups["display"].Value;
                    string defaultValue = match.Groups["default"].Value;

                    properties[shaderProperty] = BuildShaderPropertyInfo(
                        shaderProperty,
                        NormalizeShaderPropertyType(type),
                        displayName,
                        type,
                        defaultValue);
                }
            }

            return properties;
        }

        private static ShaderPropertyInfo BuildShaderPropertyInfo(
            string shaderProperty,
            string normalizedType,
            string displayName,
            string rawType,
            string defaultValue)
        {
            (float? min, float? max) = ParseRangeBounds(rawType);

            ParseDefaultValues(normalizedType, defaultValue, out float? defaultFloat, out int? defaultInt,
                out float[] defaultColor, out float[] defaultVector);

            return new ShaderPropertyInfo(
                shaderProperty,
                normalizedType,
                displayName,
                min,
                max,
                defaultFloat,
                defaultInt,
                defaultColor,
                defaultVector);
        }

        private static (float? min, float? max) ParseRangeBounds(string rawType)
        {
            var rangeMatch = Regex.Match(rawType, @"Range\((?<min>[-0-9eE\.]+)\s*,\s*(?<max>[-0-9eE\.]+)\)");
            if (rangeMatch.Success)
            {
                return (
                    ParseFloat(rangeMatch.Groups["min"].Value),
                    ParseFloat(rangeMatch.Groups["max"].Value));
            }

            return (null, null);
        }

        private static void ParseDefaultValues(
            string normalizedType,
            string rawDefault,
            out float? defaultFloat,
            out int? defaultInt,
            out float[] defaultColor,
            out float[] defaultVector)
        {
            defaultFloat = null;
            defaultInt = null;
            defaultColor = null;
            defaultVector = null;

            if (string.IsNullOrWhiteSpace(rawDefault))
            {
                return;
            }

            string trimmed = rawDefault.Trim();

            switch (normalizedType)
            {
                case "Color":
                    defaultColor = ParseFloatArray(trimmed);
                    break;
                case "Vector":
                    defaultVector = ParseFloatArray(trimmed);
                    break;
                case "Int":
                    if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                    {
                        defaultInt = parsedInt;
                    }
                    else
                    {
                        defaultFloat = ParseFloat(trimmed);
                    }
                    break;
                default:
                    defaultFloat = ParseFloat(trimmed);
                    break;
            }
        }

        private static float[] ParseFloatArray(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string normalized = raw.Trim();
            if (normalized.StartsWith("(", StringComparison.Ordinal) && normalized.EndsWith(")", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1, normalized.Length - 2);
            }

            var parts = normalized.Split(',');
            var values = new List<float>(parts.Length);

            foreach (string part in parts)
            {
                float? parsed = ParseFloat(part);
                if (parsed.HasValue)
                {
                    values.Add(parsed.Value);
                }
            }

            return values.Count > 0 ? values.ToArray() : null;
        }

        private static float? ParseFloat(string raw)
        {
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : null;
        }

        private static string StripComments(string line)
        {
            int commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            return commentIndex >= 0 ? line[..commentIndex] : line;
        }

        private static Dictionary<string, string> CollectStringConstants(CompilationUnitSyntax root)
        {
            var constants = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (FieldDeclarationSyntax field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                if (!field.Declaration.Type.ToString().Contains("string", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        constants[variable.Identifier.Text] = literal.Token.ValueText;
                    }
                }
            }

            return constants;
        }

        private static Dictionary<string, float> CollectNumericConstants(CompilationUnitSyntax root)
        {
            var constants = new Dictionary<string, float>(StringComparer.Ordinal);

            foreach (FieldDeclarationSyntax field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                if (!IsNumericType(field.Declaration.Type))
                {
                    continue;
                }

                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                {
                    float? value = EvaluateNumericExpression(variable.Initializer?.Value, constants);
                    if (value.HasValue)
                    {
                        constants[variable.Identifier.Text] = value.Value;
                    }
                }
            }

            return constants;
        }

        private static Dictionary<string, EnumDefinition> CollectEnumDefinitions(
            CompilationUnitSyntax root,
            IReadOnlyDictionary<string, string> strings)
        {
            var enums = new Dictionary<string, EnumDefinition>(StringComparer.Ordinal);

            foreach (EnumDeclarationSyntax enumDeclaration in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
            {
                string enumName = enumDeclaration.Identifier.Text;
                var values = new List<string>();

                foreach (EnumMemberDeclarationSyntax member in enumDeclaration.Members)
                {
                    string displayName = TryResolveEnumDisplayName(member, strings);
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        values.Add(displayName);
                    }
                }

                enums[enumName] = new EnumDefinition(enumName, values);
            }

            return enums;
        }

        private static bool IsNumericType(TypeSyntax type)
        {
            string normalized = type.ToString();
            return normalized.Contains("float", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("double", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("int", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("long", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("decimal", StringComparison.OrdinalIgnoreCase);
        }

        private static List<JuneModule> ExtractModules(
            BlockSyntax methodBody,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions)
        {
            var moduleContexts = new List<ModuleContext>();
            var statements = methodBody.Statements;

            for (int i = 0; i < statements.Count; i++)
            {
                if (statements[i] is ExpressionStatementSyntax expressionStatement &&
                    expressionStatement.Expression is InvocationExpressionSyntax invocation &&
                    IsMakeEffectInvocation(invocation))
                {
                    ModuleContext context = BuildModuleContext(invocation, strings);
                    context.MakeEffectIndex = i;
                    moduleContexts.Add(context);
                }
            }

            foreach (ModuleContext context in moduleContexts)
            {
                if (context.MakeEffectIndex < 0 || context.MakeEffectIndex >= statements.Count - 1)
                {
                    continue;
                }

                for (int j = context.MakeEffectIndex + 1; j < statements.Count; j++)
                {
                    if (statements[j] is IfStatementSyntax ifStatement &&
                        ifStatement.Condition is IdentifierNameSyntax identifier &&
                        string.Equals(identifier.Identifier.Text, context.ToggleIdentifier, StringComparison.Ordinal))
                    {
                        ParseModuleBody(ifStatement.Statement as BlockSyntax, context, strings, numbers, enumDefinitions);
                        break;
                    }
                }
            }

            return moduleContexts.Select(c => c.ToJuneModule()).ToList();
        }

        private static bool IsMakeEffectInvocation(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                return string.Equals(memberAccess.Name.Identifier.Text, "makeEffect", StringComparison.Ordinal);
            }

            return false;
        }

        private static ModuleContext BuildModuleContext(InvocationExpressionSyntax invocation, IReadOnlyDictionary<string, string> strings)
        {
            var args = invocation.ArgumentList.Arguments;

            string toggleIdentifier = args.Count > 2 ? args[2].Expression.ToString().Replace("ref ", string.Empty).Trim() : string.Empty;
            string moduleName = args.Count > 5 ? TryResolveString(args[5].Expression, strings) : "Unknown";
            string keywordProperty = args.Count > 10 ? TryResolveString(args[10].Expression, strings) : null;
            string keywordDefine = args.Count > 12 ? TryResolveString(args[12].Expression, strings) : null;

            return new ModuleContext(moduleName, keywordProperty, keywordDefine, toggleIdentifier);
        }

        private static void ParseModuleBody(
            BlockSyntax block,
            ModuleContext module,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions)
        {
            if (block == null)
            {
                return;
            }

            var propertyMap = new Dictionary<string, PropertyContext>(StringComparer.Ordinal);
            var sectionConditions = new List<ConditionalRule>();
            SectionLayout rootLayout = module.GetOrCreateLayout(module.Name, module.Name, null, null);
            ParseBlock(block, module, rootLayout, propertyMap, strings, numbers, enumDefinitions, sectionConditions, 0);
        }

        private static void ParseBlock(
            BlockSyntax block,
            ModuleContext module,
            SectionLayout sectionLayout,
            IDictionary<string, PropertyContext> propertyMap,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions,
            List<ConditionalRule> activeConditions,
            int indentLevel)
        {
            if (block == null)
            {
                return;
            }

            int currentIndent = indentLevel;
            foreach (StatementSyntax statement in block.Statements)
            {
                currentIndent = ParseStatement(statement, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, activeConditions, currentIndent);
            }
        }

        private static int ParseStatement(
            StatementSyntax statement,
            ModuleContext module,
            SectionLayout sectionLayout,
            IDictionary<string, PropertyContext> propertyMap,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions,
            List<ConditionalRule> activeConditions,
            int indentLevel)
        {
            switch (statement)
            {
                case LocalDeclarationStatementSyntax localDecl:
                    return HandleLocalDeclaration(localDecl, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, activeConditions, indentLevel);
                case ExpressionStatementSyntax expressionStatement:
                    return HandleExpressionStatement(expressionStatement, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, activeConditions, indentLevel);
                case IfStatementSyntax ifStatement:
                    HandleConditionalBlock(ifStatement, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, activeConditions, indentLevel);
                    break;
                case SwitchStatementSyntax switchStatement:
                    HandleSwitchStatement(switchStatement, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, activeConditions, indentLevel);
                    break;
            }

            return indentLevel;
        }

        private static void HandleConditionalBlock(
            IfStatementSyntax ifStatement,
            ModuleContext module,
            SectionLayout sectionLayout,
            IDictionary<string, PropertyContext> propertyMap,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions,
            List<ConditionalRule> activeConditions,
            int indentLevel)
        {
            var newConditions = new List<ConditionalRule>(activeConditions);

            if (TryExtractSubsectionToggle(module, ifStatement.Condition, out SectionLayout subsectionLayout))
            {
                if (ifStatement.Statement is BlockSyntax subsectionBlock)
                {
                    ParseBlock(subsectionBlock, module, subsectionLayout, propertyMap, strings, numbers, enumDefinitions, activeConditions, indentLevel);
                }

                if (ifStatement.Else?.Statement is BlockSyntax subsectionElseBlock)
                {
                    ParseBlock(subsectionElseBlock, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, activeConditions, indentLevel);
                }

                return;
            }

            List<ConditionalRule> parsedConditions = TryParsePropertyCondition(ifStatement.Condition, strings, propertyMap);
            if (parsedConditions.Count > 0)
            {
                newConditions.AddRange(parsedConditions);
            }

            if (ifStatement.Statement is BlockSyntax innerBlock)
            {
                ParseBlock(innerBlock, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, newConditions, indentLevel);
            }
            else
            {
                ParseStatement(ifStatement.Statement, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, newConditions, indentLevel);
            }

            if (ifStatement.Else?.Statement is BlockSyntax elseBlock)
            {
                List<ConditionalRule> elseConditions = parsedConditions.Count > 0
                    ? CombineConditions(activeConditions, NegateConditions(parsedConditions))
                    : activeConditions;

                ParseBlock(elseBlock, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, elseConditions, indentLevel);
            }
            else if (ifStatement.Else?.Statement != null)
            {
                List<ConditionalRule> elseConditions = parsedConditions.Count > 0
                    ? CombineConditions(activeConditions, NegateConditions(parsedConditions))
                    : activeConditions;

                ParseStatement(ifStatement.Else.Statement, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, elseConditions, indentLevel);
            }
        }

        private static void HandleSwitchStatement(
            SwitchStatementSyntax switchStatement,
            ModuleContext module,
            SectionLayout sectionLayout,
            IDictionary<string, PropertyContext> propertyMap,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions,
            List<ConditionalRule> activeConditions,
            int indentLevel)
        {
            string switchPath = TryExtractPropertyPath(switchStatement.Expression);

            foreach (SwitchSectionSyntax section in switchStatement.Sections)
            {
                var sectionConditions = new List<List<ConditionalRule>>();

                foreach (SwitchLabelSyntax label in section.Labels)
                {
                    if (label is CaseSwitchLabelSyntax caseLabel && !string.IsNullOrEmpty(switchPath))
                    {
                        object value = TryExtractComparableValue(caseLabel.Value, strings);
                        if (value != null)
                        {
                            var rule = new ConditionalRule();
                            rule.paths.Add(switchPath);
                            rule.values.Add(value);

                            sectionConditions.Add(CombineConditions(activeConditions, new List<ConditionalRule> { rule }));
                        }
                    }
                    else if (label is DefaultSwitchLabelSyntax)
                    {
                        sectionConditions.Add(new List<ConditionalRule>(activeConditions));
                    }
                }

                if (sectionConditions.Count == 0)
                {
                    sectionConditions.Add(new List<ConditionalRule>(activeConditions));
                }

                foreach (List<ConditionalRule> conditions in sectionConditions)
                {
                    foreach (StatementSyntax statement in section.Statements)
                    {
                        ParseStatement(statement, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, conditions, indentLevel);
                    }
                }
            }
        }

        private static List<ConditionalRule> CombineConditions(
            List<ConditionalRule> activeConditions,
            List<ConditionalRule> additionalConditions)
        {
            if (additionalConditions.Count == 0)
            {
                return new List<ConditionalRule>(activeConditions);
            }

            var combined = new List<ConditionalRule>(activeConditions.Count + additionalConditions.Count);
            combined.AddRange(activeConditions);
            combined.AddRange(additionalConditions);
            return combined;
        }

        private static List<ConditionalRule> NegateConditions(List<ConditionalRule> conditions)
        {
            if (conditions.Count == 0)
            {
                return new List<ConditionalRule>();
            }

            var negated = new List<ConditionalRule>(conditions.Count);
            foreach (ConditionalRule rule in conditions)
            {
                if (rule.paths.Count != rule.values.Count || rule.paths.Count == 0)
                {
                    continue;
                }

                var negatedRule = new ConditionalRule();
                bool invertible = true;

                for (int i = 0; i < rule.values.Count; i++)
                {
                    if (!TryInvertComparable(rule.values[i], out object inverted))
                    {
                        invertible = false;
                        break;
                    }

                    negatedRule.paths.Add(rule.paths[i]);
                    negatedRule.values.Add(inverted);
                }

                if (invertible)
                {
                    negated.Add(negatedRule);
                }
            }

            return negated;
        }

        private static List<ConditionalRule> TryParsePropertyCondition(
            ExpressionSyntax condition,
            IReadOnlyDictionary<string, string> strings,
            IDictionary<string, PropertyContext> propertyMap)
        {
            var result = ParseConditionalExpression(condition, strings, propertyMap);
            return result ?? new List<ConditionalRule>();
        }

        private static List<ConditionalRule> ParseConditionalExpression(
            ExpressionSyntax condition,
            IReadOnlyDictionary<string, string> strings,
            IDictionary<string, PropertyContext> propertyMap)
        {
            switch (condition)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    return ParseConditionalExpression(parenthesized.Expression, strings, propertyMap);

                case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.LogicalNotExpression):
                    return ParseNegatedCondition(prefix.Operand, strings, propertyMap);

                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression):
                    return CombineAnd(
                        ParseConditionalExpression(binary.Left, strings, propertyMap),
                        ParseConditionalExpression(binary.Right, strings, propertyMap));

                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression):
                    return CombineOr(
                        ParseConditionalExpression(binary.Left, strings, propertyMap),
                        ParseConditionalExpression(binary.Right, strings, propertyMap));

                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
                    return ParseComparison(binary.Left, binary.Right, binary.IsKind(SyntaxKind.EqualsExpression), strings, propertyMap);

                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.GreaterThanExpression) ||
                                                       binary.IsKind(SyntaxKind.GreaterThanOrEqualExpression) ||
                                                       binary.IsKind(SyntaxKind.LessThanExpression) ||
                                                       binary.IsKind(SyntaxKind.LessThanOrEqualExpression):
                    return ParseRangeComparison(binary.Left, binary.Right, binary.Kind(), strings, propertyMap);

                case IdentifierNameSyntax nameSyntax:
                    return propertyMap.ContainsKey(nameSyntax.Identifier.Text)
                        ? new List<ConditionalRule>
                        {
                            new ConditionalRule
                            {
                                paths = { nameSyntax.Identifier.Text },
                                values = { true }
                            }
                        }
                        : new List<ConditionalRule>();

                default:
                    return new List<ConditionalRule>();
            }
        }

        private static List<ConditionalRule> ParseNegatedCondition(
            ExpressionSyntax expression,
            IReadOnlyDictionary<string, string> strings,
            IDictionary<string, PropertyContext> propertyMap)
        {
            if (expression is IdentifierNameSyntax nameSyntax)
            {
                return propertyMap.ContainsKey(nameSyntax.Identifier.Text)
                    ? new List<ConditionalRule>
                    {
                        new ConditionalRule
                        {
                            paths = { nameSyntax.Identifier.Text },
                            values = { false }
                        }
                    }
                    : new List<ConditionalRule>();
            }

            List<ConditionalRule> positive = ParseConditionalExpression(expression, strings, propertyMap);
            return NegateConditions(positive);
        }

        private static List<ConditionalRule> ParseComparison(
            ExpressionSyntax left,
            ExpressionSyntax right,
            bool equals,
            IReadOnlyDictionary<string, string> strings,
            IDictionary<string, PropertyContext> propertyMap)
        {
            string leftPath = FilterKnownPropertyPath(TryExtractPropertyPath(left), propertyMap);
            string rightPath = FilterKnownPropertyPath(TryExtractPropertyPath(right), propertyMap);

            object leftValue = TryExtractComparableValue(left, strings);
            object rightValue = TryExtractComparableValue(right, strings);

            if (string.IsNullOrEmpty(leftPath) && string.IsNullOrEmpty(rightPath))
            {
                return new List<ConditionalRule>();
            }

            var rule = new ConditionalRule();

            if (!string.IsNullOrEmpty(leftPath))
            {
                object comparable = ToggleForInequality(rightValue, equals);
                if (comparable != null)
                {
                    rule.paths.Add(leftPath);
                    rule.values.Add(comparable);
                }
            }

            if (!string.IsNullOrEmpty(rightPath))
            {
                object comparable = ToggleForInequality(leftValue, equals);
                if (comparable != null)
                {
                    rule.paths.Add(rightPath);
                    rule.values.Add(comparable);
                }
            }

            return rule.paths.Count > 0 ? new List<ConditionalRule> { rule } : new List<ConditionalRule>();
        }

        private static List<ConditionalRule> ParseRangeComparison(
            ExpressionSyntax left,
            ExpressionSyntax right,
            SyntaxKind comparisonKind,
            IReadOnlyDictionary<string, string> strings,
            IDictionary<string, PropertyContext> propertyMap)
        {
            string leftPath = FilterKnownPropertyPath(TryExtractPropertyPath(left), propertyMap);
            string rightPath = FilterKnownPropertyPath(TryExtractPropertyPath(right), propertyMap);

            bool isLeftProperty = !string.IsNullOrEmpty(leftPath);
            bool isRightProperty = !string.IsNullOrEmpty(rightPath);

            object leftValue = TryExtractComparableValue(left, strings);
            object rightValue = TryExtractComparableValue(right, strings);

            if (isLeftProperty == isRightProperty)
            {
                return new List<ConditionalRule>();
            }

            string propertyPath = isLeftProperty ? leftPath : rightPath;
            object comparisonValue = isLeftProperty ? rightValue : leftValue;

            if (propertyPath == null || comparisonValue == null)
            {
                return new List<ConditionalRule>();
            }

            if (!propertyMap.TryGetValue(propertyPath, out PropertyContext context))
            {
                return new List<ConditionalRule>();
            }

            double? numeric = comparisonValue switch
            {
                int i => i,
                float f => f,
                double d => d,
                _ => null
            };

            if (!numeric.HasValue)
            {
                return new List<ConditionalRule>();
            }

            List<object> allowedValues = BuildInequalityValues(context, numeric.Value, comparisonKind);
            if (allowedValues.Count == 0)
            {
                return new List<ConditionalRule>();
            }

            return new List<ConditionalRule>
            {
                new ConditionalRule
                {
                    paths = { propertyPath },
                    values = allowedValues
                }
            };
        }

        private static List<object> BuildInequalityValues(PropertyContext context, double boundary, SyntaxKind comparisonKind)
        {
            int min = 0;
            int max = context.EnumValues.Count > 0
                ? context.EnumValues.Count - 1
                : (int)Math.Round(context.RangeMax ?? double.NaN);

            if (double.IsNaN(max))
            {
                return new List<object>();
            }

            if (context.RangeMin.HasValue)
            {
                min = (int)Math.Round(context.RangeMin.Value);
            }

            int start;
            int end;

            switch (comparisonKind)
            {
                case SyntaxKind.GreaterThanExpression:
                    start = (int)Math.Floor(boundary) + 1;
                    end = max;
                    break;
                case SyntaxKind.GreaterThanOrEqualExpression:
                    start = (int)Math.Ceiling(boundary);
                    end = max;
                    break;
                case SyntaxKind.LessThanExpression:
                    start = min;
                    end = (int)Math.Ceiling(boundary) - 1;
                    break;
                case SyntaxKind.LessThanOrEqualExpression:
                    start = min;
                    end = (int)Math.Floor(boundary);
                    break;
                default:
                    return new List<object>();
            }

            if (start > end)
            {
                return new List<object>();
            }

            var values = new List<object>();
            for (int i = start; i <= end; i++)
            {
                values.Add(i);
            }

            return values;
        }

        private static string FilterKnownPropertyPath(string path, IDictionary<string, PropertyContext> propertyMap)
        {
            return !string.IsNullOrEmpty(path) && propertyMap.ContainsKey(path) ? path : null;
        }

        private static object ToggleForInequality(object value, bool equals)
        {
            if (equals)
            {
                return value;
            }

            return TryInvertComparable(value, out object inverted) ? inverted : null;
        }

        private static bool TryInvertComparable(object value, out object inverted)
        {
            switch (value)
            {
                case bool boolean:
                    inverted = !boolean;
                    return true;
                case int intValue when intValue == 0 || intValue == 1:
                    inverted = intValue == 0 ? 1 : 0;
                    return true;
                case float floatValue when Math.Abs(floatValue) < 0.0001f || Math.Abs(floatValue - 1f) < 0.0001f:
                    inverted = Math.Abs(floatValue) < 0.0001f ? 1f : 0f;
                    return true;
            }

            inverted = null;
            return false;
        }

        private static string TryExtractPropertyPath(ExpressionSyntax expression)
        {
            if (expression is MemberAccessExpressionSyntax member && member.Expression is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text;
            }

            if (expression is IdentifierNameSyntax identifierName)
            {
                return identifierName.Identifier.Text;
            }

            return null;
        }

        private static bool TryExtractSubsectionToggle(ModuleContext module, ExpressionSyntax condition, out SectionLayout subsection)
        {
            foreach (IdentifierNameSyntax identifier in condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (module.TryGetSubsectionLayout(identifier.Identifier.Text, out subsection))
                {
                    return true;
                }
            }

            subsection = null;
            return false;
        }

        private static object TryExtractComparableValue(ExpressionSyntax expression, IReadOnlyDictionary<string, string> strings)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NumericLiteralExpression):
                    return literal.Token.Value;
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    return literal.Token.ValueText;
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression):
                    return true;
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression):
                    return false;
                case IdentifierNameSyntax identifier when strings.TryGetValue(identifier.Identifier.Text, out string text):
                    return text;
                case MemberAccessExpressionSyntax member when member.Name is IdentifierNameSyntax:
                    if (int.TryParse(member.Name.Identifier.Text, out int numeric))
                    {
                        return numeric;
                    }
                    break;
                case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.UnaryMinusExpression):
                    object inner = TryExtractComparableValue(prefix.Operand, strings);
                    if (inner is int intValue)
                    {
                        return -intValue;
                    }
                    if (inner is float floatValue)
                    {
                        return -floatValue;
                    }
                    break;
                case CastExpressionSyntax cast:
                    return TryExtractComparableValue(cast.Expression, strings);
                case ParenthesizedExpressionSyntax parenthesized:
                    return TryExtractComparableValue(parenthesized.Expression, strings);
            }

            return null;
        }

        private static List<ConditionalRule> CombineAnd(List<ConditionalRule> left, List<ConditionalRule> right)
        {
            if (left.Count == 0)
            {
                return right;
            }

            if (right.Count == 0)
            {
                return left;
            }

            var combined = new List<ConditionalRule>();
            foreach (ConditionalRule leftRule in left)
            {
                foreach (ConditionalRule rightRule in right)
                {
                    var rule = new ConditionalRule();
                    rule.paths.AddRange(leftRule.paths);
                    rule.paths.AddRange(rightRule.paths);
                    rule.values.AddRange(leftRule.values);
                    rule.values.AddRange(rightRule.values);
                    combined.Add(rule);
                }
            }

            return combined;
        }

        private static List<ConditionalRule> CombineOr(List<ConditionalRule> left, List<ConditionalRule> right)
        {
            if (left.Count == 0)
            {
                return right;
            }

            if (right.Count == 0)
            {
                return left;
            }

            var combined = new List<ConditionalRule>();
            combined.AddRange(left);
            combined.AddRange(right);
            return combined;
        }

        private static int HandleExpressionStatement(
            ExpressionStatementSyntax expressionStatement,
            ModuleContext module,
            SectionLayout sectionLayout,
            IDictionary<string, PropertyContext> propertyMap,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions,
            List<ConditionalRule> activeConditions,
            int indentLevel)
        {
            switch (expressionStatement.Expression)
            {
                case AssignmentExpressionSyntax assignment:
                    HandleAssignment(assignment, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, activeConditions, indentLevel);
                    break;
                case InvocationExpressionSyntax invocation:
                    if (TryAdjustIndentation(invocation, ref indentLevel))
                    {
                        return indentLevel;
                    }

                    HandleInvocation(invocation, module, sectionLayout, propertyMap, strings, numbers, enumDefinitions, activeConditions, indentLevel);
                    break;
            }

            return indentLevel;
        }

        private static int HandleLocalDeclaration(
            LocalDeclarationStatementSyntax localDecl,
            ModuleContext module,
            SectionLayout sectionLayout,
            IDictionary<string, PropertyContext> propertyMap,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions,
            List<ConditionalRule> activeConditions,
            int indentLevel)
        {
            if (localDecl.Declaration.Variables.Count != 1)
            {
                return indentLevel;
            }

            VariableDeclaratorSyntax variable = localDecl.Declaration.Variables.First();
            if (variable.Initializer?.Value is InvocationExpressionSyntax invocation)
            {
                if (IsMakeSubEffectInvocation(invocation))
                {
                    string toggleId = variable.Identifier.Text;
                    string newSectionName = TryResolveString(invocation.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression, strings);
                    HandleSubsection(toggleId, newSectionName, module, sectionLayout, numbers, invocation.ArgumentList.Arguments.ElementAtOrDefault(3)?.Expression);
                    return indentLevel;
                }
            }

            if (variable.Initializer?.Value is InvocationExpressionSyntax findPropertyInvocation &&
                IsFindPropertyInvocation(findPropertyInvocation))
            {
                string shaderProperty = TryResolveString(findPropertyInvocation.ArgumentList.Arguments.ElementAtOrDefault(0)?.Expression, strings);
                RegisterProperty(module, sectionLayout, variable.Identifier.Text, shaderProperty, propertyMap, activeConditions, indentLevel);
            }

            return indentLevel;
        }

        private static void HandleInvocation(
            InvocationExpressionSyntax invocation,
            ModuleContext module,
            SectionLayout sectionLayout,
            IDictionary<string, PropertyContext> propertyMap,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions,
            List<ConditionalRule> activeConditions,
            int indentLevel)
        {
            if (IsMakeSubEffectInvocation(invocation))
            {
                string toggleId = invocation.ArgumentList.Arguments.ElementAtOrDefault(2)?.Expression?.ToString();
                string newSectionName = TryResolveString(invocation.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression, strings);
                HandleSubsection(toggleId, newSectionName, module, sectionLayout, numbers, invocation.ArgumentList.Arguments.ElementAtOrDefault(3)?.Expression);
                return;
            }

            string propertyIdentifier = ExtractPropertyIdentifier(invocation.ArgumentList.Arguments);
            if (!string.IsNullOrEmpty(propertyIdentifier) && propertyMap.TryGetValue(propertyIdentifier, out PropertyContext context))
            {
                (string label, List<string> hints) = ExtractLabelAndHints(invocation.ArgumentList.Arguments, strings);
                if (!string.IsNullOrEmpty(label))
                {
                    context.DisplayName ??= label;
                }

                if (hints.Count > 0)
                {
                    foreach (string hint in hints)
                    {
                        if (!context.Hints.Contains(hint))
                        {
                            context.Hints.Add(hint);
                        }
                    }
                }

                string type = InferTypeFromInvocation(invocation, strings);
                if (!string.IsNullOrEmpty(type) && (context.PropertyType == UnknownType || string.IsNullOrEmpty(context.PropertyType)))
                {
                    context.PropertyType = type;
                }

                PopulateTypeSpecificHints(invocation, context, numbers, enumDefinitions);

                module.AttachProperty(sectionLayout, context, activeConditions, indentLevel > 0, true);
            }
        }

        private static void HandleAssignment(
            AssignmentExpressionSyntax assignment,
            ModuleContext module,
            SectionLayout sectionLayout,
            IDictionary<string, PropertyContext> propertyMap,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions,
            List<ConditionalRule> activeConditions,
            int indentLevel)
        {
            InvocationExpressionSyntax invocation = TryExtractInvocation(assignment.Right);
            if (invocation != null)
            {
                if (IsMakeSubEffectInvocation(invocation) && assignment.Left is IdentifierNameSyntax subsectionToggle)
                {
                    string newSectionName = TryResolveString(invocation.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression, strings);
                    HandleSubsection(subsectionToggle.Identifier.Text, newSectionName, module, sectionLayout, numbers, invocation.ArgumentList.Arguments.ElementAtOrDefault(3)?.Expression);
                    return;
                }

                if (assignment.Left is IdentifierNameSyntax identifier && IsFindPropertyInvocation(invocation))
                {
                    string shaderProperty = TryResolveString(invocation.ArgumentList.Arguments.ElementAtOrDefault(0)?.Expression, strings);
                    RegisterProperty(module, sectionLayout, identifier.Identifier.Text, shaderProperty, propertyMap, activeConditions, indentLevel);
                    return;
                }
            }

            if (assignment.Left is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax propertyVar)
            {
                if (!propertyMap.TryGetValue(propertyVar.Identifier.Text, out PropertyContext context))
                {
                    return;
                }

                if (invocation != null)
                {
                    (string label, List<string> hints) = ExtractLabelAndHints(invocation.ArgumentList.Arguments, strings);

                    if (!string.IsNullOrEmpty(label) && string.IsNullOrEmpty(context.DisplayName))
                    {
                        context.DisplayName = label;
                    }

                    if (hints.Count > 0)
                    {
                        foreach (string hint in hints)
                        {
                            if (!context.Hints.Contains(hint))
                            {
                                context.Hints.Add(hint);
                            }
                        }
                    }

                    string inferredType = InferTypeFromInvocation(invocation, strings);
                    if (!string.IsNullOrEmpty(inferredType) &&
                        (string.IsNullOrEmpty(context.PropertyType) || string.Equals(context.PropertyType, UnknownType, StringComparison.Ordinal)))
                    {
                        context.PropertyType = inferredType;
                    }

                    PopulateTypeSpecificHints(invocation, context, numbers, enumDefinitions);
                    module.AttachProperty(sectionLayout, context, activeConditions, indentLevel > 0, true);
                }

                float? numeric = EvaluateNumericExpression(assignment.Right, numbers);

                if (numeric.HasValue)
                {
                    if (memberAccess.Name.Identifier.Text.Contains("floatValue", StringComparison.OrdinalIgnoreCase))
                    {
                        context.DefaultValue = numeric.Value;
                        context.PropertyType ??= "Float";
                    }
                    else if (memberAccess.Name.Identifier.Text.Contains("colorValue", StringComparison.OrdinalIgnoreCase))
                    {
                        context.PropertyType = "Color";
                    }
                    else if (memberAccess.Name.Identifier.Text.Contains("intValue", StringComparison.OrdinalIgnoreCase))
                    {
                        context.DefaultInt = Convert.ToInt32(numeric.Value);
                        context.PropertyType ??= "Int";
                    }
                }

                if (invocation != null)
                {
                    (string label, List<string> hints) = ExtractLabelAndHints(invocation.ArgumentList.Arguments, strings);
                    if (!string.IsNullOrEmpty(label))
                    {
                        context.DisplayName ??= label;
                    }

                    if (hints.Count > 0)
                    {
                        foreach (string hint in hints)
                        {
                            if (!context.Hints.Contains(hint))
                            {
                                context.Hints.Add(hint);
                            }
                        }
                    }

                    string inferredType = InferTypeFromInvocation(invocation, strings);
                    if (!string.IsNullOrEmpty(inferredType) && (context.PropertyType == UnknownType || string.IsNullOrEmpty(context.PropertyType)))
                    {
                        context.PropertyType = inferredType;
                    }

                    PopulateTypeSpecificHints(invocation, context, numbers, enumDefinitions);
                    module.AttachProperty(sectionLayout, context, activeConditions, indentLevel > 0, true);
                }
            }
        }

        private static bool TryAdjustIndentation(InvocationExpressionSyntax invocation, ref int indentLevel)
        {
            string identifier = invocation.Expression switch
            {
                IdentifierNameSyntax nameSyntax => nameSyntax.Identifier.Text,
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                _ => string.Empty
            };

            if (string.Equals(identifier, "doIndentUp", StringComparison.Ordinal))
            {
                indentLevel++;
                return true;
            }

            if (string.Equals(identifier, "doIndentDown", StringComparison.Ordinal))
            {
                indentLevel = Math.Max(0, indentLevel - 1);
                return true;
            }

            return false;
        }

        private static InvocationExpressionSyntax TryExtractInvocation(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case InvocationExpressionSyntax invocation:
                    return invocation;
                case CastExpressionSyntax cast:
                    return TryExtractInvocation(cast.Expression);
                case ParenthesizedExpressionSyntax parenthesized:
                    return TryExtractInvocation(parenthesized.Expression);
                case ConditionalExpressionSyntax conditional:
                    return TryExtractInvocation(conditional.Condition) ??
                           TryExtractInvocation(conditional.WhenTrue) ??
                           TryExtractInvocation(conditional.WhenFalse);
            }

            return expression?.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        }

        private static void HandleSubsection(
            string toggleId,
            string newSectionName,
            ModuleContext module,
            SectionLayout sectionLayout,
            IReadOnlyDictionary<string, float> numbers,
            ExpressionSyntax displayOrderExpression)
        {
            if (string.IsNullOrEmpty(toggleId) || string.IsNullOrEmpty(newSectionName))
            {
                return;
            }

            int? preferredOrder = null;
            float? parsedOrder = EvaluateNumericExpression(displayOrderExpression, numbers);
            if (parsedOrder.HasValue)
            {
                preferredOrder = (int)Math.Round(parsedOrder.Value);
            }

            SectionLayout childLayout = module.GetOrCreateLayout(newSectionName, newSectionName, sectionLayout, preferredOrder);
            module.RegisterSubsectionLayout(toggleId, childLayout);
        }

        private static bool IsMakeSubEffectInvocation(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                return string.Equals(memberAccess.Name.Identifier.Text, "makeSubEffect", StringComparison.Ordinal);
            }

            return false;
        }

        private static bool IsFindPropertyInvocation(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                return string.Equals(memberAccess.Name.Identifier.Text, "FindProperty", StringComparison.Ordinal);
            }

            if (invocation.Expression is IdentifierNameSyntax identifier)
            {
                return string.Equals(identifier.Identifier.Text, "FindProperty", StringComparison.Ordinal);
            }

            return false;
        }

        private static string ExtractPropertyIdentifier(SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            foreach (ArgumentSyntax argument in arguments)
            {
                string identifier = TryExtractPropertyIdentifier(argument.Expression);
                if (!string.IsNullOrEmpty(identifier))
                {
                    return identifier;
                }
            }

            return null;
        }

        private static string TryExtractPropertyIdentifier(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.Text;
                case RefExpressionSyntax { Expression: { } inner }:
                    return TryExtractPropertyIdentifier(inner);
                case CastExpressionSyntax cast:
                    return TryExtractPropertyIdentifier(cast.Expression);
                case ParenthesizedExpressionSyntax parenthesized:
                    return TryExtractPropertyIdentifier(parenthesized.Expression);
                case MemberAccessExpressionSyntax memberAccess:
                    return TryExtractPropertyIdentifier(memberAccess.Expression) ?? memberAccess.Name.Identifier.Text;
            }

            return null;
        }

        private static (string label, List<string> hints) ExtractLabelAndHints(SeparatedSyntaxList<ArgumentSyntax> arguments, IReadOnlyDictionary<string, string> strings)
        {
            string label = null;
            var hints = new List<string>();

            foreach (ArgumentSyntax argument in arguments)
            {
                if (TryExtractGuiContent(argument.Expression, strings, out string guiLabel, out List<string> guiHints))
                {
                    label ??= guiLabel;
                    if (guiHints.Count > 0)
                    {
                        hints.AddRange(guiHints);
                    }
                    continue;
                }

                string resolved = TryResolveString(argument.Expression, strings);
                if (!string.IsNullOrEmpty(resolved))
                {
                    label ??= resolved;
                }
            }

            return (label, hints);
        }

        private static bool TryExtractGuiContent(
            ExpressionSyntax expression,
            IReadOnlyDictionary<string, string> strings,
            out string label,
            out List<string> hints)
        {
            label = null;
            hints = new List<string>();

            if (expression is ObjectCreationExpressionSyntax creation && creation.Type.ToString().Contains("GUIContent", StringComparison.Ordinal))
            {
                SeparatedSyntaxList<ArgumentSyntax>? args = creation.ArgumentList?.Arguments;
                if (args.HasValue)
                {
                    if (args.Value.Count > 0)
                    {
                        label = TryResolveString(args.Value[0].Expression, strings);
                    }

                    if (args.Value.Count > 1)
                    {
                        string tooltip = TryResolveString(args.Value[1].Expression, strings);
                        if (!string.IsNullOrEmpty(tooltip))
                        {
                            hints.Add(tooltip);
                        }
                    }
                }

                return !string.IsNullOrEmpty(label) || hints.Count > 0;
            }

            return false;
        }

        private static string InferTypeFromInvocation(
            InvocationExpressionSyntax invocation,
            IReadOnlyDictionary<string, string> strings)
        {
            string identifier = invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
                IdentifierNameSyntax nameSyntax => nameSyntax.Identifier.Text,
                _ => string.Empty
            };

            if (identifier.Contains("Texture", StringComparison.OrdinalIgnoreCase) ||
                ArgumentContainsKeyword(invocation.ArgumentList.Arguments, strings, "texture"))
            {
                return "Texture";
            }

            if (identifier.Contains("Vector", StringComparison.OrdinalIgnoreCase) ||
                ArgumentContainsKeyword(invocation.ArgumentList.Arguments, strings, "vector"))
            {
                return "Vector";
            }

            if (identifier.Contains("Gradient", StringComparison.OrdinalIgnoreCase) ||
                identifier.Contains("Ramp", StringComparison.OrdinalIgnoreCase) ||
                ArgumentContainsKeyword(invocation.ArgumentList.Arguments, strings, "gradient") ||
                ArgumentContainsKeyword(invocation.ArgumentList.Arguments, strings, "ramp"))
            {
                return "Gradient";
            }

            if (identifier.Contains("Curve", StringComparison.OrdinalIgnoreCase) ||
                ArgumentContainsKeyword(invocation.ArgumentList.Arguments, strings, "curve"))
            {
                return "Curve";
            }

            if (identifier.Contains("EnumPopup", StringComparison.OrdinalIgnoreCase) ||
                identifier.Contains("drawEnumPopup", StringComparison.OrdinalIgnoreCase))
            {
                return "Enum";
            }

            if (identifier.Contains("Color", StringComparison.OrdinalIgnoreCase))
            {
                return "Color";
            }

            if (identifier.Contains("Toggle", StringComparison.OrdinalIgnoreCase))
            {
                return "Toggle";
            }

            return "Float";
        }

        private static string TryResolveString(ExpressionSyntax expression, IReadOnlyDictionary<string, string> strings)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    return literal.Token.ValueText;
                case IdentifierNameSyntax identifier when strings.TryGetValue(identifier.Identifier.Text, out string value):
                    return value;
                case MemberAccessExpressionSyntax member when strings.TryGetValue(member.Name.Identifier.Text, out string memberValue):
                    return memberValue;
            }

            return null;
        }

        private static string TryResolveEnumDisplayName(EnumMemberDeclarationSyntax member, IReadOnlyDictionary<string, string> strings)
        {
            foreach (AttributeListSyntax attributeList in member.AttributeLists)
            {
                foreach (AttributeSyntax attribute in attributeList.Attributes)
                {
                    string attributeName = attribute.Name.ToString();
                    if (attributeName.EndsWith("InspectorName", StringComparison.Ordinal) ||
                        attributeName.EndsWith("InspectorNameAttribute", StringComparison.Ordinal))
                    {
                        ExpressionSyntax argumentExpression = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                        string resolved = TryResolveString(argumentExpression, strings);
                        if (!string.IsNullOrEmpty(resolved))
                        {
                            return resolved;
                        }
                    }
                }
            }

            return member.Identifier.Text;
        }

        private sealed class EnumDefinition
        {
            public EnumDefinition(string name, List<string> values)
            {
                Name = name;
                Values = values;
            }

            public string Name { get; }
            public List<string> Values { get; }
        }

        private sealed class ShaderPropertyInfo
        {
            public ShaderPropertyInfo(
                string shaderPropertyName,
                string type,
                string displayName,
                float? min,
                float? max,
                float? defaultValue,
                int? defaultInt,
                float[] defaultColor,
                float[] defaultVector)
            {
                ShaderPropertyName = shaderPropertyName;
                Type = type;
                DisplayName = displayName;
                Min = min;
                Max = max;
                DefaultValue = defaultValue;
                DefaultInt = defaultInt;
                DefaultColor = defaultColor;
                DefaultVector = defaultVector;
            }

            public string ShaderPropertyName { get; }
            public string Type { get; }
            public string DisplayName { get; }
            public float? Min { get; }
            public float? Max { get; }
            public float? DefaultValue { get; }
            public int? DefaultInt { get; }
            public float[] DefaultColor { get; }
            public float[] DefaultVector { get; }
        }

        private sealed class PropertyContext
        {
            public PropertyContext(string variableName, string shaderProperty)
            {
                VariableName = variableName;
                ShaderPropertyName = shaderProperty;
            }

            public string VariableName { get; }
            public string ShaderPropertyName { get; }
            public string DisplayName { get; set; }
            public string PropertyType { get; set; } = UnknownType;
            public float? DefaultValue { get; set; }
            public int? DefaultInt { get; set; }
            public float[] DefaultColor { get; set; }
            public float[] DefaultVector { get; set; }
            public float? RangeMin { get; set; }
            public float? RangeMax { get; set; }
            public string EnumType { get; set; }
            public List<string> EnumValues { get; } = new();
            public bool IsToggle { get; set; }
            public bool IsIndented { get; set; }
            public List<string> Hints { get; } = new();
            public List<ConditionalRule> Conditions { get; } = new();
        }

        private sealed class SectionLayout
        {
            public SectionLayout(string name, string foldoutName, SectionLayout parent, int displayOrder)
            {
                Name = name;
                FoldoutName = string.IsNullOrEmpty(foldoutName) ? name : foldoutName;
                Parent = parent;
                DisplayOrder = displayOrder;
                IndentLevel = parent?.IndentLevel + 1 ?? 0;
                IsRoot = parent == null;
            }

            public string Name { get; }
            public string FoldoutName { get; private set; }
            public SectionLayout Parent { get; }
            public int IndentLevel { get; }
            public int DisplayOrder { get; private set; }
            public bool IsRoot { get; }

            public void UpdateFoldoutName(string foldoutName)
            {
                if (!string.IsNullOrEmpty(foldoutName) && string.Equals(FoldoutName, Name, StringComparison.Ordinal))
                {
                    FoldoutName = foldoutName;
                }
            }

            public void UpdateDisplayOrder(int? preferredOrder)
            {
                if (!preferredOrder.HasValue)
                {
                    return;
                }

                if (DisplayOrder < 0)
                {
                    DisplayOrder = preferredOrder.Value;
                }
                else
                {
                    DisplayOrder = Math.Min(DisplayOrder, preferredOrder.Value);
                }
            }
        }

        private sealed class ModuleContext
        {
            public ModuleContext(string name, string keywordProperty, string keywordDefine, string toggleIdentifier)
            {
                Name = name;
                KeywordProperty = keywordProperty;
                KeywordDefine = keywordDefine;
                ToggleIdentifier = toggleIdentifier;
            }

            public string Name { get; }
            public string KeywordProperty { get; }
            public string KeywordDefine { get; }
            public string ToggleIdentifier { get; }
            public int MakeEffectIndex { get; set; } = -1;
            public List<JuneSection> Sections { get; } = new();
            public List<JuneProperty> Properties { get; } = new();

            private readonly Dictionary<string, SectionLayout> _sectionLayouts = new(StringComparer.Ordinal);
            private readonly Dictionary<string, SectionLayout> _subsectionLayouts = new(StringComparer.Ordinal);
            private int _sectionOrderCounter;

            public SectionLayout GetOrCreateLayout(string name, string foldoutName, SectionLayout parent, int? preferredDisplayOrder)
            {
                if (!_sectionLayouts.TryGetValue(name, out SectionLayout layout))
                {
                    int order = preferredDisplayOrder ?? _sectionOrderCounter++;
                    layout = new SectionLayout(name, foldoutName, parent, order);
                    _sectionLayouts[name] = layout;
                }
                else
                {
                    layout.UpdateFoldoutName(foldoutName);
                    layout.UpdateDisplayOrder(preferredDisplayOrder);
                }

                return layout;
            }

            public void RegisterSubsectionLayout(string toggleId, SectionLayout layout)
            {
                if (string.IsNullOrEmpty(toggleId) || layout == null)
                {
                    return;
                }

                _subsectionLayouts[toggleId] = layout;
            }

            public bool TryGetSubsectionLayout(string toggleId, out SectionLayout layout)
            {
                if (string.IsNullOrEmpty(toggleId))
                {
                    layout = null;
                    return false;
                }

                return _subsectionLayouts.TryGetValue(toggleId, out layout);
            }

            private JuneSection GetOrCreateSection(SectionLayout layout)
            {
                JuneSection section = Sections.FirstOrDefault(s => string.Equals(s.name, layout.Name, StringComparison.Ordinal));
                if (section == null)
                {
                    section = new JuneSection
                    {
                        name = layout.Name,
                        foldoutName = layout.FoldoutName,
                        parentSection = layout.Parent?.Name,
                        isRootSection = layout.IsRoot,
                        indentLevel = layout.IndentLevel,
                        displayOrder = layout.DisplayOrder
                    };
                    Sections.Add(section);
                }
                else
                {
                    section.foldoutName ??= layout.FoldoutName;
                    section.parentSection ??= layout.Parent?.Name;
                    section.isRootSection |= layout.IsRoot;
                    if (section.displayOrder < 0)
                    {
                        section.displayOrder = layout.DisplayOrder;
                    }
                    else
                    {
                        section.displayOrder = Math.Min(section.displayOrder, layout.DisplayOrder);
                    }

                    if (section.indentLevel == 0 && !layout.IsRoot)
                    {
                        section.indentLevel = layout.IndentLevel;
                    }
                }

                return section;
            }

            public void AttachProperty(
                SectionLayout sectionLayout,
                PropertyContext context,
                List<ConditionalRule> activeConditions,
                bool isIndented,
                bool prioritizeDisplayOrder)
            {
                if (sectionLayout == null || context == null || string.IsNullOrEmpty(context.ShaderPropertyName))
                {
                    return;
                }

                context.IsIndented |= isIndented;
                if (activeConditions.Count == 0)
                {
                    context.Conditions.Clear();
                }
                JuneProperty existing = Properties.FirstOrDefault(p => p.shaderPropertyName == context.ShaderPropertyName);
                int index;
                string resolvedPropertyType = !string.IsNullOrEmpty(context.PropertyType)
                    ? context.PropertyType
                    : UnknownType;
                if (existing == null)
                {
                    existing = new JuneProperty
                    {
                        name = context.ShaderPropertyName.TrimStart('_'),
                        shaderPropertyName = context.ShaderPropertyName,
                        rawShaderPropertyName = context.ShaderPropertyName,
                        displayName = context.DisplayName,
                        propertyType = resolvedPropertyType,
                        min = context.RangeMin,
                        max = context.RangeMax,
                        defaultValue = context.DefaultValue,
                        defaultIntValue = context.DefaultInt,
                        defaultColor = context.DefaultColor,
                        defaultVector = context.DefaultVector,
                        enumTypeName = context.EnumType,
                        isToggle = context.IsToggle,
                        indented = context.IsIndented
                    };

                    if (context.EnumValues.Count > 0)
                    {
                        existing.enumValues.AddRange(context.EnumValues);
                    }

                    if (context.Hints.Count > 0)
                    {
                        existing.hints.AddRange(context.Hints);
                    }

                    if (context.Conditions.Count > 0)
                    {
                        existing.conditions.AddRange(context.Conditions);
                    }
                    else
                    {
                        existing.conditions.Clear();
                    }

                    Properties.Add(existing);
                    index = Properties.Count - 1;
                }
                else
                {
                    index = Properties.IndexOf(existing);

                    if (!string.IsNullOrEmpty(context.DisplayName) && string.IsNullOrEmpty(existing.displayName))
                    {
                        existing.displayName = context.DisplayName;
                    }

                    if (string.IsNullOrEmpty(existing.propertyType) || existing.propertyType == UnknownType)
                    {
                        existing.propertyType = resolvedPropertyType;
                    }
                    else if (!string.IsNullOrEmpty(resolvedPropertyType) &&
                             !string.Equals(existing.propertyType, resolvedPropertyType, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(resolvedPropertyType, "Range", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.propertyType = resolvedPropertyType;
                    }
                    existing.min ??= context.RangeMin;
                    existing.max ??= context.RangeMax;
                    existing.defaultValue ??= context.DefaultValue;
                    existing.defaultIntValue ??= context.DefaultInt;
                    existing.defaultVector ??= context.DefaultVector;
                    existing.enumTypeName ??= context.EnumType;
                    existing.isToggle |= context.IsToggle;
                    existing.indented |= context.IsIndented;
                    if (existing.enumValues.Count == 0 && context.EnumValues.Count > 0)
                    {
                        existing.enumValues.AddRange(context.EnumValues);
                    }
                    if (context.Hints.Count > 0)
                    {
                        existing.hints.AddRange(context.Hints.Except(existing.hints, StringComparer.Ordinal));
                    }

                    if (activeConditions.Count == 0)
                    {
                        existing.conditions.Clear();
                    }
                }

                JuneSection section = GetOrCreateSection(sectionLayout);

                if (!section.propertyIndices.Contains(index))
                {
                    section.propertyIndices.Add(index);
                }
                else if (prioritizeDisplayOrder)
                {
                    section.propertyIndices.Remove(index);
                    section.propertyIndices.Add(index);
                }

                if (activeConditions.Count > 0)
                {
                    existing.conditions.AddRange(activeConditions);
                }
            }

            public JuneModule ToJuneModule()
            {
                return new JuneModule
                {
                    name = Name,
                    keyword = KeywordProperty,
                    keywordDefine = KeywordDefine,
                    sections = Sections,
                    properties = Properties
                };
            }
        }

        private static void PopulateTypeSpecificHints(
            InvocationExpressionSyntax invocation,
            PropertyContext context,
            IReadOnlyDictionary<string, float> numbers,
            IReadOnlyDictionary<string, EnumDefinition> enumDefinitions)
        {
            string identifier = invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
                IdentifierNameSyntax nameSyntax => nameSyntax.Identifier.Text,
                GenericNameSyntax genericName => genericName.Identifier.Text,
                _ => string.Empty
            };

            if (identifier.Contains("Toggle", StringComparison.OrdinalIgnoreCase))
            {
                context.IsToggle = true;
            }

            if (identifier.Contains("Color", StringComparison.OrdinalIgnoreCase) && context.DefaultColor == null)
            {
                float[] color = TryExtractColor(invocation.ArgumentList.Arguments, numbers);
                if (color != null)
                {
                    context.DefaultColor = color;
                }
            }

            if (identifier.Contains("Slider", StringComparison.OrdinalIgnoreCase) || identifier.Contains("Range", StringComparison.OrdinalIgnoreCase))
            {
                (float? min, float? max) = TryExtractRange(invocation.ArgumentList.Arguments, numbers);
                context.RangeMin ??= min;
                context.RangeMax ??= max;
                context.PropertyType = "Range";
            }

            if (identifier.Contains("Enum", StringComparison.OrdinalIgnoreCase))
            {
                string enumType = TryExtractEnumType(invocation.Expression) ?? TryExtractEnumType(invocation.ArgumentList.Arguments);
                if (!string.IsNullOrEmpty(enumType))
                {
                    context.EnumType = enumType;
                    if (enumDefinitions.TryGetValue(enumType, out EnumDefinition definition) && context.EnumValues.Count == 0)
                    {
                        context.EnumValues.AddRange(definition.Values);
                    }
                }
            }

            if (identifier.Contains("Vector", StringComparison.OrdinalIgnoreCase) && context.DefaultVector == null)
            {
                float[] vector = TryExtractVector(invocation.ArgumentList.Arguments, numbers);
                if (vector != null)
                {
                    context.DefaultVector = vector;
                }
            }
        }

        private static (float? min, float? max) TryExtractRange(
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            IReadOnlyDictionary<string, float> numbers)
        {
            if (arguments.Count < 2)
            {
                return (null, null);
            }

            float? min = TryGetNumeric(arguments[arguments.Count - 2], numbers);
            float? max = TryGetNumeric(arguments[arguments.Count - 1], numbers);
            return (min, max);
        }

        private static float? TryGetNumeric(ArgumentSyntax argument, IReadOnlyDictionary<string, float> numbers)
        {
            return EvaluateNumericExpression(argument?.Expression, numbers);
        }

        private static float[] TryExtractColor(
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            IReadOnlyDictionary<string, float> numbers)
        {
            foreach (ArgumentSyntax argument in arguments)
            {
                if (argument.Expression is ObjectCreationExpressionSyntax creation && creation.ArgumentList?.Arguments.Count == 4)
                {
                    var color = new float[4];
                    for (int i = 0; i < 4; i++)
                    {
                        color[i] = TryGetNumeric(creation.ArgumentList.Arguments[i], numbers) ?? 0f;
                    }

                    return color;
                }
            }

            return null;
        }

        private static float[] TryExtractVector(
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            IReadOnlyDictionary<string, float> numbers)
        {
            foreach (ArgumentSyntax argument in arguments)
            {
                if (argument.Expression is ObjectCreationExpressionSyntax creation && creation.ArgumentList?.Arguments.Count >= 2)
                {
                    int count = creation.ArgumentList.Arguments.Count;
                    var vector = new float[Math.Max(count, 4)];
                    for (int i = 0; i < count; i++)
                    {
                        vector[i] = TryGetNumeric(creation.ArgumentList.Arguments[i], numbers) ?? 0f;
                    }

                    return vector;
                }
            }

            return null;
        }

        private static bool ArgumentContainsKeyword(
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            IReadOnlyDictionary<string, string> strings,
            string keyword)
        {
            foreach (ArgumentSyntax argument in arguments)
            {
                string resolved = TryResolveString(argument.Expression, strings);
                if (!string.IsNullOrEmpty(resolved) && resolved.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string TryExtractEnumType(SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            foreach (ArgumentSyntax argument in arguments)
            {
                string typeName = TryExtractEnumType(argument.Expression);
                if (!string.IsNullOrEmpty(typeName))
                {
                    return typeName;
                }
            }

            return null;
        }

        private static string TryExtractEnumType(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }:
                    return genericName.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
                case GenericNameSyntax nameSyntax:
                    return nameSyntax.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
                case CastExpressionSyntax cast:
                    return cast.Type.ToString();
                case ParenthesizedExpressionSyntax parenthesized:
                    return TryExtractEnumType(parenthesized.Expression);
            }

            return null;
        }

        private static void RegisterProperty(
            ModuleContext module,
            SectionLayout sectionLayout,
            string variableName,
            string shaderProperty,
            IDictionary<string, PropertyContext> propertyMap,
            List<ConditionalRule> activeConditions,
            int indentLevel)
        {
            if (string.IsNullOrEmpty(variableName) || string.IsNullOrEmpty(shaderProperty))
            {
                return;
            }

            if (!propertyMap.TryGetValue(variableName, out PropertyContext context))
            {
                context = new PropertyContext(variableName, shaderProperty);
                propertyMap[variableName] = context;
            }

            ApplyShaderMetadata(context);

            bool isIndented = indentLevel > 0;
            context.IsIndented |= isIndented;

            if (activeConditions.Count > 0)
            {
                context.Conditions.AddRange(activeConditions);
            }

            module.AttachProperty(sectionLayout, context, activeConditions, isIndented, false);
        }

        private static void ApplyShaderMetadata(PropertyContext context)
        {
            if (context == null)
            {
                return;
            }

            if (s_shaderPropertyMap.TryGetValue(context.ShaderPropertyName, out ShaderPropertyInfo info))
            {
                if (string.IsNullOrEmpty(context.PropertyType) || string.Equals(context.PropertyType, UnknownType, StringComparison.Ordinal))
                {
                    context.PropertyType = info.Type;
                }

                context.DisplayName ??= info.DisplayName;
                context.RangeMin ??= info.Min;
                context.RangeMax ??= info.Max;
                context.DefaultValue ??= info.DefaultValue;
                context.DefaultInt ??= info.DefaultInt;
                context.DefaultColor ??= info.DefaultColor;
                context.DefaultVector ??= info.DefaultVector;
            }
            else if (string.Equals(context.PropertyType, UnknownType, StringComparison.Ordinal) &&
                     context.ShaderPropertyName.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                context.PropertyType = "Color";
            }
        }

        private static string NormalizeShaderPropertyType(string shaderType)
        {
            if (string.IsNullOrEmpty(shaderType))
            {
                return UnknownType;
            }

            if (shaderType.StartsWith("Range", StringComparison.OrdinalIgnoreCase))
            {
                return "Range";
            }

            if (shaderType.Equals("Color", StringComparison.OrdinalIgnoreCase))
            {
                return "Color";
            }

            if (shaderType.Equals("Vector", StringComparison.OrdinalIgnoreCase))
            {
                return "Vector";
            }

            if (shaderType.Equals("Int", StringComparison.OrdinalIgnoreCase))
            {
                return "Int";
            }

            if (shaderType.Equals("2D", StringComparison.OrdinalIgnoreCase) ||
                shaderType.Equals("3D", StringComparison.OrdinalIgnoreCase) ||
                shaderType.Equals("Cube", StringComparison.OrdinalIgnoreCase) ||
                shaderType.Equals("Texture", StringComparison.OrdinalIgnoreCase))
            {
                return "Texture";
            }

            return "Float";
        }

        private static float? EvaluateNumericExpression(ExpressionSyntax expression, IReadOnlyDictionary<string, float> knownValues)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NumericLiteralExpression):
                    return Convert.ToSingle(literal.Token.Value);
                case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.UnaryMinusExpression):
                    return -EvaluateNumericExpression(prefix.Operand, knownValues);
                case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.UnaryPlusExpression):
                    return EvaluateNumericExpression(prefix.Operand, knownValues);
                case ParenthesizedExpressionSyntax parenthesized:
                    return EvaluateNumericExpression(parenthesized.Expression, knownValues);
                case IdentifierNameSyntax identifier when knownValues.TryGetValue(identifier.Identifier.Text, out float value):
                    return value;
                case MemberAccessExpressionSyntax member when knownValues.TryGetValue(member.Name.Identifier.Text, out float memberValue):
                    return memberValue;
                case CastExpressionSyntax cast:
                    return EvaluateNumericExpression(cast.Expression, knownValues);
                case BinaryExpressionSyntax binary:
                    return EvaluateBinaryNumericExpression(binary, knownValues);
            }

            return null;
        }

        private static float? EvaluateBinaryNumericExpression(BinaryExpressionSyntax binary, IReadOnlyDictionary<string, float> knownValues)
        {
            float? left = EvaluateNumericExpression(binary.Left, knownValues);
            float? right = EvaluateNumericExpression(binary.Right, knownValues);

            if (!left.HasValue || !right.HasValue)
            {
                return null;
            }

            return binary.Kind() switch
            {
                SyntaxKind.AddExpression => left + right,
                SyntaxKind.SubtractExpression => left - right,
                SyntaxKind.MultiplyExpression => left * right,
                SyntaxKind.DivideExpression => right.Value != 0 ? left / right : (float?)null,
                SyntaxKind.ModuloExpression => right.Value != 0 ? left % right : (float?)null,
                _ => null
            };
        }
    }
}
#endif
