using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// Guards the CI test-selection invariants, which live in
// .github/workflows/playwright.yml and are not otherwise checked by anything.
//
// Two steps select tests, and they select on different things:
//
//   unit step:        -trait- "Category=*"          (everything with NO Category)
//   integration step: -trait "Category=<value>"  AND  a -class allow-list
//
// So a test class reaches CI only by satisfying a two-sided property, and each
// test below covers one side. Carrying a Category trait removes a class from the
// unit step; it does not get it run. Those are independent facts and checking
// only the first is how a class ends up executing nowhere while the build is
// green.
//
// This is not hypothetical. MarkPaidSecurityTest derives from PlaywrightBaseTest
// and carries Category=PluginSecurityTest - a value no CI step selects. It is
// excluded from the unit step, not selected by the integration step, and its 3
// tests have never run. Its sibling VendorPaySecurityTests carries
// Category=PlaywrightUITest and runs.
public class UnitTestFilterTests
{
    private const string WorkflowRelPath = ".github/workflows/playwright.yml";

    // Classes deliberately run by no CI step, each with the reason. An entry here
    // is a claim that somebody decided this on purpose; deleting an entry is how
    // the debt gets closed out. Reasons are quoted from the workflow comment on
    // the "Run tests" step, which is where this was recorded and where nobody
    // reads it.
    private static readonly (string Class, string Reason)[] KnownNotRunAnywhere =
    {
        ("BTCPayServer.Plugins.Tests.PluginPermissionUITest",
            "hangs indefinitely under the runner with zero test output, verified solo at the 20min step timeout; "
            + "suspected SharedPluginTestFixture init deadlock, needs its own follow-up"),
        ("BTCPayServer.Plugins.Tests.TransactionCounterPluginUITestStandalone",
            "hangs the same way as PluginPermissionUITest, same suspected cause"),
    };

    // Includes inherited methods on purpose. xunit discovers and runs a [Fact]
    // inherited from a base class, so a concrete class that declares none of its
    // own is still a test class the CI filter will select. Restricting this to
    // DeclaredOnly would make such a class invisible here while the runner
    // executed it - the guard would stay silent on precisely the failure it
    // exists to catch.
    private static bool HasTestMethods(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(m => m.CustomAttributes.Any(a =>
                a.AttributeType == typeof(FactAttribute) || a.AttributeType == typeof(TheoryAttribute)
                || a.AttributeType.IsSubclassOf(typeof(FactAttribute))));

    // Walks the base chain because the runner's own -trait resolution honours a
    // Category trait inherited from a base class. Checking declared attributes
    // only would fail a class whose trait is hoisted onto a shared base, which is
    // correct code the runner already handles right.
    private static string CategoryValue(Type type)
    {
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            var trait = t.CustomAttributes
                .Where(a => a.AttributeType == typeof(TraitAttribute))
                .FirstOrDefault(a => a.ConstructorArguments.Count == 2
                                     && (a.ConstructorArguments[0].Value as string) == "Category");
            if (trait is not null)
                return trait.ConstructorArguments[1].Value as string;
        }
        return null;
    }

