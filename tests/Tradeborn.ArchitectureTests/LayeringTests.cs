using System.Reflection;
using Tradeborn.Domain.Common;

namespace Tradeborn.ArchitectureTests;

/// <summary>
/// Enforces the boundaries that make a modular monolith stay modular (ADR-002).
/// </summary>
/// <remarks>
/// These run in seconds and prevent the slow decay that turns a monolith into a big ball of
/// mud. Written with plain reflection rather than an architecture-testing package: the rules
/// are few and specific, and one fewer dependency in the test stack is worth more than the
/// fluent syntax.
/// </remarks>
public class LayeringTests
{
    private static readonly Assembly Domain = typeof(Money).Assembly;
    private static readonly Assembly Application = typeof(Application.Cities.GetCityHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;

    /// <summary>
    /// The rule that carries the most weight: the economy model must be testable with no I/O,
    /// no framework, and no mocking. Every dependency added here erodes that.
    /// </summary>
    [Fact]
    public void Domain_has_no_external_dependencies()
    {
        var offenders = Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !IsFrameworkAssembly(name))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Tradeborn.Domain must depend only on the BCL, but references: {string.Join(", ", offenders)}. " +
            "See docs/adr/ADR-002-modular-monolith.md.");
    }

    [Fact]
    public void Domain_does_not_reference_other_Tradeborn_projects()
    {
        var offenders = Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Tradeborn.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Application_does_not_reference_Infrastructure_or_Web()
    {
        var offenders = Application.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name is "Tradeborn.Infrastructure" or "Tradeborn.Web")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Tradeborn.Application must not depend on {string.Join(", ", offenders)}. " +
            "Dependencies point inward; use an abstraction in Application/Abstractions instead.");
    }

    [Fact]
    public void Application_does_not_reference_EntityFrameworkCore()
    {
        // Persistence concerns leaking into use cases is how "swap the database" becomes
        // impossible and how handlers become untestable without a DbContext.
        var offenders = Application.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Guards docs/economy/ECONOMY_DESIGN.md §1: no floating-point type may touch the economy.
    /// </summary>
    [Fact]
    public void No_floating_point_fields_in_the_economy_domain()
    {
        var offenders = new List<string>();

        foreach (var type in Domain.GetTypes().Where(t => t.IsClass || t.IsValueType))
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(float) || field.FieldType == typeof(double))
                {
                    offenders.Add($"{type.FullName}.{field.Name} ({field.FieldType.Name})");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Balances and quantities must be integral — floating point drifts and makes the " +
            $"economy non-reproducible. Offenders: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Guards docs/architecture/REALTIME_AND_TIME_MODEL.md §7: the server clock is the only
    /// clock, injected via TimeProvider so tests can control it.
    /// </summary>
    [Fact]
    public void Domain_and_Application_do_not_read_the_ambient_clock()
    {
        var banned = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.DateTime.get_Now",
            "System.DateTime.get_UtcNow",
            "System.DateTime.get_Today",
            "System.DateTimeOffset.get_Now",
            "System.DateTimeOffset.get_UtcNow",
        };

        var offenders = new List<string>();

        foreach (var assembly in new[] { Domain, Application })
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(Everything).Concat<MethodBase>(type.GetConstructors(Everything)))
                {
                    foreach (var called in CalledMethods(method))
                    {
                        var name = $"{called.DeclaringType?.FullName}.{called.Name}";
                        if (banned.Contains(name))
                        {
                            offenders.Add($"{type.FullName}.{method.Name} calls {name}");
                        }
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Domain and Application must take time from an injected TimeProvider, never from the " +
            "ambient clock — otherwise settlement is untestable and the economy is not reproducible. " +
            $"Offenders: {string.Join("; ", offenders)}. See docs/architecture/REALTIME_AND_TIME_MODEL.md §7.");
    }

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Yields the methods a given method calls, by scanning its IL for call/callvirt and
    /// resolving the metadata token that follows.
    /// </summary>
    /// <remarks>
    /// Reflection alone cannot see call sites, so a name-based check here would be a test
    /// that can never fail. Scanning IL is the only way to make this rule real.
    /// </remarks>
    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception ex) when (ex is BadImageFormatException or NotSupportedException)
        {
            yield break;
        }

        if (il is null)
        {
            yield break;
        }

        const byte Call = 0x28;
        const byte Callvirt = 0x6F;

        // Constructors throw NotSupportedException from GetGenericArguments(); only
        // MethodInfo carries method-level generic parameters.
        var methodGenerics = method is MethodInfo info && info.IsGenericMethodDefinition
            ? info.GetGenericArguments()
            : null;
        var typeGenerics = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments()
            : null;

        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (Call or Callvirt))
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);

            MethodBase? resolved = null;
            try
            {
                resolved = method.Module.ResolveMethod(token, typeGenerics, methodGenerics);
            }
            catch (Exception ex) when (ex is ArgumentException or BadImageFormatException)
            {
                // Not a method token — this byte was operand data, not an opcode.
            }

            if (resolved is not null)
            {
                yield return resolved;
            }
        }
    }

    [Fact]
    public void Infrastructure_may_reference_Application_and_Domain()
    {
        // The permitted direction, asserted so an accidental reversal is caught.
        var referenced = Infrastructure.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.Contains("Tradeborn.Application", referenced);
        Assert.Contains("Tradeborn.Domain", referenced);
        Assert.DoesNotContain("Tradeborn.Web", referenced);
    }

    private static bool IsFrameworkAssembly(string name) =>
        name is "System.Runtime" or "System.Private.CoreLib" or "netstandard" or "System.Collections"
            or "System.Linq" or "System.Runtime.InteropServices" or "System.Memory"
        || name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.CSharp", StringComparison.Ordinal);
}
