using JobTrail.Modules.Applications.Domain;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.Modules.Applications.Tests;

/// <summary>
/// The pipeline state machine, proven exhaustively. Every one of the 64 ordered
/// (from, to) stage pairs is asserted: the 44 legal moves - each against the kind
/// it must record - and the 20 illegal ones. The legal set is written out by hand
/// below as the specification of the machine; the illegal set is its complement,
/// so neither theory re-runs the aggregate's own logic to decide what it expects.
///
/// Stages travel through the theory data as their names, not as the values: Stage
/// and TransitionKind are internal, and a public xUnit test method may not take an
/// internal type as a parameter. The names round-trip through <see cref="Parse"/>
/// inside each test, and read clearly in the test output besides.
/// </summary>
public sealed class StageTransitionTests
{
    // Two distinct instants: one drives an application into its starting stage,
    // the later one is the move under test - so an UpdatedAt stamp that must not
    // happen (an illegal move) can't hide behind the arrange-time value.
    private static readonly DateTimeOffset ArrangeTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MoveTime = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Stage[] ActiveStages = [Stage.Applied, Stage.Screening, Stage.Interview, Stage.Offer];

    // Accepted is deliberately absent: it is reachable from Offer alone, never as a
    // free terminal outcome, so it is neither a close-from-any-active nor a
    // reclassify target.
    private static readonly Stage[] FreeTerminals = [Stage.Rejected, Stage.Withdrawn, Stage.Ghosted];
    private static readonly Stage[] AllTerminals = [Stage.Accepted, Stage.Rejected, Stage.Withdrawn, Stage.Ghosted];

    /// <summary>
    /// The legal moves, hand-authored: the state machine as a lookup keyed by the
    /// ordered pair, valued by the kind the transition must report.
    /// </summary>
    private static readonly IReadOnlyDictionary<(Stage From, Stage To), TransitionKind> Legal = BuildLegalMoves();