    private static IEnumerable<Type> StackDependentTestClasses()
        => typeof(PlaywrightBaseTest).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => typeof(PlaywrightBaseTest).IsAssignableFrom(t) && t != typeof(PlaywrightBaseTest))
            .Where(HasTestMethods);

    private static string ReadWorkflow()
    {
        // Located by walking up from the test binary rather than hardcoding a
        // depth, because the bin path depends on TFM and configuration.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, WorkflowRelPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        // Fail closed. A guard that silently passes when it cannot find the thing
        // it checks is the exact defect these tests exist to catch.
        throw new InvalidOperationException(
            $"Could not locate {WorkflowRelPath} by walking up from {AppContext.BaseDirectory}. These tests cannot "
            + "verify anything without it and will not pass by default. If the workflow moved, update "
            + "WorkflowRelPath.");
    }

    // Category values any CI step positively selects. Note the negative filter
    // `-trait- "Category=*"` is deliberately NOT a consumer: it selects the
    // absence of a Category, so it can never make a specific value run. The
    // regex requires whitespace after `-trait`, which is what keeps `-trait-`
    // from matching.
    private static HashSet<string> ConsumedCategoryValues(string workflow)
        => Regex.Matches(workflow, @"-trait\s+""Category=([^""*]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> AllowListedClasses(string workflow)
        => Regex.Matches(workflow, @"-class\s+""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryDeclaredCategoryValue_IsSelectedBySomeCiStep()
    {
        // The cheapest of the three and the one that would have caught the real
        // defect on the day the trait was introduced. A Category value with a
        // producer and no consumer is a test class that has quietly left CI.
        //
        // Deliberately does NOT honour KnownNotRunAnywhere. A parked class still
        // has to carry a trait some step selects, because the trait describes
        // what the class needs to run and KnownNotRunAnywhere separately records
        // why it is not currently run. Those are orthogonal facts, and letting
        // the excuse list suppress this check would let a parked class also
        // accumulate a meaningless trait value nobody notices when it is
        // unparked.
        var workflow = ReadWorkflow();
        var consumed = ConsumedCategoryValues(workflow);

        Assert.True(consumed.Count > 0,
            "Parsed no `-trait \"Category=...\"` selectors out of " + WorkflowRelPath + ". Either no CI step "
            + "selects by Category any more - in which case these tests are stale and should be rewritten against "
            + "whatever replaced it - or the parse broke and this test is no longer checking anything.");

        var orphaned = StackDependentTestClasses()
            .Select(t => new { Type = t, Category = CategoryValue(t) })
            .Where(x => x.Category is not null && !consumed.Contains(x.Category))
            .GroupBy(x => x.Category, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"Category=\"{g.Key}\" on " + string.Join(" and ", g.Select(x => x.Type.FullName)))
            .ToList();

        Assert.True(orphaned.Count == 0,
            "These Category values are declared on stack-dependent test classes but no CI step selects them, so "
            + "those classes run nowhere: the unit step's `-trait- \"Category=*\"` excludes anything with a "
            + "Category, and the integration step's `-trait` selects only [" + string.Join(", ", consumed.OrderBy(v => v, StringComparer.Ordinal))
            + "]. Two edits clear this check and it does not care which you pick: (1) change the trait to a value "
            + "CI already selects, or (2) add a CI step that selects the current value. Parking the class in "
            + "KnownNotRunAnywhere is deliberately NOT one of them - that list records why a class is not being "
            + "run, while the trait records what the class needs in order to run, and a parked class carrying a "
            + "value nothing selects is still broken on the day somebody unparks it. So if you do not intend to "
            + "run this class for now, do both: park it there with a reason AND give it a selectable trait here. "
            + "Separately, changing the trait is not by itself enough to make the class run, because the "
            + "integration step also filters on a `-class` allow-list - see "
            + "EveryStackDependentTestClass_IsNamedInTheAllowList. Orphans: " + string.Join("; ", orphaned));
    }

    [Fact]
    public void EveryStackDependentTestClass_CarriesACategoryTrait()
    {
        // Side one: a class with no Category lands in the pre-docker unit step
        // and fails on socket connect rather than on its own merits.
        var offenders = StackDependentTestClasses()
            .Where(t => CategoryValue(t) is null)
            .Select(t => t.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These test classes derive from PlaywrightBaseTest, so they need the regtest stack, but carry no "
            + "Category trait. Without one they are selected by the CI unit-test step's `-trait- \"Category=*\"` "
            + "filter and will run before docker starts, failing on socket connect. Fixing this takes TWO edits, "
            + "and doing only the first leaves the class running nowhere with a green build: (1) add "
            + "[Trait(\"Category\", \"PlaywrightUITest\")], the value the integration step selects, and (2) add the "
            + "class to the `-class` allow-list on the \"Run tests\" step in " + WorkflowRelPath + ". Classes: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryStackDependentTestClass_IsNamedInTheAllowList()
    {
        // Side two. The integration step intersects `-class` with `-trait`, so
        // membership in the allow-list is necessary and not sufficient either.
        // Both halves are reported per class, because fixing one and not the
        // other leaves the class exactly as dead as it started.
        var workflow = ReadWorkflow();
        var listed = AllowListedClasses(workflow);
        var consumed = ConsumedCategoryValues(workflow);
        var excused = KnownNotRunAnywhere.Select(e => e.Class).ToHashSet(StringComparer.Ordinal);

        Assert.True(listed.Count > 0,
            "Parsed no `-class \"...\"` entries out of " + WorkflowRelPath + ". Either the integration step stopped "
            + "using an allow-list - in which case delete this test, it is now pure false-positive surface - or "
            + "the parse broke and this test is no longer checking anything.");

        var dead = new List<string>();
        foreach (var t in StackDependentTestClasses().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (excused.Contains(t.FullName))
                continue;

            var category = CategoryValue(t);
            var missingFromList = !listed.Contains(t.FullName);
            var traitNotSelected = category is null || !consumed.Contains(category);
            if (!missingFromList && !traitNotSelected)
                continue;

            var reasons = new List<string>();
            if (missingFromList)
                reasons.Add("not in the `-class` allow-list");
            if (traitNotSelected)
                reasons.Add(category is null
                    ? "carries no Category trait, which the step's `-trait` requires"
                    : $"carries Category=\"{category}\", which no `-trait` selector matches");
            dead.Add($"{t.FullName} ({string.Join("; ", reasons)})");
        }

        Assert.True(dead.Count == 0,
            "These classes need the regtest stack and are run by no CI step. The integration step selects the "
            + "INTERSECTION of its `-class` allow-list and its `-trait` filter, so satisfying one and not the "
            + "other still runs nothing - fix every reason listed for each class, not the first one. Add the class "
            + "to the `-class` list on the \"Run tests\" step in " + WorkflowRelPath + " AND give it a Category "
            + "value that step selects ([" + string.Join(", ", consumed.OrderBy(v => v, StringComparer.Ordinal))
            + "]). If the exclusion is deliberate, add the class to KnownNotRunAnywhere in this file with the "
            + "reason instead. Classes: " + string.Join(", ", dead));
    }

    [Fact]
    public void KnownNotRunAnywhere_ContainsNoStaleEntries()
    {
        // An excuse that outlives the thing it excuses is worse than no excuse,
        // because it reads as a live decision.
        var workflow = ReadWorkflow();
        var listed = AllowListedClasses(workflow);
        var allTypes = typeof(PlaywrightBaseTest).Assembly.GetTypes()
            .Select(t => t.FullName)
            .ToHashSet(StringComparer.Ordinal);

        var vanished = KnownNotRunAnywhere.Where(e => !allTypes.Contains(e.Class))
            .Select(e => e.Class).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var nowListed = KnownNotRunAnywhere.Where(e => listed.Contains(e.Class))
            .Select(e => e.Class).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var unexplained = KnownNotRunAnywhere.Where(e => string.IsNullOrWhiteSpace(e.Reason))
            .Select(e => e.Class).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(vanished.Count == 0,
            "KnownNotRunAnywhere excuses classes that no longer exist. Delete these entries: "
            + string.Join(", ", vanished));

        Assert.True(nowListed.Count == 0,
            "These classes are excused in KnownNotRunAnywhere but ARE in the workflow's `-class` allow-list. The "
            + "excuse is stale and now misdescribes what CI does. Delete these entries: "
            + string.Join(", ", nowListed));

        Assert.True(unexplained.Count == 0,
            "These KnownNotRunAnywhere entries carry no reason. The reason is the entire point of the list - an "
            + "exclusion nobody justified is indistinguishable from drift. Add one or delete the entry: "
            + string.Join(", ", unexplained));
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
        var nowTraited = mustBeUnitTests.Where(t => CategoryValue(t) is not null).Select(t => t.FullName).ToList();
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
