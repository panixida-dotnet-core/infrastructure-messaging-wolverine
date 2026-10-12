using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal sealed class OptionBindingSourceBuilder(Compilation compilation, INamedTypeSymbol? keyNameAttribute, int optionIndex,
    IReadOnlyList<INamedTypeSymbol> runtimeTypes)
{
    private const string Section = "global::Microsoft.Extensions.Configuration.IConfigurationSection";
    private const string Collections = "global::System.Collections.Generic.";
    private const string AnyChildren = "global::System.Linq.Enumerable.Any(section.GetChildren())";
    private const string ReturnOption = "        return option;\n";
    private const string EndMethod = "    }\n";
    private const string FalseLiteral = "false";
    private const string OptionVariable = "option";
    private const string Accessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";
    private const string AccessorKind = "global::System.Runtime.CompilerServices.UnsafeAccessorKind.";
    private readonly Dictionary<ITypeSymbol, string> methods = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, string> runtimeMethods = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, string> converterMethods = new(StringComparer.Ordinal);
    private readonly HashSet<INamedTypeSymbol> inspectedObjectTypes = new(SymbolEqualityComparer.Default);
    private readonly List<INamedTypeSymbol> objectTypes = [];
    private readonly StringBuilder source = new();

    internal string? Error { get; private set; }

    internal string Complete()
    {
        var type = compilation.GetSpecialType(SpecialType.System_Object);
        if (methods.TryGetValue(type, out var method))
        {
            var body = new StringBuilder();
            AppendComplexBody(body, type, method);
            source.Append($"    private static object {method}({Section} section, object option, bool readOnly = false, bool parentCollection = false)\n    {{\n")
                .Append(body).Append(EndMethod);
        }

        return source.ToString();
    }

    internal string Build(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Dynamic)
        {
            type = compilation.GetSpecialType(SpecialType.System_Object);
        }

        type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        if (methods.TryGetValue(type, out var existing))
        {
            return existing;
        }

        var name = $"Bind{optionIndex}_{methods.Count}";
        methods.Add(type, name);
        if (type.SpecialType == SpecialType.System_Object)
        {
            return name;
        }

        var body = new StringBuilder();
        if (type.IsRefLikeType || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
        {
            Error = $"type '{type}' must be accessible to generated code";
        }
        else
        {
            AppendBody(body, type, name);
        }

        source.Append($"    private static {Name(type)} {name}({Section} section, {Name(type)} option, bool readOnly = false, bool parentCollection = false)\n    {{\n")
            .Append(body).Append(EndMethod);
        return name;
    }

    private void AppendBody(StringBuilder body, ITypeSymbol type, string method)
    {
        var converter = GetCustomConverter(type);
        var conversion = converter is null ? OptionBindingGenerator.GetConversion(type) : null;
        if (conversion is not null)
        {
            var nullTest = IsNullable(type) ? "global::System.String.IsNullOrEmpty(section.Value)" : "section.Value is null";
            body.Append($"        if (readOnly) return option;\n        return {nullTest} ? default({Name(type)}) : {OptionBindingGenerator.Registry}.Parse(section, static value => {conversion});\n");
            return;
        }

        if (converter is not null)
        {
            var nullTest = IsNullable(type) ? "global::System.String.IsNullOrEmpty(section.Value)" : "section.Value is null";
            body.Append($"        if (section.Value is not null || !{AnyChildren})\n        {{\n            global::System.ComponentModel.TypeConverter converter = {converter};\n")
                .Append("            if (converter.CanConvertFrom(typeof(string)))\n            {\n                if (readOnly) return option;\n")
                .Append($"                return {nullTest} ? default({Name(type)}) : {OptionBindingGenerator.Registry}.Parse(section, value => ({Name(type)})converter.ConvertFromInvariantString(value)!);\n            }}\n        }}\n");
        }

        AppendComplexBody(body, type, method);
    }

    private void AppendComplexBody(StringBuilder body, ITypeSymbol type, string method)
    {
        if (Name(type) == Section)
        {
            body.Append("        return readOnly ? option : section;\n");
        }
        else if (type.SpecialType == SpecialType.System_Object)
        {
            body.Append($"        if (HasConfigurationValue(section) || !{AnyChildren}) return readOnly ? option : section.Value;\n");
            AppendRuntimeDispatch(body, (INamedTypeSymbol)type);
            body.Append("        return readOnly ? option : option ?? new object();\n");
        }
        else if (IsNullable(type))
        {
            var underlying = ((INamedTypeSymbol)type).TypeArguments[0];
            body.Append($"        if (readOnly) return option;\n        if (section.Value == string.Empty) return default;\n")
                .Append($"        if (!{AnyChildren}) return section.Value is null ? default : option;\n        return {Build(underlying)}(section, option.GetValueOrDefault());\n");
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
        var dictionary = FindInterface(type, "System.Collections.Generic.IDictionary<TKey, TValue>") ??
            (definition == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>" ? type : null);
        if (dictionary is not null)
        {
            AppendDictionary(body, type, dictionary);
            return;
        }

        var copy = definition is "System.Collections.Generic.IEnumerable<T>" or
            "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.IReadOnlyCollection<T>";
        var sequence = FindInterface(type, type.TypeKind == TypeKind.Interface
            ? "System.Collections.Generic.IEnumerable<T>" : "System.Collections.Generic.ICollection<T>");
        if (sequence is not null)
        {
            AppendSequence(body, type, sequence.TypeArguments[0], copy);
            return;
        }

        var creation = Construction(type, method);
        body.Append($"        if (!{AnyChildren})\n        {{\n")
            .Append($"            if (parentCollection && !readOnly && {(type.IsReferenceType ? "option is null && " : string.Empty)}global::System.String.IsNullOrEmpty(section.Value)) return {creation};\n")
            .Append("            return option;\n        }\n");
        AppendRuntimeDispatch(body, type);
        var constructor = GetConstructor(type);
        var parameters = constructor?.Parameters.Select(parameter => parameter.Name).ToArray() ?? [];
        if (type.IsReferenceType)
        {
            if (parameters.Length > 0)
            {
                body.Append("        var created = option is null;\n");
            }

            body.Append("        if (option is null)\n        {\n            if (readOnly) return option;\n")
                .Append("            option = ").Append(creation).Append(";\n        }\n");
        }

        AppendProperties(body, type, method, parameters);
    }

    private void AppendRuntimeDispatch(StringBuilder body, INamedTypeSymbol type)
    {
        if (type.IsSealed || type.IsValueType)
        {
            return;
        }

        body.Append("        if (option is not null)\n        {\n            var runtimeType = ((object)option).GetType();\n");
        var implementations = type.SpecialType == SpecialType.System_Object ? GetObjectImplementations() : runtimeTypes;
        foreach (var implementation in implementations.Where(candidate => candidate.TypeKind == TypeKind.Class && !candidate.IsAbstract &&
                     !candidate.IsFileLocal && compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly) &&
                     compilation.ClassifyCommonConversion(candidate, type) is { IsImplicit: true, IsReference: true }))
        {
            body.Append($"            if (runtimeType == typeof({Name(implementation)})) return {BuildRuntimeProperties(implementation)}(section, ({Name(implementation)})option);\n");
        }

        body.Append($"            if (runtimeType != typeof({Name(type)}))\n")
            .Append("                throw new UnsupportedRuntimeTypeException(runtimeType, section.Path);\n        }\n");
    }

    private string BuildRuntimeProperties(INamedTypeSymbol type)
    {
        if (runtimeMethods.TryGetValue(type, out var existing))
        {
            return existing;
        }

        var method = $"Bind{optionIndex}Runtime{runtimeMethods.Count}";
        runtimeMethods.Add(type, method);
        var body = new StringBuilder();
        AppendProperties(body, type, method, []);
        source.Append($"    private static {Name(type)} {method}({Section} section, {Name(type)} option)\n    {{\n")
            .Append(body).Append(EndMethod);
        return method;
    }

    private IEnumerable<INamedTypeSymbol> GetObjectImplementations()
    {
        for (var index = 0; index < objectTypes.Count; index++)
        {
            yield return objectTypes[index];
        }
    }

    private void DiscoverObjectTypes(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!inspectedObjectTypes.Add(current))
            {
                continue;
            }

            foreach (var candidate in OptionBindingGenerator.GetObjectTypes(compilation, current).Prepend(current)
                         .Where(candidate => !objectTypes.Any(existing => SymbolEqualityComparer.Default.Equals(existing, candidate))))
            {
                objectTypes.Add(candidate);
            }
        }
    }

    private void AppendProperties(StringBuilder body, INamedTypeSymbol type, string method, string[] parameters)
    {
        DiscoverObjectTypes(type);
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
        if (!writable && (OptionBindingGenerator.GetConversion(property.Type) is not null || property.Type.IsValueType))
        {
            return;
        }

        AppendPropertySection(body, property, index);
        var getter = AppendGetter(property, method + "Get" + index) + "(option)";
        var initial = InitialValue(property.Type, "child" + index, getter);
        var value = "value" + index;
        body.Append($"        {Name(property.Type)} {value} = {(constructorBound ? "!created && " : string.Empty)}hasChild{index}\n")
            .Append($"            ? {Build(property.Type)}(child{index}, {initial}, {(writable ? FalseLiteral : "true")})\n")
            .Append($"            : {(writable ? getter : "default")};\n");
        if (!writable)
        {
            return;
        }

        if (property.Type.IsReferenceType || IsNullable(property.Type))
        {
            body.Append($"        if ({(constructorBound ? "created || " : string.Empty)}{value} is not null || hasChild{index})\n");
        }

        body.Append("        {\n");
        AppendSetter(body, type, property, method + "Set" + index, value);
        body.Append("        }\n");
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
            var receiver = SymbolEqualityComparer.Default.Equals(type, property.ContainingType)
                ? OptionVariable : $"(({Name(property.ContainingType)})option)";
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
        var lookup = $"children.TryGetValue({literal}, out var child{index})";
        var separator = key.LastIndexOf(':');
        if (Name(property.Type) == Section)
        {
            body.Append($"        var child{index} = section.GetSection({literal});\n");
            lookup = "true";
        }
        else if (separator >= 0)
        {
            var parent = SymbolDisplay.FormatLiteral(key.Substring(0, separator), true);
            var childKey = SymbolDisplay.FormatLiteral(key.Substring(separator + 1), true);
            body.Append($"        var child{index} = global::System.Linq.Enumerable.FirstOrDefault(section.GetSection({parent}).GetChildren(), child => global::System.String.Equals(child.Key, {childKey}, global::System.StringComparison.OrdinalIgnoreCase));\n");
            lookup = $"child{index} is not null";
        }
        body.Append($"        var hasChild{index} = {lookup};\n");
    }

    private string AppendGetter(IPropertySymbol property, string method)
    {
        var body = new StringBuilder();
        AppendInvocation(body, $"return option.@{property.Name};");
        source.Append($"    private static {Name(property.Type)} {method}({Name(property.ContainingType)} option)\n    {{\n")
            .Append(body).Append(EndMethod);
        return method;
    }

    private string InitialValue(ITypeSymbol type, string child, string getter)
    {
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            return getter;
        }
        if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic)
        {
            return $"HasConfigurationValue({child}) || !global::System.Linq.Enumerable.Any({child}.GetChildren()) ? default : {getter}";
        }

        var conversion = ScalarConversionCondition(type);
        if (conversion == "true")
        {
            return "default";
        }
        if (conversion != FalseLiteral)
        {
            return $"({child}.Value is not null || !global::System.Linq.Enumerable.Any({child}.GetChildren())) && ({conversion}) ? default : {getter}";
        }
        if (IsNullable(type))
        {
            return $"{child}.Value == string.Empty || ({child}.Value is null && !global::System.Linq.Enumerable.Any({child}.GetChildren())) ? default : {getter}";
        }
        return getter;
    }

    private string Construction(INamedTypeSymbol type, string method)
    {
        if (type.IsAbstract || type.TypeKind == TypeKind.Interface)
        {
            return InvalidConstruction(type, method, $"Cannot create an instance of type '{type}'.");
        }

        var constructor = GetConstructor(type);
        if (constructor is null)
        {
            return InvalidConstruction(type, method, $"Type '{type}' needs a public parameterless constructor or a single public constructor with matching properties.");
        }

        if (type.IsValueType && constructor.IsImplicitlyDeclared)
        {
            return "default(" + Name(type) + ")";
        }

        var properties = OptionBindingGenerator.GetProperties(type).ToArray();
        var arguments = new List<string>();
        var signature = new List<string>();
        foreach (var parameter in constructor.Parameters)
        {
            var property = properties.FirstOrDefault(candidate => string.Equals(candidate.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (property is null || parameter.RefKind != RefKind.None)
            {
                return InvalidConstruction(type, method, $"Constructor parameter '{parameter.Name}' on '{type}' needs a matching property.");
            }

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
        var conversion = ScalarConversionCondition(parameterType);
        if (conversion != "true")
        {
            var nullableEmpty = IsNullable(parameterType) ? " && section.Value != string.Empty" : string.Empty;
            body.Append($"        if (section.Value is not null{nullableEmpty} && !{AnyChildren} && !({conversion}))\n")
                .Append($"            throw new global::System.ArgumentException($\"Object of type 'System.String' cannot be converted to type '{{typeof({Name(parameterType)})}}'.\");\n");
        }

        body.Append($"        var value = {Build(parameterType)}(section, default);\n");
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
            return $"throw new global::System.InvalidOperationException(\"Missing configuration for constructor parameter '{parameter.Name}' on '{type}'.\")";
        }

        if (parameter.ExplicitDefaultValue is null)
        {
            return "default";
        }

        return "(" + Name(parameter.Type) + ")" + SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false);
    }

    private string InvalidConstruction(INamedTypeSymbol type, string method, string message)
    {
        source.Append($"    private static {Name(type)} {method}CannotCreate() => throw new global::System.InvalidOperationException({SymbolDisplay.FormatLiteral(message, true)});\n");
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
        var set = named is not null && (FindInterface(named, "System.Collections.Generic.ISet<T>") is not null ||
            FindInterface(named, "System.Collections.Generic.IReadOnlySet<T>") is not null);
        var readOnlySet = named?.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlySet<T>";
        var setInterface = named?.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.ISet<T>" or "System.Collections.Generic.IReadOnlySet<T>";
        if (setInterface && element.SpecialType != SpecialType.System_String && element.TypeKind != TypeKind.Enum)
        {
            body.Append(ReturnOption);
            return;
        }

        var collection = named is not null && FindInterface(named, "System.Collections.Generic.ICollection<T>") is not null;
        if (!copy && !set && !collection)
        {
            Error = $"collection '{type}' needs an ICollection<T> implementation";
            return;
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            body.Append($"        if (section.Value is null && !{AnyChildren}) return readOnly || option is {{ Length: > 0 }} ? option : null;\n")
                .Append($"        if (section.Value is not null && !readOnly)\n        {{\n            var bytes = {OptionBindingGenerator.Registry}.Parse(section, static value => global::System.Convert.FromBase64String(value));\n")
                .Append("            return option is null ? bytes : global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Concat(option, bytes));\n        }\n");
        }

        AppendSequenceBody(body, type, element, copy, set, readOnlySet);
    }

    private void AppendSequenceBody(StringBuilder body, ITypeSymbol type, ITypeSymbol element, bool copy, bool set, bool readOnlySet)
    {
        var elementName = Name(element);
        var named = type as INamedTypeSymbol;
        var concrete = Collections + (set ? "HashSet" : "List") + "<" + elementName + ">";
        var create = named is not null && type.TypeKind != TypeKind.Interface ? Construction(named, methods[type]) : "new " + concrete + "()";
        var empty = copy ? $"!readOnly && option is null && section.Value == string.Empty ? global::System.Array.Empty<{elementName}>() : option" : OptionVariable;
        AppendCollectionLeaf(body, type, create, empty);
        if (copy || readOnlySet)
        {
            body.Append("        if (readOnly) return option;\n");
        }
        else
        {
            body.Append("        if (readOnly && option is null) return option;\n");
        }

        if (copy || readOnlySet)
        {
            body.Append($"        var items = option is null ? new {concrete}() : new {concrete}(option);\n");
        }
        else
        {
            body.Append($"        option ??= {create};\n        var items = ({Collections}ICollection<{elementName}>)option;\n");
        }

        AppendSequenceItems(body, element, copy, readOnlySet);
    }

    private void AppendSequenceItems(StringBuilder body, ITypeSymbol element, bool copy, bool readOnlySet)
    {
        var scalarConversion = ScalarConversionCondition(element);
        var nonNullableArray = copy && element.IsValueType && !IsNullable(element) && scalarConversion != FalseLiteral;
        if (nonNullableArray)
        {
            body.Append("        var nullElement = false;\n");
        }

        var rejectNull = nonNullableArray ? $"if ({scalarConversion} && child.Value is null && !global::System.Linq.Enumerable.Any(child.GetChildren())) {{ nullElement = true; continue; }}\n                " : string.Empty;
        AppendItems(body, element, rejectNull + $"items.Add({Build(element)}(child, default, parentCollection: true));");
        if (nonNullableArray)
        {
            body.Append("        if (nullElement) throw new global::System.InvalidCastException(\"At least one element in the source array could not be cast down to the destination array type.\");\n");
        }

        var result = readOnlySet ? "items" : OptionVariable;
        if (copy)
        {
            result = "items.ToArray()";
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
            body.Append($"        if (readOnly) return option;\n        var items = option is null ? new {concrete}() : new {concrete}(option);\n");
        }
        else
        {
            body.Append($"        if (readOnly && option is null) return option;\n        option ??= {create};\n")
                .Append($"        var items = ({Collections}IDictionary<{Name(key)}, {Name(value)}>)option;\n");
        }

        if (keyExpression is not null)
        {
            var initial = InitialValue(value, "child", "(items.TryGetValue(key, out var current) ? current : default)");
            AppendItems(body, value, $"var key = {keyExpression};\n                items[key] = {Build(value)}(child, {initial}, parentCollection: true);");
        }
        body.Append(readOnly ? "        return items;\n" : ReturnOption);
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
            body.Append($"                return {creation};\n");
        }

        body.Append($"            }}\n            return {empty};\n        }}\n");
    }

    private void AppendItems(StringBuilder body, ITypeSymbol element, string binding)
    {
        body.Append("        foreach (var child in section.GetChildren())\n        {\n            try\n            {\n                ");
        var scalarConversion = ScalarConversionCondition(element);
        if (scalarConversion != "true")
        {
            body.Append($"if (!({scalarConversion}) && !global::System.String.IsNullOrEmpty(child.Value) && !global::System.Linq.Enumerable.Any(child.GetChildren())) continue;\n                ");
        }

        body.Append(binding).Append("\n            }\n            catch (global::System.Exception exception) when (exception is not UnsupportedRuntimeTypeException)\n            {\n            }\n        }\n");
    }

    private string ScalarConversionCondition(ITypeSymbol type)
    {
        var converter = GetCustomConverter(type);
        if (converter is not null)
        {
            return $"((global::System.ComponentModel.TypeConverter){converter}).CanConvertFrom(typeof(string))";
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
        return OptionBindingGenerator.GetConversion(type) is not null || type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic ||
            type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } ||
            Name(type) == Section;
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
        return type.TypeKind == TypeKind.Dynamic ? "object" : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