    public static TheoryData<string, string, string> LegalMoves()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var ((from, to), kind) in Legal)
        {
            data.Add(from.ToString(), to.ToString(), kind.ToString());
        }

        return data;
    }

    public static TheoryData<string, string> IllegalMoves()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in Enum.GetValues<Stage>())
        {
            foreach (var to in Enum.GetValues<Stage>())
            {
                if (!Legal.ContainsKey((from, to)))
                {
                    data.Add(from.ToString(), to.ToString());
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LegalMoves))]
    public void A_legal_move_applies_and_reports_its_kind(string from, string to, string expectedKind)
    {
        var target = Parse(to);
        var application = ArrangeAt(Parse(from));

        var result = application.TransitionTo(target, MoveTime);

        result.IsSuccess.ShouldBeTrue();
        result.Value.From.ShouldBe(Parse(from));
        result.Value.To.ShouldBe(target);
        result.Value.Kind.ShouldBe(Enum.Parse<TransitionKind>(expectedKind));
        application.Stage.ShouldBe(target);
        application.UpdatedAt.ShouldBe(MoveTime);
    }

    [Theory]
    [MemberData(nameof(IllegalMoves))]
    public void An_illegal_move_is_rejected_and_leaves_the_application_untouched(string from, string to)
    {
        var source = Parse(from);
        var target = Parse(to);
        var application = ArrangeAt(source);
        var updatedBefore = application.UpdatedAt;

        var result = application.TransitionTo(target, MoveTime);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ApplicationErrors.IllegalTransition(source, target));
        result.Error.Type.ShouldBe(ErrorType.Validation);
        application.Stage.ShouldBe(source);
        application.UpdatedAt.ShouldBe(updatedBefore);
    }

    [Fact]
    public void An_application_starts_at_Applied() =>
        new Application().Stage.ShouldBe(Stage.Applied);

    [Theory]
    [InlineData(nameof(Stage.Rejected))]
    [InlineData(nameof(Stage.Withdrawn))]
    [InlineData(nameof(Stage.Ghosted))]
    public void Applied_can_jump_straight_to_a_terminal(string terminal)
    {
        // The common real path: most applications never advance, they go straight
        // from Applied to Rejected or Ghosted.
        var application = new Application();

        var result = application.TransitionTo(Parse(terminal), MoveTime);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Kind.ShouldBe(TransitionKind.Terminal);
        application.Stage.ShouldBe(Parse(terminal));
    }

    [Fact]
    public void Accepted_is_reachable_from_Offer_only()
    {
        ArrangeAt(Stage.Offer).TransitionTo(Stage.Accepted, MoveTime).IsSuccess.ShouldBeTrue();

        // Not from an earlier active stage, and not out of a terminal outcome -
        // an acceptance always follows an offer.
        ArrangeAt(Stage.Applied).TransitionTo(Stage.Accepted, MoveTime).IsFailure.ShouldBeTrue();
        ArrangeAt(Stage.Interview).TransitionTo(Stage.Accepted, MoveTime).IsFailure.ShouldBeTrue();
        ArrangeAt(Stage.Rejected).TransitionTo(Stage.Accepted, MoveTime).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_terminal_outcome_can_be_reclassified_but_never_into_Accepted()
    {
        // A ghost whose rejection email finally arrives.
        var application = ArrangeAt(Stage.Ghosted);

        var result = application.TransitionTo(Stage.Rejected, MoveTime);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Kind.ShouldBe(TransitionKind.Reclassify);
        application.Stage.ShouldBe(Stage.Rejected);

        // But Accepted stays off-limits without a fresh offer.
        ArrangeAt(Stage.Ghosted).TransitionTo(Stage.Accepted, MoveTime).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_terminal_can_be_reopened_to_any_active_stage()
    {
        foreach (var active in ActiveStages)
        {
            var application = ArrangeAt(Stage.Rejected);

            var result = application.TransitionTo(active, MoveTime);

            result.IsSuccess.ShouldBeTrue();
            result.Value.Kind.ShouldBe(TransitionKind.Reopen);
            application.Stage.ShouldBe(active);
        }
    }

    [Fact]
    public void A_same_stage_move_is_rejected()
    {
        var application = ArrangeAt(Stage.Screening);

        application.TransitionTo(Stage.Screening, MoveTime).IsFailure.ShouldBeTrue();
        application.Stage.ShouldBe(Stage.Screening);
    }

    private static Stage Parse(string name) => Enum.Parse<Stage>(name);

    /// <summary>
    /// Puts a fresh application into <paramref name="stage"/> by the shortest legal
    /// path from Applied - the only way in, since Stage has no public setter. Every
    /// step used here is itself asserted legal by
    /// <see cref="A_legal_move_applies_and_reports_its_kind"/>.
    /// </summary>
    private static Application ArrangeAt(Stage stage)
    {
        var application = new Application();
        foreach (var step in PathFromApplied(stage))
        {
            application.TransitionTo(step, ArrangeTime).IsSuccess.ShouldBeTrue(
                $"arranging into {stage} needs a legal move to {step}");
        }

        return application;
    }

    private static Stage[] PathFromApplied(Stage stage) => stage switch
    {
        Stage.Applied => [],
        // Accepted is reachable from Offer alone, so it takes two steps.
        Stage.Accepted => [Stage.Offer, Stage.Accepted],
        // Every other stage is one legal move from Applied: an advance for the
        // active stages, a close for Rejected/Withdrawn/Ghosted.
        _ => [stage],
    };

    private static Dictionary<(Stage From, Stage To), TransitionKind> BuildLegalMoves()
    {
        var moves = new Dictionary<(Stage From, Stage To), TransitionKind>();

        // Advance: an active stage to any strictly later active stage (skips allowed).
        for (var i = 0; i < ActiveStages.Length; i++)
        {
            for (var j = i + 1; j < ActiveStages.Length; j++)
            {
                moves[(ActiveStages[i], ActiveStages[j])] = TransitionKind.Advance;
            }
        }

        // Close: any active stage to Rejected/Withdrawn/Ghosted; Accepted from Offer only.
        foreach (var from in ActiveStages)
        {
            foreach (var to in FreeTerminals)
            {
                moves[(from, to)] = TransitionKind.Terminal;
            }
        }

        moves[(Stage.Offer, Stage.Accepted)] = TransitionKind.Terminal;

        // Reopen: any terminal back to any active stage.
        foreach (var from in AllTerminals)
        {
            foreach (var to in ActiveStages)
            {
                moves[(from, to)] = TransitionKind.Reopen;
            }
        }

        // Reclassify: one terminal outcome to another, except into Accepted.
        foreach (var from in AllTerminals)
        {
            foreach (var to in FreeTerminals)
            {
                if (from != to)
                {
                    moves[(from, to)] = TransitionKind.Reclassify;
                }
            }
        }

        return moves;
    }
}
