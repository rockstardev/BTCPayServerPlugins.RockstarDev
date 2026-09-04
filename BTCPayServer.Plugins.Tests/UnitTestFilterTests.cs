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
// Steps select tests, and they select on different things:
//
//   unit step: -trait- "Category=*"   (everything with NO Category)
//   any other: -trait "Category=<value>"  AND, optionally, a -class allow-list
//
// So a test class reaches CI only by satisfying a two-sided property, and each
// test below covers one side. Carrying a Category trait removes a class from the
// unit step; it does not get it run. Those are independent facts and checking
// only the first is how a class ends up executing nowhere while the build is
// green.
//
// Both halves have to hold on the SAME step, which is why the workflow is parsed
// one `dotnet run --` invocation at a time rather than as a file-wide set of
// traits and a file-wide set of classes. The union model is wrong twice over: it
// passes a class named on one step but traited for another, which nothing runs,
// and it fails a class selected by a step that carries no `-class` filter, which
// does run. The number of selecting steps is not fixed at two.
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

    // One selecting CI step: the trait values and class names on a single runner
    // invocation. Parsed per command line rather than as file-wide unions, because
    // selection is per step - a class named on one step and traited for a
    // different one is run by neither, and a step that filters on trait alone runs
    // every class carrying that trait. Collapsing the file into one `-class` set
    // and one `-trait` set gets both of those wrong.
    private sealed record CiSelector(string Command, HashSet<string> Traits, HashSet<string> Classes);

    private static List<CiSelector> CiSelectors(string workflow)
        => Regex.Matches(workflow, @"dotnet run --[^\r\n]*")
            .Select(m => m.Value)
            .Select(cmd => new CiSelector(
                cmd,
                Regex.Matches(cmd, @"-trait\s+""Category=([^""*]+)""")
                    .Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal),
                Regex.Matches(cmd, @"-class\s+""([^""]+)""")
                    .Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal)))
            .ToList();

    // An empty `-class` set is not "selects nothing", it is "no class filter", so
    // the step runs everything its `-trait` matches. That asymmetry is the whole
    // reason this is not a set-membership check.
    private static bool IsRunBySomeStep(string fullName, string category, List<CiSelector> selectors)
        => category is not null
           && selectors.Any(s => s.Traits.Contains(category)
                                 && (s.Classes.Count == 0 || s.Classes.Contains(fullName)));

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
            + "Separately, a selectable trait is not by itself enough to make the class run, because the step that "
            + "selects it may also carry a `-class` allow-list - see "
            + "EveryStackDependentTestClass_IsRunBySomeCiStep. Orphans: " + string.Join("; ", orphaned));
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
            + "filter and will run before docker starts, failing on socket connect. Adding the trait is only half "
            + "of it, and doing only that half leaves the class running nowhere with a green build: (1) add "
            + "[Trait(\"Category\", \"PlaywrightUITest\")], and (2) name the class in the `-class` allow-list on the "
            + "\"Run tests\" step in " + WorkflowRelPath + ", since that step has one. See "
            + "EveryStackDependentTestClass_IsRunBySomeCiStep for the general rule. Classes: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryStackDependentTestClass_IsRunBySomeCiStep()
    {
        // Side two, and the only check here that answers the question the file is
        // actually about: does some single step run this class. A step selects the
        // INTERSECTION of its own `-class` and `-trait` filters, so a class needs
        // both halves satisfied on the SAME step - and a step with no `-class`
        // filter at all imposes no such requirement, it runs whatever its `-trait`
        // matches.
        var workflow = ReadWorkflow();
        var selectors = CiSelectors(workflow);
        var excused = KnownNotRunAnywhere.Select(e => e.Class).ToHashSet(StringComparer.Ordinal);

        Assert.True(selectors.Any(s => s.Traits.Count > 0),
            "Parsed no `dotnet run --` invocation carrying a `-trait \"Category=...\"` selector out of "
            + WorkflowRelPath + ". Either no CI step selects by Category any more - in which case these tests are "
            + "stale and should be rewritten against whatever replaced it - or the parse broke and this test is no "
            + "longer checking anything.");

        var dead = new List<string>();
        foreach (var t in StackDependentTestClasses().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (excused.Contains(t.FullName))
                continue;

            var category = CategoryValue(t);
            if (IsRunBySomeStep(t.FullName, category, selectors))
                continue;

            // Report per step, because "no step runs it" is never the useful
            // fact - which half is missing on which step is.
            var detail = category is null
                ? "carries no Category trait, so no step's `-trait` can select it"
                : "carries Category=\"" + category + "\"; "
                  + string.Join(", ", selectors.Where(s => s.Traits.Count > 0).Select(s =>
                      !s.Traits.Contains(category)
                          ? $"the step selecting [{string.Join(", ", s.Traits.OrderBy(v => v, StringComparer.Ordinal))}] does not select that value"
                          : $"the step selecting [{string.Join(", ", s.Traits.OrderBy(v => v, StringComparer.Ordinal))}] selects that value but its `-class` allow-list does not name this class"));
            dead.Add($"{t.FullName} ({detail})");
        }

        Assert.True(dead.Count == 0,
            "These classes need the regtest stack and no single CI step runs them. A step selects the INTERSECTION "
            + "of its own `-class` allow-list and its own `-trait` filter, so satisfying one half on one step and "
            + "the other half on a different step still runs nothing. Fix it on ONE step, either by adding the "
            + "class to that step's `-class` list, or by giving it a Category value that step selects, or by "
            + "adding a step that selects its Category with no `-class` filter. If the exclusion is deliberate, "
            + "add the class to KnownNotRunAnywhere in this file with the reason instead. Classes: "
            + string.Join(", ", dead));
    }

    [Fact]
    public void KnownNotRunAnywhere_IsConsistentWithTheWorkflow()
    {
        // An excuse that outlives the thing it excuses is worse than no excuse,
        // because it reads as a live decision.
        //
        // Selection and completion are different properties, and this list tracks
        // the second. Both current entries are here because the class HANGS, not
        // because nobody selected it - so "a step now selects it" is not evidence
        // the excuse expired, it is evidence CI is about to hang. This check
        // therefore reports the contradiction and refuses to say which side is
        // wrong, because it cannot know: the same red means either "someone
        // deliberately started running this, delete the excuse" or "someone
        // widened a filter and pulled in a class known to hang, restore it".
        // Telling the reader to delete the excuse would push them toward the
        // configuration the repo has already observed hanging - the workflow's
        // own comment records that `-trait Category=PlaywrightUITest` alone, with
        // no `-class` filter, hangs exactly this way.
        var workflow = ReadWorkflow();
        var selectors = CiSelectors(workflow);
        var byName = typeof(PlaywrightBaseTest).Assembly.GetTypes()
            .Where(t => t.FullName is not null)
            .ToDictionary(t => t.FullName, t => t, StringComparer.Ordinal);

        var vanished = KnownNotRunAnywhere.Where(e => !byName.ContainsKey(e.Class))
            .Select(e => e.Class).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var selectedAnyway = KnownNotRunAnywhere
            .Where(e => byName.TryGetValue(e.Class, out var t)
                        && IsRunBySomeStep(e.Class, CategoryValue(t), selectors))
            .Select(e => $"{e.Class} (excused because: {e.Reason})")
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        var unexplained = KnownNotRunAnywhere.Where(e => string.IsNullOrWhiteSpace(e.Reason))
            .Select(e => e.Class).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(vanished.Count == 0,
            "KnownNotRunAnywhere excuses classes that no longer exist. Delete these entries: "
            + string.Join(", ", vanished));

        Assert.True(selectedAnyway.Count == 0,
            "These classes are recorded in KnownNotRunAnywhere as run by no CI step, but a step now selects them. "
            + "Those two facts contradict and this check cannot tell you which one is wrong, so read the recorded "
            + "reason before you touch anything. If the reason is that the class HANGS, the selector is the thing "
            + "that changed and the fix is to narrow it again - deleting the excuse would leave CI selecting a "
            + "class known to hang until the step's timeout. Widening a step by dropping its `-class` filter is "
            + "the usual way to arrive here, and the \"Run tests\" comment in " + WorkflowRelPath + " records that "
            + "the trait-only form hangs for exactly these classes. Only if the class has actually been fixed is "
            + "deleting the entry correct. Entries: " + string.Join("; ", selectedAnyway));

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
