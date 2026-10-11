using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generators.Options;

internal sealed class OptionBindingSourceBuilder(Compilation compilation, INamedTypeSymbol? keyNameAttribute, int optionIndex)
{
    private const string Section = "global::Microsoft.Extensions.Configuration.IConfigurationSection";
    private const string Collections = "global::System.Collections.Generic.";
    private const string AnyChildren = "global::System.Linq.Enumerable.Any(section.GetChildren())";
    private const string Accessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";
    private const string AccessorKind = "global::System.Runtime.CompilerServices.UnsafeAccessorKind.";
    private readonly Dictionary<ITypeSymbol, string> methods = new(SymbolEqualityComparer.Default);
    private readonly StringBuilder source = new();

    internal string? Error { get; private set; }
    internal string Source => source.ToString();

    internal string Build(ITypeSymbol type)
    {
        type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        if (methods.TryGetValue(type, out var existing))
        {
            return existing;
        }

        var name = $"Bind{optionIndex}_{methods.Count}";
        methods.Add(type, name); // Reserve before visiting members: recursive models share a binder.
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
            .Append(body).Append("    }\n");
        return name;
    }

    private void AppendBody(StringBuilder body, ITypeSymbol type, string method)
    {
        var conversion = OptionBindingGenerator.GetConversion(type);
        if (conversion is not null)
        {
            var nullTest = IsNullable(type) ? "global::System.String.IsNullOrEmpty(section.Value)" : "section.Value is null";
            body.Append($"        if (readOnly) return option;\n        return {nullTest} ? default({Name(type)}) : {OptionBindingGenerator.Registry}.Parse(section, static value => {conversion});\n");
        }
        else if (type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfigurationSection")
        {
            body.Append("        return readOnly ? option : section;\n");
        }
        else if (type.SpecialType == SpecialType.System_Object)
        {
            body.Append($"        return readOnly ? option : section.Value ?? ({AnyChildren} ? option ?? new object() : null);\n");
        }
        else if (IsNullable(type))
        {
            var underlying = ((INamedTypeSymbol)type).TypeArguments[0];
            body.Append($"        if (readOnly) return option;\n        if (!{AnyChildren}) return default;\n        return {Build(underlying)}(section, option.GetValueOrDefault());\n");
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
    }

    private void AppendNamedBody(StringBuilder body, INamedTypeSymbol type, string method)
    {
        var dictionary = FindInterface(type, "System.Collections.Generic.IDictionary<TKey, TValue>") ??
            FindInterface(type, "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>");
        if (dictionary is not null)
        {
            AppendDictionary(body, type, dictionary);
            return;
        }

        var sequence = FindInterface(type, "System.Collections.Generic.IEnumerable<T>");
        if (sequence is not null)
        {
            var copy = type.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.IEnumerable<T>" or
                "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.IReadOnlyCollection<T>";
            AppendSequence(body, type, sequence.TypeArguments[0], copy);
            return;
        }

        var creation = type.IsReferenceType ? Construction(type, method) : "new " + Name(type) + "()";
        body.Append($"        if (!{AnyChildren})\n        {{\n")
            .Append($"            if (parentCollection && !readOnly && {(type.IsReferenceType ? "option is null && " : string.Empty)}global::System.String.IsNullOrEmpty(section.Value)) return {creation};\n")
            .Append("            return option;\n        }\n");
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

        body.Append($"        var children = new {Collections}Dictionary<string, {Section}>(global::System.StringComparer.OrdinalIgnoreCase);\n")
            .Append("        foreach (var child in section.GetChildren()) children[child.Key] = child;\n");
        var index = 0;
        foreach (var property in OptionBindingGenerator.GetProperties(type))
        {
            AppendProperty(body, property, method, index++, type.IsReferenceType && parameters.Contains(property.Name));
        }

        body.Append("        return option;\n");
    }

    private void AppendProperty(StringBuilder body, IPropertySymbol property, string method, int index, bool constructorBound)
    {
        var writable = property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
        if (!writable && (OptionBindingGenerator.GetConversion(property.Type) is not null || property.Type.IsValueType))
        {
            return;
        }

        var key = property.GetAttributes().FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, keyNameAttribute))
            ?.ConstructorArguments.FirstOrDefault().Value as string ?? property.Name;
        if (string.IsNullOrWhiteSpace(key)) key = property.Name;
        var literal = SymbolDisplay.FormatLiteral(key, true);
        var value = "value" + index;
        body.Append($"        var {value} = option.@{property.Name};\n")
            .Append($"        if ({(constructorBound ? "!created && " : string.Empty)}children.TryGetValue({literal}, out var child{index}))\n        {{\n")
            .Append($"            {value} = {Build(property.Type)}(child{index}, {value}, {(writable ? "false" : "true")});\n        }}\n");
        if (!writable)
        {
            return;
        }

        if (property.Type.IsReferenceType || IsNullable(property.Type))
        {
            body.Append($"        if ({value} is not null || children.ContainsKey({literal}))\n");
        }

        body.Append("        {\n");
        if (property.SetMethod!.IsInitOnly)
        {
            var reference = property.ContainingType.IsValueType ? "ref " : string.Empty;
            var setter = AppendAccessor(property.SetMethod, method + "Set" + index);
            body.Append($"            {setter}({reference}option, {value});\n");
        }
        else
        {
            body.Append($"            option.@{property.Name} = {value};\n");
        }

        body.Append("        }\n");
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

        var properties = OptionBindingGenerator.GetProperties(type).ToArray();
        var arguments = new List<string>();
        foreach (var parameter in constructor.Parameters)
        {
            var property = properties.FirstOrDefault(candidate => string.Equals(candidate.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (property is null || parameter.RefKind != RefKind.None)
            {
                return InvalidConstruction(type, method, $"Constructor parameter '{parameter.Name}' on '{type}' needs a matching property.");
            }

            var section = "section.GetSection(" + SymbolDisplay.FormatLiteral(parameter.Name, true) + ")";
            var missing = parameter.HasExplicitDefaultValue
                ? parameter.ExplicitDefaultValue is null ? "default" : SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false)
                : $"throw new global::System.InvalidOperationException(\"Missing configuration for constructor parameter '{parameter.Name}' on '{type}'.\")";
            arguments.Add($"(global::Microsoft.Extensions.Configuration.ConfigurationExtensions.Exists({section}) ? {Build(parameter.Type)}({section}, default) : {missing})");
        }

        if (properties.Any(property => property.IsRequired))
        {
            return AppendAccessor(constructor, method + "Create") + "(" + string.Join(", ", arguments) + ")";
        }

        return "new " + Name(type) + "(" + string.Join(", ", arguments) + ")";
    }

    private string InvalidConstruction(INamedTypeSymbol type, string method, string message)
    {
        source.Append($"    private static {Name(type)} {method}CannotCreate() => throw new global::System.InvalidOperationException({SymbolDisplay.FormatLiteral(message, true)});\n");
        return method + "CannotCreate()";
    }

    private string AppendAccessor(IMethodSymbol target, string accessorName)
    {
        // UnsafeAccessor signatures must retain the declaring type's generic parameter positions.
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
        if (parameter.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
        else if (parameter.HasValueTypeConstraint) constraints.Add("struct");
        else if (parameter.HasReferenceTypeConstraint) constraints.Add("class");
        else if (parameter.HasNotNullConstraint) constraints.Add("notnull");
        constraints.AddRange(parameter.ConstraintTypes.Select(type => name(type)));
        if (parameter.HasConstructorConstraint) constraints.Add("new()");
        return constraints;
    }

    private void AppendSequence(StringBuilder body, ITypeSymbol type, ITypeSymbol element, bool copy)
    {
        var named = type as INamedTypeSymbol;
        var set = named is not null && (FindInterface(named, "System.Collections.Generic.ISet<T>") is not null ||
            FindInterface(named, "System.Collections.Generic.IReadOnlySet<T>") is not null);
        var readOnlySet = named?.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlySet<T>";
        var collection = named is not null && FindInterface(named, "System.Collections.Generic.ICollection<T>") is not null;
        if (!copy && !set && !collection)
        {
            Error = $"collection '{type}' needs an ICollection<T> implementation";
            return;
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            body.Append($"        if (section.Value is not null && !readOnly)\n        {{\n            var bytes = {OptionBindingGenerator.Registry}.Parse(section, static value => global::System.Convert.FromBase64String(value));\n")
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
        var empty = copy ? $"!readOnly && option is null && section.Value == string.Empty ? global::System.Array.Empty<{elementName}>() : option" : "option";
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

        var nonNullableArray = copy && element.IsValueType && !IsNullable(element) && CanConvertScalar(element);
        if (nonNullableArray) body.Append("        var nullElement = false;\n");
        var rejectNull = nonNullableArray ? "if (child.Value is null && !global::System.Linq.Enumerable.Any(child.GetChildren())) { nullElement = true; continue; }\n                " : string.Empty;
        AppendItems(body, element, rejectNull + $"items.Add({Build(element)}(child, default, parentCollection: true));");
        if (nonNullableArray) body.Append("        if (nullElement) throw new global::System.InvalidCastException(\"At least one element in the source array could not be cast down to the destination array type.\");\n");
        body.Append(copy ? "        return items.ToArray();\n" : readOnlySet ? "        return items;\n" : "        return option;\n");
    }

    private void AppendDictionary(StringBuilder body, INamedTypeSymbol type, INamedTypeSymbol dictionary)
    {
        var key = dictionary.TypeArguments[0];
        var value = dictionary.TypeArguments[1];
        var keyExpression = key.SpecialType == SpecialType.System_String ? "child.Key" : key.TypeKind == TypeKind.Enum
            ? $"global::System.Enum.Parse<{Name(key)}>(child.Key, ignoreCase: true)"
            : $"({Name(key)})global::System.Convert.ChangeType(child.Key, typeof({Name(key)}), global::System.Globalization.CultureInfo.InvariantCulture)";
        var readOnly = type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
        var concrete = $"{Collections}Dictionary<{Name(key)}, {Name(value)}>";
        var create = type.TypeKind == TypeKind.Interface ? "new " + concrete + "()" : Construction(type, methods[type]);
        AppendCollectionLeaf(body, type, create, "option");
        if (readOnly)
        {
            body.Append($"        if (readOnly) return option;\n        var items = option is null ? new {concrete}() : new {concrete}(option);\n");
        }
        else
        {
            body.Append($"        if (readOnly && option is null) return option;\n        option ??= {create};\n")
                .Append($"        var items = ({Collections}IDictionary<{Name(key)}, {Name(value)}>)option;\n");
        }

        AppendItems(body, value, $"var key = {keyExpression};\n                items.TryGetValue(key, out var current);\n                items[key] = {Build(value)}(child, current, parentCollection: true);");
        body.Append(readOnly ? "        return items;\n" : "        return option;\n");
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

    private static void AppendItems(StringBuilder body, ITypeSymbol element, string binding)
    {
        body.Append("        foreach (var child in section.GetChildren())\n        {\n            try\n            {\n                ")
            .Append(CanConvertScalar(element) ? string.Empty : "if (!global::System.String.IsNullOrEmpty(child.Value) && !global::System.Linq.Enumerable.Any(child.GetChildren())) continue;\n                ")
            .Append(binding).Append("\n            }\n            catch (global::System.Exception)\n            {\n                // ConfigurationBinder skips invalid collection entries by default.\n            }\n        }\n");
    }

    private static bool CanConvertScalar(ITypeSymbol type)
    {
        return OptionBindingGenerator.GetConversion(type) is not null || type.SpecialType == SpecialType.System_Object ||
            type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } ||
            type.ToDisplayString() == "Microsoft.Extensions.Configuration.IConfigurationSection";
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
