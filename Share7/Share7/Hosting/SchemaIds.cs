namespace Share7.API.Hosting;

/// <summary>
/// The names Swagger gives request and response shapes. By default a shape is named after its
/// class alone, and two classes with one name fail the whole document with a 500: the Studio's
/// inbox and the organisations area both declare a <c>CreateAssignmentRequest</c>.
/// <para>
/// A name only one Share7 type carries is kept as it is, so the document reads the same as before.
/// A name several carry is prefixed with its feature, the namespace segment after the project
/// (<c>WorkspaceCreateAssignmentRequest</c>, <c>OrganizationsCreateAssignmentRequest</c>). The rule
/// looks at every type rather than at which one Swagger met first, so the names don't depend on the
/// order the controllers are read in.
/// </para>
/// </summary>
internal static class SchemaIds
{
    private static readonly Lazy<HashSet<string>> SharedNames = new(() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("Share7", StringComparison.Ordinal) == true)
            .SelectMany(PublicTypes)
            .GroupBy(BaseName)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal));

    public static string For(Type type)
    {
        if (type.IsGenericType)
        {
            // As Swashbuckle's own default does: PagedResult<Foo> is "FooPagedResult".
            return string.Concat(type.GetGenericArguments().Select(For)) + BaseName(type);
        }

        var name = BaseName(type);
        if (type.Namespace?.StartsWith("Share7", StringComparison.Ordinal) != true || !SharedNames.Value.Contains(name))
        {
            return name;
        }

        var outer = type.DeclaringType is { } declaring ? BaseName(declaring) : string.Empty;
        return Feature(type) + outer + name;
    }

    private static string BaseName(Type type)
    {
        var tick = type.Name.IndexOf('`');
        return tick < 0 ? type.Name : type.Name[..tick];
    }

    // Share7.Application.Workspace.Models → Workspace; Share7.API.Controllers → Controllers.
    private static string Feature(Type type)
    {
        var parts = (type.Namespace ?? string.Empty).Split('.');
        return parts.Length > 2 ? parts[2] : parts[^1];
    }

    private static IEnumerable<Type> PublicTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
