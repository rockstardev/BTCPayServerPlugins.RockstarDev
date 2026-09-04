using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// Guards the invariant the CI unit-test step depends on.
//
// CI runs the unit tests with `-trait- "Category=*"`, i.e. every test that does
// NOT carry a Category trait, in a step that executes before the docker stack
// comes up. That is only safe while "no Category trait" really does mean "needs
// nothing but the CLR". Every test class that needs the regtest stack derives
// from PlaywrightBaseTest, so the rule is: a PlaywrightBaseTest subclass with
// test methods must carry a Category trait, or it silently lands in the
// pre-docker step and fails on socket connect rather than on its own merits.
//
// This is not hypothetical. MarkPaidSecurityTest is named like a unit test and
// is not one - it derives from PlaywrightBaseTest and fails 3/3 standalone. It
// stays out of the unit step because it carries Category=PluginSecurityTest,
// which is a deliberate marker rather than a naming heuristic. That is exactly
// the distinction this test protects.
public class UnitTestFilterTests
{
    private static bool HasTestMethods(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(m => m.CustomAttributes.Any(a =>
                a.AttributeType == typeof(FactAttribute) || a.AttributeType == typeof(TheoryAttribute)
                || a.AttributeType.IsSubclassOf(typeof(FactAttribute))));

    // Walks the base chain because the runner's own -trait- resolution honours a
    // Category trait inherited from a base class. Checking declared attributes
    // only would fail a class whose trait is hoisted onto a shared base, which is
    // correct code the runner already excludes - this guard would be reporting a
    // defect that is not there.
    private static bool HasCategoryTrait(Type type)
    {
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            if (t.CustomAttributes
                .Where(a => a.AttributeType == typeof(TraitAttribute))
                .Any(a => a.ConstructorArguments.Count == 2
                          && (a.ConstructorArguments[0].Value as string) == "Category"))
                return true;
        return false;
    }

    [Fact]
    public void EveryStackDependentTestClass_CarriesACategoryTrait()
    {
        var offenders = typeof(PlaywrightBaseTest).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => typeof(PlaywrightBaseTest).IsAssignableFrom(t) && t != typeof(PlaywrightBaseTest))
            .Where(HasTestMethods)
            .Where(t => !HasCategoryTrait(t))
            .Select(t => t.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These test classes derive from PlaywrightBaseTest, so they need the regtest stack, but carry no "
            + "Category trait. Without one they are selected by the CI unit-test step's `-trait- \"Category=*\"` "
            + "filter and will run before docker starts, failing on socket connect. Add "
            + "[Trait(\"Category\", \"PlaywrightUITest\")] (or another Category value) to: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void TheUnitTestFilter_SelectsAtLeastTheKnownPureUnitClasses()
    {
        // Fails closed the other way: if someone adds a Category trait to a pure
        // unit class, or moves one under PlaywrightBaseTest, CI would quietly stop
        // running it. These four carry the money-handling and parsing coverage, so
        // name them explicitly.
        var mustBeUnitTests = new[]
        {
            typeof(StonewallSplitterTests),
            typeof(VendorPayPaidHostedServiceTests),
            typeof(LnurlVerifyTests.LnurlVerifyConnectionStringTests),
            typeof(VendorPayTests.EnumExtensionsTests)
        };

        // The two conditions break the step in different ways, so report them
        // separately rather than under one message. A Category trait drops the
        // class out of the filter silently; deriving from PlaywrightBaseTest does
        // not drop it, it keeps it in the pre-docker step where it needs a stack
        // that is not up yet.
        var nowTraited = mustBeUnitTests.Where(HasCategoryTrait).Select(t => t.FullName).ToList();
        var nowStackDependent = mustBeUnitTests
            .Where(t => typeof(PlaywrightBaseTest).IsAssignableFrom(t))
            .Select(t => t.FullName).ToList();

        Assert.True(nowTraited.Count == 0,
            "These classes carry the settlement and parsing coverage and are expected to run in the CI "
            + "unit-test step, but they now carry a Category trait, which excludes them from "
            + "`-trait- \"Category=*\"` - they would stop running entirely, silently: "
            + string.Join(", ", nowTraited));

        Assert.True(nowStackDependent.Count == 0,
            "These classes are expected to be pure unit tests but now derive from PlaywrightBaseTest. That "
            + "does not remove them from `-trait- \"Category=*\"`; it leaves them in the pre-docker step "
            + "needing a regtest stack that has not started, so they fail on socket connect: "
            + string.Join(", ", nowStackDependent));
    }
}
