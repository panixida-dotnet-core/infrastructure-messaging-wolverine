using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal sealed class OptionBindingSourceBuilder(Compilation compilation, INamedTypeSymbol? keyNameAttribute, int optionIndex)
{
    private const string Section = "global::Microsoft.Extensions.Configuration.IConfigurationSection";
    private const string Collections = "global::System.Collections.Generic.";
    private const string AnyChildren = "global::System.Linq.Enumerable.Any(section.GetChildren())";
    private const string ReturnOption = "        return option;\n";
    private const string EndMethod = "    }\n";
    private const string FalseLiteral = "false";
    private const string DefaultValue = "default";
    private const string OptionVariable = "option";
    private const string ChildVariable = "child";
    private const string ConversionVariable = "scalarConversion";
    private const string WritableCondition = "!readOnly";
    private const string SetInterface = "System.Collections.Generic.ISet<T>";
    private const string EnumerableInterface = "System.Collections.Generic.IEnumerable<T>";
    private const string ReadOnlySetInterface = "System.Collections.Generic.IReadOnlySet<T>";
    private const string Accessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";
    private const string AccessorKind = "global::System.Runtime.CompilerServices.UnsafeAccessorKind.";
    private static readonly HashSet<string> SupportedCollectionInterfaces = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.IDictionary<TKey, TValue>",
        "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>",
        EnumerableInterface,
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.IList<T>",
        SetInterface,
        ReadOnlySetInterface,
    };
    private readonly Dictionary<ITypeSymbol, string> methods = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ITypeSymbol> activeDefinitions = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, string> converterMethods = new(StringComparer.Ordinal);
    private readonly StringBuilder source = new();

    internal string? Error { get; private set; }

    internal string Complete() => source.ToString();

    internal string Build(ITypeSymbol type)
    {
        if (Error is not null)
        {
            return string.Empty;
        }

        type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        if (methods.TryGetValue(type, out var existing))
        {
            return existing;
        }

        var name = $"Bind{optionIndex}_{methods.Count}";
        methods.Add(type, name);
        var definition = type.OriginalDefinition;
        var added = activeDefinitions.Add(definition);
        var remaining = GenericTypeBudget.MaxRecursiveTypeNodes;
        if (!added && !GenericTypeBudget.TryConsume(type, ref remaining))
        {
            Error = $"recursive generic model '{definition}' expands beyond the supported type complexity";
            return name;
        }

        var body = new StringBuilder();
        string? converter = null;
        if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic ||
            type is { TypeKind: TypeKind.Class, IsAbstract: true })
        {
            Error = $"type '{type}' must be a concrete configuration model; object, dynamic and abstract models are not supported";
        }
        else if (type.IsRefLikeType || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
        {
            Error = $"type '{type}' must be accessible to generated code";
        }
        else
        {
            converter = GetCustomConverter(type);
            AppendBody(body, type, name, converter);
        }
        if (added)
        {
            activeDefinitions.Remove(definition);
        }

        var byteArray = IsByteArray(type);
        var arguments = "bool readOnly = false, bool parentCollection = false, bool? conversionProbe = null, bool hasExistingValue = true" +
            (byteArray ? ", global::System.Func<byte[]> getInitialValue = null" : string.Empty) +
            (converter is not null ? ", global::System.Action<object> captureConvertedValue = null" : string.Empty);
        var forwarded = "readOnly, parentCollection, conversionProbe, hasExistingValue" +
            (byteArray ? ", getInitialValue" : string.Empty) +
            (converter is not null ? ", captureConvertedValue" : string.Empty);
        source.Append($"    private static {Name(type)} {name}({Section} section, {Name(type)} option, {arguments})\n")
            .Append($"        => {name}(section, option, out _, {forwarded});\n")
            .Append($"    private static {Name(type)} {name}({Section} section, {Name(type)} option, out bool hasNewValue, {arguments})\n")
            .Append($"        => {name}(section, option, out hasNewValue, out _, {forwarded});\n")
            .Append($"    private static {Name(type)} {name}({Section} section, {Name(type)} option, out bool hasNewValue, out bool convertedNull, {arguments})\n    {{\n")
            .Append("        convertedNull = false;\n")
            .Append($"        hasNewValue = {(type.IsValueType ? WritableCondition : FalseLiteral)};\n")
            .Append(body).Append(EndMethod);
        return name;
    }

    private void AppendBody(StringBuilder body, ITypeSymbol type, string method, string? converter)
    {
        if (IsNullable(type))
        {
            body.Append("        if (IsNullValue(section)) { convertedNull = true; return readOnly ? option : default; }\n");
        }
        var conversion = converter is null ? OptionBindingGenerator.GetConversion(type) : null;
        if (conversion is not null)
        {
            AppendScalarChildren(body, type, method);
            var nullTest = IsNullable(type) ? "global::System.String.IsNullOrEmpty(section.Value)" : "section.Value is null";
            body.Append($"        convertedNull = {nullTest};\n        var convertedValue = convertedNull ? default({Name(type)}) : {OptionBindingGenerator.Registry}.Parse(section, static value => {conversion});\n")
                .Append("        if (readOnly) return option;\n        hasNewValue = true;\n        return convertedValue;\n");
            return;
        }

        if (converter is not null)
        {
            var nullTest = IsNullable(type) ? "global::System.String.IsNullOrEmpty(section.Value)" : "section.Value is null";
            var convertedType = IsNullable(type) ? ((INamedTypeSymbol)type).TypeArguments[0] : type;
            body.Append($"        if (section.Value is not null || IsNullValue(section) || !{AnyChildren})\n        {{\n            global::System.ComponentModel.TypeConverter converter = {converter};\n")
                .Append("            if ((conversionProbe ??= converter.CanConvertFrom(typeof(string))) == true)\n            {\n")
                .Append($"                var nullConversion = {nullTest};\n")
                .Append($"                var convertedValue = nullConversion ? default({Name(type)}) : {OptionBindingGenerator.Registry}.Parse(section, value =>\n                {{\n                    var result = converter.ConvertFromInvariantString(value);\n                    nullConversion = result is null;\n                    if (captureConvertedValue is not null)\n                    {{\n                        captureConvertedValue(result);\n                        return default({Name(convertedType)});\n                    }}\n                    return readOnly || nullConversion ? default({Name(convertedType)}) : ({Name(convertedType)})result;\n                }});\n")
                .Append("                convertedNull = nullConversion;\n")
                .Append("                if (readOnly) return option;\n                hasNewValue = true;\n                return nullConversion ? default : convertedValue;\n            }\n        }\n");
        }

        AppendComplexBody(body, type, method);
    }

    private void AppendScalarChildren(StringBuilder body, ITypeSymbol type, string method)
    {
        body.Append($"        if (section.Value is null && !IsNullValue(section) && {AnyChildren})\n        {{\n");
        if (IsNullable(type))
        {
            var underlying = ((INamedTypeSymbol)type).TypeArguments[0];
            body.Append($"            if (readOnly && !option.HasValue) return option;\n            var boundValue = {Build(underlying)}(section, option.GetValueOrDefault(), readOnly: readOnly);\n            return readOnly ? option : boundValue;\n");
        }
        else
        {
            var named = (INamedTypeSymbol)type;
            if (type.IsReferenceType)
            {
                AppendRuntimeTypeCheck(body, named);
                var creation = Construction(named, method);
                body.Append($"            if (option is null)\n            {{\n                if (readOnly) return option;\n                hasNewValue = true;\n                option = {creation};\n            }}\n");
            }
            AppendProperties(body, named, method, []);
        }
        body.Append("        }\n");
    }

    private void AppendComplexBody(StringBuilder body, ITypeSymbol type, string method)
    {
        if (Name(type) == Section)
        {
            body.Append("        hasNewValue = !readOnly;\n        return readOnly ? option : section;\n");
        }
        else if (IsNullable(type))
        {
            var underlying = ((INamedTypeSymbol)type).TypeArguments[0];
            body.Append("        if (readOnly && !option.HasValue) return option;\n        if (section.Value == string.Empty) return readOnly ? option : default;\n")
                .Append($"        if (!{AnyChildren}) return section.Value is null && !readOnly ? default : option;\n")
                .Append($"        var boundValue = {Build(underlying)}(section, option.GetValueOrDefault(), conversionProbe: conversionProbe, hasExistingValue: option.HasValue);\n        return readOnly ? option : boundValue;\n");
        }
        else if (type is IArrayTypeSymbol array)
        {
            if (array.Rank != 1)
            {
                Error = $"array '{type}' must be one-dimensional";
                return;
            }

            AppendSequence(body, type, array.ElementType, copy: true);
        }
        else if (type is INamedTypeSymbol named)
        {
            AppendNamedBody(body, named, method);
        }
        else
        {
            Error = $"type '{type}' is not a supported configuration value";
        }
    }

    private void AppendNamedBody(StringBuilder body, INamedTypeSymbol type, string method)
    {
        var definition = type.OriginalDefinition.ToDisplayString();
        if (type.TypeKind == TypeKind.Interface && !SupportedCollectionInterfaces.Contains(definition))
        {
            Error = $"interface '{type}' must be replaced with a concrete configuration model or a supported collection interface";
            return;
        }

        var dictionary = FindInterface(type, "System.Collections.Generic.IDictionary<TKey, TValue>") ??
            (definition == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>" ? type : null);
        if (dictionary is not null)
        {
            AppendDictionary(body, type, dictionary);
            return;
        }

        var copy = definition is EnumerableInterface or
            "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.IReadOnlyCollection<T>";
        var sequence = FindInterface(type, type.TypeKind == TypeKind.Interface
            ? EnumerableInterface : "System.Collections.Generic.ICollection<T>");
        if (sequence is not null)
        {
            AppendSequence(body, type, sequence.TypeArguments[0], copy);
            return;
        }

        var creation = Construction(type, method);
        body.Append($"        if (!{AnyChildren})\n        {{\n")
            .Append($"            if (parentCollection && !readOnly && {(type.IsReferenceType ? "option is null" : "!hasExistingValue")} && global::System.String.IsNullOrEmpty(section.Value))\n            {{\n                hasNewValue = true;\n                return {creation};\n            }}\n")
            .Append(type.IsReferenceType ? "            hasNewValue = !readOnly && option is null && global::System.String.IsNullOrEmpty(section.Value);\n" : string.Empty)
            .Append("            return option;\n        }\n");
        AppendRuntimeTypeCheck(body, type);
        var constructor = GetConstructor(type);
        var parameters = constructor?.Parameters.Select(parameter => parameter.Name).ToArray() ?? [];
        if (type.IsReferenceType)
        {
            if (parameters.Length > 0)
            {
                body.Append("        var created = option is null;\n");
            }

            body.Append("        if (option is null)\n        {\n            if (readOnly) return option;\n            hasNewValue = true;\n")
                .Append("            option = ").Append(creation).Append(";\n        }\n");
        }
        else
        {
            body.Append($"        if (!hasExistingValue) option = {creation};\n");
        }

        AppendProperties(body, type, method, parameters);
    }

    private static void AppendRuntimeTypeCheck(StringBuilder body, INamedTypeSymbol type)
    {
        if (type.IsSealed || type.IsValueType)
        {
            return;
        }

        body.Append("        if (option is not null)\n        {\n            var runtimeType = ((object)option).GetType();\n");
        body.Append($"            if (runtimeType != typeof({Name(type)}))\n")
            .Append($"                throw new UnsupportedRuntimeTypeException(runtimeType, typeof({Name(type)}), section.Path);\n        }}\n");
    }

    private void AppendProperties(StringBuilder body, INamedTypeSymbol type, string method, string[] parameters)
    {
        body.Append($"        var children = new {Collections}Dictionary<string, {Section}>(global::System.StringComparer.OrdinalIgnoreCase);\n")
            .Append("        foreach (var child in section.GetChildren()) children[child.Key] = child;\n");
        var index = 0;
        foreach (var property in OptionBindingGenerator.GetProperties(type))
        {
            AppendProperty(body, type, property, method, index++, type.IsReferenceType && parameters.Contains(property.Name));
        }

        body.Append(ReturnOption);
    }

    private void AppendProperty(StringBuilder body, INamedTypeSymbol type, IPropertySymbol property, string method, int index, bool constructorBound)
    {
        var writable = property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
        AppendPropertySection(body, property, index);
        if (IsAbstractFrameworkMember(property))
        {
            body.Append($"        if (hasChild{index}) throw new global::System.NotSupportedException($\"The framework property {property.Name} at '{{child{index}.Path}}' requires a concrete configuration model.\");\n");
            return;
        }
        var receiver = property.ContainingType.IsValueType ? "ref option" : "option";
        var getter = AppendGetter(property, method + "Get" + index) + "(" + receiver + ")";
        var probe = ConversionVariable + index;
        AppendConversionProbe(body, property.Type, probe);
        var initial = InitialValue(property.Type, ChildVariable + index, getter, probe, readOnly: !writable);
        var byteArray = IsByteArray(property.Type);
        var initialArgument = byteArray ? DefaultValue : initial;
        var initialGetter = byteArray ? $", getInitialValue: () => {initial}" : string.Empty;
        var value = "value" + index;
        body.Append($"        var changed{index} = false;\n")
            .Append($"        {Name(property.Type)} {value} = {(constructorBound ? "!created && " : string.Empty)}hasChild{index}\n")
            .Append($"            ? {Build(property.Type)}(child{index}, {initialArgument}, out changed{index}, {(writable ? FalseLiteral : "true")}{ConversionArgument(property.Type, probe)}{initialGetter})\n")
            .Append($"            : {(writable ? getter : DefaultValue)};\n");
        if (!writable)
        {
            return;
        }

        if (property.Type.IsReferenceType || IsNullable(property.Type))
        {
            body.Append($"        if ({(constructorBound ? "created || " : string.Empty)}{value} is not null || changed{index})\n");
        }

        body.Append("        {\n");
        AppendSetter(body, type, property, method + "Set" + index, value);
        body.Append("        }\n");
    }

    private bool IsAbstractFrameworkMember(IPropertySymbol property)
    {
        var model = property.Type is IArrayTypeSymbol array ? array.ElementType : property.Type;
        return model is { TypeKind: TypeKind.Class, IsAbstract: true } &&
            SymbolEqualityComparer.Default.Equals(property.ContainingAssembly, compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly);
    }

    private void AppendSetter(StringBuilder body, INamedTypeSymbol type, IPropertySymbol property, string method, string value)
    {
        if (property.SetMethod!.IsInitOnly)
        {
            var reference = property.ContainingType.IsValueType ? "ref " : string.Empty;
            var setter = AppendAccessor(property.SetMethod, method);
            AppendInvocation(body, $"{setter}({reference}option, {value});", indent: "            ");
        }
        else
        {
            var receiver = Name(property.ContainingType);
            if (!property.IsStatic)
            {
                receiver = SymbolEqualityComparer.Default.Equals(type, property.ContainingType)
                    ? OptionVariable : $"(({Name(property.ContainingType)})option)";
            }
            AppendInvocation(body, $"{receiver}.@{property.Name} = {value};", indent: "            ");
        }
    }

    private void AppendPropertySection(StringBuilder body, IPropertySymbol property, int index)
    {
        var key = property.GetAttributes().FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, keyNameAttribute))
            ?.ConstructorArguments.FirstOrDefault().Value as string ?? property.Name;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = property.Name;
        }
        var literal = SymbolDisplay.FormatLiteral(key, true);
        var lookup = $"children.TryGetValue({literal}, out _)";
        var separator = key.LastIndexOf(':');
        if (Name(property.Type) == Section)
        {
            lookup = "true";
        }
        else if (separator >= 0)
        {
            var parent = SymbolDisplay.FormatLiteral(key.Substring(0, separator), true);
            var childKey = SymbolDisplay.FormatLiteral(key.Substring(separator + 1), true);
            lookup = $"global::System.Linq.Enumerable.Any(section.GetSection({parent}).GetChildren(), child => global::System.String.Equals(child.Key, {childKey}, global::System.StringComparison.OrdinalIgnoreCase))";
        }
        body.Append($"        var hasChild{index} = {lookup};\n")
            .Append($"        var child{index} = hasChild{index} ? section.GetSection({literal}) : default;\n");
    }

    private string AppendGetter(IPropertySymbol property, string method)
    {
        var body = new StringBuilder();
        var reference = property.ContainingType.IsValueType ? "ref " : string.Empty;
        AppendInvocation(body, $"return {(property.IsStatic ? Name(property.ContainingType) : OptionVariable)}.@{property.Name};");
        source.Append($"    private static {Name(property.Type)} {method}({reference}{Name(property.ContainingType)} option)\n    {{\n")
            .Append(body).Append(EndMethod);
        return method;
    }

    private string InitialValue(ITypeSymbol type, string child, string getter, string probe = ConversionVariable, bool readOnly = false)
    {
        if (IsByteArray(type))
        {
            return readOnly ? $"!IsNullValue({child}) && {child}.Value is null && global::System.Linq.Enumerable.Any({child}.GetChildren()) ? default : {getter}" : getter;
        }
        if (readOnly && IsArrayLike(type))
        {
            return $"global::System.Linq.Enumerable.Any({child}.GetChildren()) || !global::System.String.IsNullOrEmpty({child}.Value) ? default : {getter}";
        }
        var readOnlyLeaf = $"!global::System.String.IsNullOrEmpty({child}.Value) && !global::System.Linq.Enumerable.Any({child}.GetChildren())";
        var skipGetter = readOnly ? readOnlyLeaf + " || " : string.Empty;
        var conversion = ScalarConversionCondition(type, child, probe);
        if (conversion == "true")
        {
            return OptionBindingGenerator.GetConversion(type) is not null
                ? $"{child}.Value is not null || IsNullValue({child}) || !global::System.Linq.Enumerable.Any({child}.GetChildren()) ? default : {getter}"
                : DefaultValue;
        }
        if (conversion != FalseLiteral)
        {
            var nullValue = IsNullable(type) ? $"IsNullValue({child}) || " : string.Empty;
            return $"{skipGetter}{nullValue}({child}.Value is not null || IsNullValue({child}) || !global::System.Linq.Enumerable.Any({child}.GetChildren())) && ({conversion}) ? default : {getter}";
        }
        if (IsNullable(type))
        {
            return $"{skipGetter}IsNullValue({child}) || ({child}.Value is null && !global::System.Linq.Enumerable.Any({child}.GetChildren())) ? default : {getter}";
        }
        return readOnly ? $"{readOnlyLeaf} ? default : {getter}" : getter;
    }

    private static bool IsByteArray(ITypeSymbol type)
        => type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte };

    private static bool IsArrayLike(ITypeSymbol type)
        => type is IArrayTypeSymbol || type.OriginalDefinition.ToDisplayString() is
            EnumerableInterface or "System.Collections.Generic.IReadOnlyCollection<T>" or "System.Collections.Generic.IReadOnlyList<T>";

    private string Construction(INamedTypeSymbol type, string method)
    {
        var constructor = GetConstructor(type);
        if (constructor is null)
        {
            var reason = type.InstanceConstructors.Any(candidate => candidate.DeclaredAccessibility == Accessibility.Public)
                ? "it has multiple public parameterized constructors." : "it is missing a public instance constructor.";
            return InvalidConstruction(type, method, reason);
        }

        if (type.IsValueType && constructor.IsImplicitlyDeclared)
        {
            return "default(" + Name(type) + ")";
        }

        var properties = OptionBindingGenerator.GetProperties(type, includeNonBindable: true).ToArray();
        var invalid = constructor.Parameters.FirstOrDefault(parameter => parameter.RefKind != RefKind.None);
        if (invalid is not null)
        {
            return InvalidConstruction(type, method, $"one or more parameters cannot be bound to. Constructor parameters cannot be declared as in, out, or ref. Invalid parameters are: '{invalid.Name}'");
        }
        var missing = constructor.Parameters.Where(parameter => !properties.Any(property => string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(parameter => parameter.Name).ToArray();
        if (missing.Length > 0)
        {
            return InvalidConstruction(type, method, $"one or more parameters cannot be bound to. Constructor parameters must have corresponding properties. Fields are not supported. Missing properties are: '{string.Join(",", missing)}'");
        }
        var arguments = new List<string>();
        var signature = new List<string>();
        foreach (var parameter in constructor.Parameters)
        {
            var section = "section.GetSection(" + SymbolDisplay.FormatLiteral(parameter.Name, true) + ")";
            arguments.Add(AppendParameter(type, parameter, method + "Parameter" + arguments.Count) + "(" + section + ")");
            signature.Add(Name(parameter.Type) + " argument" + signature.Count);
        }

        var invocation = properties.Any(property => property.IsRequired)
            ? AppendAccessor(constructor, method + "Create")
            : "new " + Name(type);
        var body = new StringBuilder();
        var parameterNames = Enumerable.Range(0, arguments.Count).Select(index => "argument" + index);
        AppendInvocation(body, $"return {invocation}({string.Join(", ", parameterNames)});",
            activationType: arguments.Count == 0 ? Name(type) : null);
        source.Append($"    private static {Name(type)} {method}Construct({string.Join(", ", signature)})\n    {{\n")
            .Append(body).Append(EndMethod);

        return method + "Construct(" + string.Join(", ", arguments) + ")";
    }

    private string AppendParameter(INamedTypeSymbol type, IParameterSymbol parameter, string method)
    {
        var parameterType = parameter.Type;
        var missing = MissingParameter(type, parameter);
        var missingStatement = parameter.HasExplicitDefaultValue ? "return " + missing : missing;
        var body = new StringBuilder();
        if (Name(parameterType) != Section)
        {
            body.Append($"        if (!global::Microsoft.Extensions.Configuration.ConfigurationExtensions.Exists(section)) {missingStatement};\n");
        }
        AppendConversionProbe(body, parameterType);
        var conversion = ScalarConversionCondition(parameterType, "section");
        var arraySeed = false;
        if (conversion != "true")
        {
            var nullableEmpty = IsNullable(parameterType) ? " && section.Value != string.Empty" : string.Empty;
            arraySeed = IsArrayLike(parameterType);
            var arrayLeaf = arraySeed ? $" && !{AnyChildren}" : string.Empty;
            body.Append($"        if (section.Value is not null{nullableEmpty}{arrayLeaf} && !({conversion}))\n")
                .Append($"            throw new global::System.ArgumentException($\"Object of type 'System.String' cannot be converted to type '{{typeof({Name(parameterType)})}}'.\");\n");
        }

        body.Append($"        var value = {Build(parameterType)}(section, default, out _, out var convertedNull, hasExistingValue: false{ConversionArgument(parameterType)});\n")
            .Append($"        if (convertedNull) {missingStatement};\n");
        if (arraySeed)
        {
            var element = parameterType is IArrayTypeSymbol array ? array.ElementType : ((INamedTypeSymbol)parameterType).TypeArguments[0];
            body.Append($"        if (section.Value is {{ Length: > 0 }} seed && {AnyChildren})\n")
                .Append($"            return MergeConstructorSeed<{Name(element)}>(seed, value);\n");
        }
        body.Append(parameterType.IsReferenceType || IsNullable(parameterType)
            ? $"        return value is null ? {missing} : value;\n"
            : "        return value;\n");
        source.Append($"    private static {Name(parameterType)} {method}({Section} section)\n    {{\n")
            .Append(body).Append(EndMethod);
        return method;
    }

    private static void AppendInvocation(StringBuilder body, string statement, string? activationType = null, string indent = "        ")
    {
        var exception = "new global::System.Reflection.TargetInvocationException(exception)";
        if (activationType is not null)
        {
            exception = $"new global::System.InvalidOperationException($\"Failed to create instance of type '{{typeof({activationType})}}'.\", {exception})";
        }

        body.Append($"{indent}try\n{indent}{{\n{indent}    {statement}\n{indent}}}\n")
            .Append($"{indent}catch (global::System.Exception exception)\n{indent}{{\n{indent}    throw {exception};\n{indent}}}\n");
    }

    private static string MissingParameter(INamedTypeSymbol type, IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return $"throw new global::System.InvalidOperationException($\"Cannot create instance of type '{{typeof({Name(type)})}}' because parameter '{parameter.Name}' has no matching config. Each parameter in the constructor that does not have a default value must have a corresponding config entry.\")";
        }

        if (parameter.ExplicitDefaultValue is null)
        {
            return DefaultValue;
        }

        var suffix = parameter.ExplicitDefaultValue is decimal ? "m" : string.Empty;
        return "(" + Name(parameter.Type) + ")" + SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false) + suffix;
    }

    private string InvalidConstruction(INamedTypeSymbol type, string method, string message)
    {
        source.Append($"    private static {Name(type)} {method}CannotCreate() => throw new global::System.InvalidOperationException($\"Cannot create instance of type '{{typeof({Name(type)})}}' because \" + {SymbolDisplay.FormatLiteral(message, true)});\n");
        return method + "CannotCreate()";
    }

    private string AppendAccessor(IMethodSymbol target, string accessorName)
    {
        var containingTypes = new Stack<INamedTypeSymbol>();
        for (var type = target.ContainingType; type is not null; type = type.ContainingType)
        {
            containingTypes.Push(type);
        }

        var parameters = containingTypes.SelectMany(type => type.OriginalDefinition.TypeParameters).ToArray();
        var arguments = containingTypes.SelectMany(type => type.TypeArguments).ToArray();
        var parameterNames = parameters.Select((_, index) => "T" + index).ToArray();
        string DefinitionName(ISymbol symbol) => string.Concat(symbol.ToDisplayParts(SymbolDisplayFormat.FullyQualifiedFormat)
            .Select(part => part.Symbol is ITypeParameterSymbol parameter
                ? parameterNames[Array.FindIndex(parameters, candidate => SymbolEqualityComparer.Default.Equals(candidate, parameter))]
                : part.ToString()));
        var definition = target.OriginalDefinition;
        var generic = parameters.Length == 0 ? string.Empty : "<" + string.Join(", ", parameterNames) + ">";
        source.Append($"    private static class {accessorName}{generic}\n");
        for (var index = 0; index < parameters.Length; index++)
        {
            var constraints = GetConstraints(parameters[index], DefinitionName);
            if (constraints.Count > 0)
            {
                source.Append($"        where T{index} : {string.Join(", ", constraints)}\n");
            }
        }

        source.Append("    {\n");
        var constructor = target.MethodKind == MethodKind.Constructor;
        var kind = constructor ? "Constructor" : "Method, Name = " + SymbolDisplay.FormatLiteral(target.MetadataName, true);
        var signature = definition.Parameters.Select(parameter => DefinitionName(parameter.Type) + " @" + parameter.Name).ToList();
        if (!constructor)
        {
            signature.Insert(0, (target.ContainingType.IsValueType ? "ref " : string.Empty) + DefinitionName(definition.ContainingType) + " option");
        }

        source.Append($"        [{Accessor}({AccessorKind}{kind})]\n")
            .Append($"        internal static extern {(constructor ? DefinitionName(definition.ContainingType) : "void")} Invoke({string.Join(", ", signature)});\n    }}\n");
        return accessorName + (arguments.Length == 0 ? string.Empty : "<" + string.Join(", ", arguments.Select(Name)) + ">") + ".Invoke";
    }

    private static List<string> GetConstraints(ITypeParameterSymbol parameter, Func<ISymbol, string> name)
    {
        var constraints = new List<string>();
        if (parameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("unmanaged");
        }
        else if (parameter.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }
        else if (parameter.HasReferenceTypeConstraint)
        {
            constraints.Add("class");
        }
        else if (parameter.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }

        constraints.AddRange(parameter.ConstraintTypes.Select(type => name(type)));
        if (parameter.HasConstructorConstraint)
        {
            constraints.Add("new()");
        }
        return constraints;
    }

    private void AppendSequence(StringBuilder body, ITypeSymbol type, ITypeSymbol element, bool copy)
    {
        var named = type as INamedTypeSymbol;
        var set = named is not null && (FindInterface(named, SetInterface) is not null ||
            FindInterface(named, ReadOnlySetInterface) is not null);
        var readOnlySet = named?.OriginalDefinition.ToDisplayString() == ReadOnlySetInterface;
        var setInterface = named?.OriginalDefinition.ToDisplayString() is SetInterface or ReadOnlySetInterface;
        if (setInterface && element.SpecialType != SpecialType.System_String && element.TypeKind != TypeKind.Enum)
        {
            body.Append(ReturnOption);
            return;
        }

        if (IsByteArray(type))
        {
            body.Append($"        var bytes = section.Value is null ? null : {OptionBindingGenerator.Registry}.Parse(section, static value => global::System.Convert.FromBase64String(value));\n")
                .Append("        if (getInitialValue is not null) option = getInitialValue();\n        if (bytes is not null)\n        {\n")
                .Append("            if (readOnly || (option is { Length: > 0 } && bytes.Length == 0)) return option;\n            hasNewValue = true;\n")
                .Append("            return option is null ? bytes : global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Concat(option, bytes));\n        }\n")
                .Append($"        if (IsNullValue(section) || !{AnyChildren})\n        {{\n            hasNewValue = !readOnly && option is not {{ Length: > 0 }};\n            return readOnly || option is {{ Length: > 0 }} ? option : null;\n        }}\n");
        }

        AppendSequenceBody(body, type, element, copy, set, readOnlySet);
    }

    private void AppendSequenceBody(StringBuilder body, ITypeSymbol type, ITypeSymbol element, bool copy, bool set, bool readOnlySet)
    {
        var captureConversion = copy && GetCustomConverter(element) is not null;
        var elementName = Name(element);
        var named = type as INamedTypeSymbol;
        var concrete = Collections + (set ? "HashSet" : "List") + "<" + elementName + ">";
        var create = named is not null && type.TypeKind != TypeKind.Interface ? Construction(named, methods[type]) : "new " + concrete + "()";
        var empty = copy ? EmptyCopiedSequenceValue(type, elementName) : OptionVariable;
        AppendCollectionLeaf(body, type, create, empty);
        if (copy)
        {
            body.Append("        if (readOnly) return option;\n");
        }
        else
        {
            body.Append("        if (readOnly && option is null) return option;\n");
        }

        if (readOnlySet)
        {
            body.Append($"        hasNewValue = !readOnly;\n        var items = new {concrete}();\n")
                .Append($"        if (option is not null)\n        {{\n            foreach ({elementName} existing in (global::System.Collections.IEnumerable)option) items.Add(existing);\n        }}\n");
        }
        else if (copy)
        {
            AppendCopiedSequence(body);
        }
        else
        {
            var setInterface = type.OriginalDefinition.ToDisplayString() == SetInterface;
            var replacement = setInterface ? WritableCondition : "option is null";
            var collectionInterface = setInterface ? "ISet" : "ICollection";
            body.Append($"        hasNewValue = {replacement};\n        option ??= {create};\n        var items = ({Collections}{collectionInterface}<{elementName}>)option;\n");
        }

        AppendSequenceItems(body, element, copy, readOnlySet, captureConversion);
    }

    private static string EmptyCopiedSequenceValue(ITypeSymbol type, string elementName)
        => type.TypeKind == TypeKind.Interface
            ? "option is null && section.Value == string.Empty ? throw new global::System.ArgumentNullException(\"elementType\") : option"
            : $"!readOnly && option is null && section.Value == string.Empty ? global::System.Array.Empty<{elementName}>() : option";

    private static void AppendCopiedSequence(StringBuilder body)
    {
        body.Append($"        hasNewValue = true;\n        var items = new {Collections}List<object>();\n")
            .Append("        if (option is not null)\n        {\n            foreach (var existing in (global::System.Collections.IEnumerable)option) items.Add(existing);\n        }\n");
    }

    private void AppendSequenceItems(StringBuilder body, ITypeSymbol element, bool copy, bool readOnlySet, bool captureConversion)
    {
        var scalarConversion = ScalarConversionCondition(element, ChildVariable);
        var nonNullableArray = copy && !captureConversion && element.IsValueType && !IsNullable(element) && scalarConversion != FalseLiteral;
        if (nonNullableArray)
        {
            body.Append("        var nullElement = false;\n");
        }

        var rejectNull = nonNullableArray ? $"if (child.Value is null && !global::System.Linq.Enumerable.Any(child.GetChildren()) && ({scalarConversion})) {{ nullElement = true; continue; }}\n                " : string.Empty;
        var capture = captureConversion ? ", captureConvertedValue: item => rawValue = item" : string.Empty;
        var binding = captureConversion ? "object rawValue = null;\n                " : string.Empty;
        binding += $"var value = {Build(element)}(child, default, out var changed, out var itemConvertedNull, parentCollection: true, hasExistingValue: false{ConversionArgument(element)}{capture});\n                ";
        if (nonNullableArray)
        {
            binding += "if (itemConvertedNull) { nullElement = true; continue; }\n                ";
        }
        var item = captureConversion ? "itemConvertedNull ? null : rawValue ?? (object)value" : "value";
        AppendItems(body, element, rejectNull + binding + $"if (changed) items.Add({item});");
        if (nonNullableArray)
        {
            body.Append("        if (nullElement) throw new global::System.InvalidCastException(\"At least one element in the source array could not be cast down to the destination array type.\");\n");
        }

        var result = readOnlySet ? "readOnly ? option : items" : OptionVariable;
        if (copy)
        {
            result = $"CopyToArray<{Name(element)}>(items)";
        }

        body.Append($"        return {result};\n");
    }

    private void AppendDictionary(StringBuilder body, INamedTypeSymbol type, INamedTypeSymbol dictionary)
    {
        var key = dictionary.TypeArguments[0];
        var value = dictionary.TypeArguments[1];
        var keyExpression = DictionaryKey(key);
        var readOnly = type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
        if (keyExpression is null && type.TypeKind == TypeKind.Interface)
        {
            body.Append(ReturnOption);
            return;
        }

        var concrete = $"{Collections}Dictionary<{Name(key)}, {Name(value)}>";
        var create = type.TypeKind == TypeKind.Interface ? "new " + concrete + "()" : Construction(type, methods[type]);
        AppendCollectionLeaf(body, type, create, OptionVariable);
        if (readOnly)
        {
            body.Append($"        if (readOnly && option is null) return option;\n        hasNewValue = !readOnly;\n        var items = new {concrete}();\n")
                .Append($"        if (option is not null)\n        {{\n            foreach ({Collections}KeyValuePair<{Name(key)}, {Name(value)}> entry in (global::System.Collections.IEnumerable)option)\n            {{\n                items.Add(entry.Key, entry.Value);\n            }}\n        }}\n");
        }
        else
        {
            var replacement = type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>" ? WritableCondition : "option is null";
            body.Append($"        if (readOnly && option is null) return option;\n        hasNewValue = {replacement};\n        option ??= {create};\n")
                .Append($"        var items = ({Collections}IDictionary<{Name(key)}, {Name(value)}>)option;\n");
        }

        if (keyExpression is not null)
        {
            AppendDictionaryItems(body, value, keyExpression);
        }
        body.Append(readOnly ? "        return readOnly ? option : items;\n" : ReturnOption);
    }

    private void AppendDictionaryItems(StringBuilder body, ITypeSymbol value, string keyExpression)
    {
        var primitive = IsPrimitiveScalar(value);
        var lookup = "items.TryGetValue(key, out var current)";
        if (value.IsValueType)
        {
            lookup = "existingValue = " + lookup + (IsNullable(value) ? " && current.HasValue" : string.Empty);
        }
        var initial = InitialValue(value, ChildVariable, $"(({lookup}) ? current : default)");
        var existing = value.IsValueType ? "var existingValue = false;\n                " : string.Empty;
        var existingArgument = value.IsValueType ? ", hasExistingValue: existingValue" : string.Empty;
        var changed = primitive ? "changed && !existingValue" : "changed";
        AppendItems(body, value, $"var key = {keyExpression};\n                {existing}var value = {Build(value)}(child, {initial}, out var changed, parentCollection: true{ConversionArgument(value)}{existingArgument});\n                if ({changed}) items[key] = value;");
    }

    private static string? DictionaryKey(ITypeSymbol key)
    {
        if (key.SpecialType == SpecialType.System_String)
        {
            return "child.Key";
        }

        if (key.TypeKind == TypeKind.Enum)
        {
            return $"global::System.Enum.Parse<{Name(key)}>(child.Key, ignoreCase: true)";
        }

        return key.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or
            SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
            ? $"({Name(key)})global::System.Convert.ChangeType(child.Key, typeof({Name(key)}), global::System.Globalization.CultureInfo.CurrentCulture)"
            : null;
    }

    private static void AppendCollectionLeaf(StringBuilder body, ITypeSymbol type, string creation, string empty)
    {
        body.Append($"        if (!{AnyChildren})\n        {{\n            if (parentCollection && option is null && global::System.String.IsNullOrEmpty(section.Value))\n            {{\n");
        if (type.TypeKind is TypeKind.Array or TypeKind.Interface)
        {
            body.Append("                throw new global::System.InvalidOperationException(\"Cannot construct this collection from an empty entry.\");\n");
        }
        else
        {
            body.Append($"                hasNewValue = true;\n                return {creation};\n");
        }

        body.Append($"            }}\n            hasNewValue = !readOnly && option is null && global::System.String.IsNullOrEmpty(section.Value);\n            return {empty};\n        }}\n");
    }

    private void AppendItems(StringBuilder body, ITypeSymbol element, string binding)
    {
        body.Append("        foreach (var child in section.GetChildren())\n        {\n            try\n            {\n");
        AppendConversionProbe(body, element);
        body.Append("                ");
        var scalarConversion = ScalarConversionCondition(element, ChildVariable);
        if (scalarConversion != "true")
        {
            body.Append($"if (!global::System.String.IsNullOrEmpty(child.Value) && !global::System.Linq.Enumerable.Any(child.GetChildren()) && !({scalarConversion})) continue;\n                ");
        }

        body.Append(binding).Append("\n            }\n            catch (global::System.Exception exception) when (exception is not UnsupportedRuntimeTypeException)\n            {\n            }\n        }\n");
    }

    private void AppendConversionProbe(StringBuilder body, ITypeSymbol type, string variable = ConversionVariable)
    {
        if (GetCustomConverter(type) is not null)
        {
            body.Append($"        bool? {variable} = null;\n");
        }
    }

    private string ConversionArgument(ITypeSymbol type, string variable = ConversionVariable)
    {
        return GetCustomConverter(type) is null ? string.Empty : $", conversionProbe: {variable}";
    }

    private string ScalarConversionCondition(ITypeSymbol type, string section, string probe = ConversionVariable)
    {
        var converter = GetCustomConverter(type);
        if (converter is not null)
        {
            var nullValue = IsNullable(type) ? $"global::System.String.IsNullOrEmpty({section}.Value) || " : string.Empty;
            return $"{nullValue}({probe} ??= ((global::System.ComponentModel.TypeConverter){converter}).CanConvertFrom(typeof(string))) == true";
        }

        return CanConvertScalar(type) ? "true" : FalseLiteral;
    }

    private string? GetCustomConverter(ITypeSymbol type)
    {
        var converter = OptionTypeConverter.GetConstruction(type, compilation, out var error);
        if (error is not null)
        {
            Error = error;
        }

        if (converter is null)
        {
            return null;
        }

        if (!converterMethods.TryGetValue(converter, out var method))
        {
            method = $"CreateConverter{optionIndex}_{converterMethods.Count}";
            converterMethods.Add(converter, method);
            source.Append($"    private static global::System.ComponentModel.TypeConverter {method}()\n    {{\n");
            AppendInvocation(source, $"return {converter};");
            source.Append(EndMethod);
        }

        var convertedType = IsNullable(type) ? ((INamedTypeSymbol)type).TypeArguments[0] : type;
        return $"{OptionBindingGenerator.Registry}.GetConverter<{Name(convertedType)}>(static () => {method}())";
    }

    private static bool CanConvertScalar(ITypeSymbol type)
    {
        return OptionBindingGenerator.GetConversion(type) is not null ||
            type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } ||
            Name(type) == Section;
    }

    private static bool IsPrimitiveScalar(ITypeSymbol type)
    {
        if (IsNullable(type)) type = ((INamedTypeSymbol)type).TypeArguments[0];
        return type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char or
            SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double;
    }

    private static INamedTypeSymbol? FindInterface(INamedTypeSymbol type, string name)
    {
        return type.AllInterfaces.Prepend(type).FirstOrDefault(candidate => candidate.OriginalDefinition.ToDisplayString() == name);
    }

    private static IMethodSymbol? GetConstructor(INamedTypeSymbol type)
    {
        var constructors = type.InstanceConstructors.Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public).ToArray();
        return constructors.FirstOrDefault(candidate => candidate.Parameters.Length == 0) ??
            (constructors.Length == 1 ? constructors[0] : null);
    }

    private static bool IsNullable(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    private static string Name(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
