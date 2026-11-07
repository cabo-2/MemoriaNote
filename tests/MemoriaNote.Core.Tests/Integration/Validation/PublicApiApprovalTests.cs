using System.Globalization;
using System.Reflection;
using System.Text;
using MemoriaNote.Application;
using MemoriaNote.Integration.Validation;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Integration.Validation;

/// <summary>Approves the ECLI-1125 public surface and rejects storage-provider leakage.</summary>
[TestFixture]
public sealed class PublicApiApprovalTests
{
    private static readonly HashSet<string> ApplicationTypeNames = new(StringComparer.Ordinal)
    {
        "INotebookMetadataApplication",
        "INotebookPageApplication",
        "NotebookApplication",
        "NotebookApplicationSession",
        "NotebookCreateRequest",
        "NotebookDescriptor",
        "NotebookFailure",
        "NotebookFailureCategory",
        "NotebookMetadataChangeSet",
        "NotebookMetadataSnapshot",
        "NotebookOpenRequest",
        "NotebookOperationResult`1",
        "NotebookBodySearchBatch",
        "NotebookBodySearchHit",
        "NotebookBodySearchMatch",
        "NotebookBodySearchRequest",
        "NotebookBodySearchSession",
        "NotebookBodySearchSnippet",
        "NotebookBodySearchSort",
        "NotebookPage",
        "NotebookPageCreateRequest",
        "NotebookPageQueryRequest",
        "NotebookPageQuerySort",
        "NotebookPageReplaceRequest",
        "NotebookPageSummary"
    };

    /// <summary>Verifies the checked-in text baseline matches every intended public member.</summary>
    [Test]
    public void IntegrationSurface_MatchesApprovedBaseline()
    {
        var actual = CreateSnapshot();
        var repositoryRoot = CoreIntegrationContractTests.FindRepositoryRoot();
        var expectedPath = Path.Combine(
            repositoryRoot,
            "tests",
            "MemoriaNote.Core.Tests",
            "ApiApproval",
            "ecli-1125-public-api.txt");
        var expected = File.ReadAllText(expectedPath).ReplaceLineEndings("\n").TrimEnd();
        var actualPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "ecli-1125-public-api.actual.txt");
        File.WriteAllText(actualPath, actual + Environment.NewLine);

        Assert.That(
            actual,
            Is.EqualTo(expected),
            $"The actual API snapshot was written to {actualPath}.");
    }

    /// <summary>Verifies no public signature exposes storage, CLI, or SchemaLab implementation types.</summary>
    [Test]
    public void IntegrationSurface_DoesNotExposeForbiddenTypes()
    {
        var forbidden = new[]
        {
            "Microsoft.Data.Sqlite",
            "Microsoft.EntityFrameworkCore",
            "MemoriaNote.Cli",
            "MemoriaNote.Persistence",
            "MemoriaNote.SchemaLab"
        };
        var snapshot = CreateSnapshot();

        Assert.That(
            forbidden.Where(snapshot.Contains),
            Is.Empty);
    }

    private static string CreateSnapshot()
    {
        var builder = new StringBuilder();
        foreach (var type in SurfaceTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            builder.AppendLine(TypeHeader(type));
            foreach (var member in MemberSignatures(type).OrderBy(value => value, StringComparer.Ordinal))
                builder.Append("  ").AppendLine(member);
            builder.AppendLine();
        }

        return builder.ToString().ReplaceLineEndings("\n").TrimEnd();
    }

    private static IEnumerable<Type> SurfaceTypes()
    {
        return typeof(CoreIntegrationContract).Assembly
            .GetExportedTypes()
            .Where(type =>
                type.Namespace == "MemoriaNote.Integration.Validation" ||
                type.Namespace == "MemoriaNote.Application" &&
                ApplicationTypeNames.Contains(type.Name));
    }

    private static string TypeHeader(Type type)
    {
        if (type.IsEnum)
            return $"public enum {FormatType(type)}";
        if (type.IsInterface)
            return $"public interface {FormatType(type)}";
        if (type.IsAbstract && type.IsSealed)
            return $"public static class {FormatType(type)}";
        return $"public sealed class {FormatType(type)}";
    }

    private static IEnumerable<string> MemberSignatures(Type type)
    {
        const BindingFlags flags = BindingFlags.Public |
                                   BindingFlags.Instance |
                                   BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;
        if (type.IsEnum)
        {
            return Enum.GetNames(type).Select(name =>
                $"value {name} = {Convert.ToInt64(Enum.Parse(type, name), CultureInfo.InvariantCulture)}");
        }

        var constructors = type.GetConstructors(flags).Select(constructor =>
            $"constructor {FormatParameters(constructor.GetParameters())}");
        var fields = type.GetFields(flags).Select(field =>
            $"field {FormatType(field.FieldType)} {field.Name}" +
            (field.IsLiteral
                ? $" = {FormatValue(field.GetRawConstantValue())}"
                : string.Empty));
        var properties = type.GetProperties(flags).Select(property =>
            $"property {FormatType(property.PropertyType)} {property.Name} " +
            $"{{ {(property.GetMethod is null ? string.Empty : "get; ")}" +
            $"{(property.SetMethod is null ? string.Empty : "set; ")}}}");
        var methods = type.GetMethods(flags)
            .Where(method => !method.IsSpecialName)
            .Select(method =>
                $"method {(method.IsStatic ? "static " : string.Empty)}" +
                $"{FormatType(method.ReturnType)} {method.Name}" +
                FormatGenericParameters(method) +
                FormatParameters(method.GetParameters()));
        return constructors.Concat(fields).Concat(properties).Concat(methods);
    }

    private static string FormatGenericParameters(MethodInfo method)
    {
        var arguments = method.GetGenericArguments();
        return arguments.Length == 0
            ? string.Empty
            : $"<{string.Join(", ", arguments.Select(argument => argument.Name))}>";
    }

    private static string FormatParameters(IReadOnlyList<ParameterInfo> parameters)
    {
        return "(" + string.Join(", ", parameters.Select(parameter =>
        {
            var modifier = parameter.IsOut
                ? "out "
                : parameter.ParameterType.IsByRef
                    ? "ref "
                    : string.Empty;
            var type = parameter.ParameterType.IsByRef
                ? parameter.ParameterType.GetElementType()!
                : parameter.ParameterType;
            var defaultValue = parameter.HasDefaultValue
                ? $" = {FormatValue(parameter.DefaultValue)}"
                : string.Empty;
            return $"{modifier}{FormatType(type)} {parameter.Name}{defaultValue}";
        })) + ")";
    }

    private static string FormatType(Type type)
    {
        if (type.IsGenericParameter)
            return type.Name;
        if (type.IsArray)
            return FormatType(type.GetElementType()!) + "[]";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return FormatType(type.GetGenericArguments()[0]) + "?";

        var aliases = new Dictionary<Type, string>
        {
            [typeof(void)] = "void",
            [typeof(bool)] = "bool",
            [typeof(int)] = "int",
            [typeof(long)] = "long",
            [typeof(string)] = "string"
        };
        if (aliases.TryGetValue(type, out var alias))
            return alias;

        if (type.IsGenericType)
        {
            var definitionName = type.GetGenericTypeDefinition().FullName!;
            definitionName = definitionName[..definitionName.IndexOf('`')];
            return definitionName + "<" +
                   string.Join(", ", type.GetGenericArguments().Select(FormatType)) + ">";
        }

        return type.FullName ?? type.Name;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null or DBNull => "default",
            string text => $"\"{text}\"",
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default"
        };
    }
}
