using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Serializes every test class that swaps <c>ProtectedDataShim</c>'s delegates.
///
/// <para>
/// Those delegates (<c>ProtectImpl</c> / <c>UnprotectImpl</c>) are process-global
/// mutable statics. Each class that overrides them restores the originals in its
/// own <c>Dispose</c>, which is correct for <i>sequential</i> execution -- but
/// xUnit's unit of parallelization is the test collection, and by default every
/// class is its own collection, so those classes run <i>concurrently</i>. When
/// they do, one class's shim (e.g. the mobile "no DPAPI, throw" stand-in) is the
/// active global while another class (e.g. the MachineBound round-trip, which
/// relies on its own XOR stand-in) calls through it -- and the round-trip throws
/// <c>PlatformNotSupportedException</c>. That is the intermittent red CI run:
/// a data race on a shared static, surfacing or not purely by scheduling.
/// </para>
///
/// <para>
/// Placing all shim-mutating classes in one named collection makes xUnit run
/// them one at a time relative to each other (tests within a collection never
/// run in parallel), so the active shim during any test is always the one that
/// test installed. This is deterministic regardless of run order or parallelism
/// -- it removes the race rather than reducing its probability. Per-class
/// ctor/Dispose isolation cannot do this: it guards against leakage <i>between</i>
/// sequential tests, not against a concurrently-running class mutating the same
/// global mid-test.
/// </para>
///
/// <para>
/// Any future test that assigns <c>ProtectedDataShim.ProtectImpl</c> or
/// <c>UnprotectImpl</c> must carry <c>[Collection(Name)]</c> as well, or it
/// reopens the race.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class ProtectedDataShimCollection
{
    public const string Name = "ProtectedDataShim (global delegate) - serialized";
}
