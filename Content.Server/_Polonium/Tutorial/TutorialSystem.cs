using Content.Server.GameTicking.Rules;
using Content.Shared._Polonium.Tutorial;
using Content.Shared._Polonium.Tutorial.Components;
using Content.Shared._Polonium.Tutorial.Prototypes;
using Content.Shared.CCVar;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Polonium.Tutorial;

public sealed class TutorialSystem : SharedTutorialSystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly TutorialActionExecutor _actions = default!;
    [Dependency] private readonly SolitarySpawningSystem _solitary = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    private static readonly string[] HelpOrder = ["brute", "burn", "toxin", "rad"];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TutorialStartRequestedEvent>(OnStartRequested);
        SubscribeLocalEvent<TutorialSessionComponent, ComponentShutdown>(OnShutdown);
        SubscribeNetworkEvent<TutorialHelpRequestedEvent>(OnHelpRequested);
        SubscribeNetworkEvent<TutorialRestartRequestedEvent>(OnRestartRequested);
        SubscribeNetworkEvent<TutorialStartPracticalEvent>(OnStartPractical);
    }

    public void TryAdvance(EntityUid player, Predicate<Shared._Polonium.Tutorial.Conditions.TutorialCondition> predicate)
    {
        if (!TryComp<TutorialSessionComponent>(player, out var session))
            return;

        if (!TryGetCurrentStep(session, out _, out var stepProto))
            return;

        if (stepProto.Completion is null)
            return;

        if (!predicate(stepProto.Completion))
            return;

        AdvanceStep((player, session));
    }

    public void ForceAdvance(Entity<TutorialSessionComponent?> player)
    {
        if (!Resolve(player, ref player.Comp, false))
            return;

        AdvanceStep((player.Owner, player.Comp));
    }

    public void ForceStartFlow(EntityUid player, ProtoId<TutorialFlowPrototype> flowId) =>
        StartFlow(player, flowId);

    private void OnStartRequested(TutorialStartRequestedEvent ev)
    {
        StartFlow(ev.Player, ev.Flow);
    }

    private void OnHelpRequested(TutorialHelpRequestedEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } player)
            return;

        RequestHelp(player);
    }

    private void OnStartPractical(TutorialStartPracticalEvent ev, EntitySessionEventArgs args)
    {
        _solitary.TryJoinFromLobby(args.SenderSession);
    }

    private void OnRestartRequested(TutorialRestartRequestedEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (_solitary.TryRestartTutorial(session))
            return;

        // tutorialforcestart / already in a flow on a dirty map - replay in place
        if (session.AttachedEntity is not { } mob || !TryComp<TutorialSessionComponent>(mob, out var tut))
            return;

        StartFlow(mob, tut.Flow);
    }

    private void StartFlow(EntityUid player, ProtoId<TutorialFlowPrototype> flowId)
    {
        if (!_proto.TryIndex(flowId, out var flow))
        {
            Log.Error($"Tutorial: flow prototype '{flowId}' not found");
            return;
        }

        if (flow.Steps.Count == 0)
        {
            Log.Error($"Tutorial: flow '{flowId}' has no steps");
            return;
        }

        // already in a flow? nuke it and start over (respawn case)
        if (HasComp<TutorialSessionComponent>(player))
            RemComp<TutorialSessionComponent>(player);

        var session = AddComp<TutorialSessionComponent>(player);
        session.Flow = flowId;
        session.Anchors = ResolveAnchorsOnGrid(player);

        // can't punch the pig / patients until the flow says otherwise
        EnsureComp<PacifiedComponent>(player);

        EnterStep((player, session), 0);
    }

    private void OnShutdown(Entity<TutorialSessionComponent> ent, ref ComponentShutdown args)
    {
    }

    private Dictionary<string, EntityUid> ResolveAnchorsOnGrid(EntityUid player)
    {
        var result = new Dictionary<string, EntityUid>();

        if (!TryComp(player, out TransformComponent? xform) || xform.GridUid is not { } grid)
        {
            Log.Warning($"Tutorial: player {ToPrettyString(player)} has no grid — anchors won't be resolved");
            return result;
        }

        var query = EntityQueryEnumerator<TutorialAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anchor, out var anchorXform))
        {
            if (anchorXform.GridUid != grid)
                continue;

            if (string.IsNullOrWhiteSpace(anchor.AnchorId))
                continue;

            result.TryAdd(anchor.AnchorId, uid);
        }

        Log.Debug($"Tutorial: resolved {result.Count} anchors on grid {grid} for {ToPrettyString(player)}");
        return result;
    }

    private void AdvanceStep(Entity<TutorialSessionComponent> ent)
    {
        if (TryGetCurrentStep(ent.Comp, out _, out var currentProto))
            _actions.ExecuteAll(ent.Owner, currentProto.OnComplete);

        var next = ent.Comp.CurrentStepIndex + 1;

        if (TryGetFlow(ent.Comp, out var flow) && next >= flow.Steps.Count)
        {
            CompleteFlow(ent, redial: true);
            return;
        }

        EnterStep(ent, next);
    }

    private void EnterStep(Entity<TutorialSessionComponent> ent, int index)
    {
        if (!TryGetFlow(ent.Comp, out var flow))
            return;

        if (index < 0 || index >= flow.Steps.Count)
        {
            CompleteFlow(ent);
            return;
        }

        var stepId = flow.Steps[index];
        if (!_proto.TryIndex(stepId, out var stepProto))
        {
            Log.Error($"Tutorial: step prototype '{stepId}' not found, aborting flow");
            CompleteFlow(ent);
            return;
        }

        ent.Comp.CurrentStepIndex = index;
        ent.Comp.CurrentStep = stepId;
        ent.Comp.NavigationAnchor = stepProto.NavigationAnchor;
        ent.Comp.NavigationTarget = ResolveNavigationTarget(ent.Comp, stepProto.NavigationAnchor);
        ent.Comp.StepStartedAt = _timing.CurTime;
        ent.Comp.HelpActive = false;
        ent.Comp.HelpKind = null;
        ent.Comp.HelpPatientName = null;
        Dirty(ent);

        _actions.ExecuteAll(ent.Owner, stepProto.OnEnter);

        Log.Debug($"Tutorial: {ToPrettyString(ent.Owner)} entered step '{stepId}' ({index + 1}/{flow.Steps.Count})");
    }

    private void CompleteFlow(Entity<TutorialSessionComponent> ent, bool redial = false)
    {
        Log.Debug($"Tutorial: {ToPrettyString(ent.Owner)} completed flow '{ent.Comp.Flow}'");

        if (redial)
            TryRedial(ent.Owner);

        ent.Comp.CurrentStep = null;
        ent.Comp.NavigationAnchor = null;
        ent.Comp.NavigationTarget = null;
        Dirty(ent);

        RemComp<TutorialSessionComponent>(ent.Owner);
    }

    private void TryRedial(EntityUid player)
    {
        var address = _cfg.GetCVar(CCVars.IntroReturnServerConnectionString);
        if (string.IsNullOrWhiteSpace(address))
            return;

        if (!_player.TryGetSessionByEntity(player, out var session))
            return;

        RaiseNetworkEvent(
            new TutorialRedialEvent(address, Loc.GetString("tutorial-redial-message")),
            session);
    }

    private bool TryGetFlow(TutorialSessionComponent session, out TutorialFlowPrototype flow)
    {
        return _proto.TryIndex(session.Flow, out flow!);
    }

    private bool TryGetCurrentStep(
        TutorialSessionComponent session,
        out ProtoId<TutorialStepPrototype> stepId,
        out TutorialStepPrototype proto)
    {
        stepId = default;
        proto = default!;

        if (session.CurrentStep is not { } id)
            return false;

        if (!_proto.TryIndex(id, out var result))
            return false;

        stepId = id;
        proto = result;
        return true;
    }

    private NetEntity? ResolveNavigationTarget(TutorialSessionComponent session, string? anchorId)
    {
        if (anchorId is null)
            return null;

        if (!session.Anchors.TryGetValue(anchorId, out var uid))
        {
            Log.Warning($"Tutorial: navigation anchor '{anchorId}' not found on grid");
            return null;
        }

        return GetNetEntity(uid);
    }

    public bool AreLivingPatientsHealed(EntityUid player)
    {
        foreach (var (uid, patient) in PatientsOnGrid(player))
        {
            if (patient.SpawnedDead)
                continue;

            if (_mobState.IsDead(uid))
                continue;

            if (!IsHealed(uid, patient))
                return false;
        }

        return true;
    }

    public bool AreDeadPatientsContained(EntityUid player)
    {
        foreach (var (uid, _) in PatientsOnGrid(player))
        {
            if (!_mobState.IsDead(uid))
                continue;

            if (!_container.IsEntityInContainer(uid))
                return false;
        }

        return true;
    }

    public void RequestHelp(EntityUid player)
    {
        if (!TryComp<TutorialSessionComponent>(player, out var session))
            return;

        if (!TryGetCurrentStep(session, out _, out var step) || !step.ShowHelp)
            return;

        var remaining = new List<(EntityUid Uid, TutorialPatientComponent Patient)>();
        foreach (var (uid, patient) in PatientsOnGrid(player))
        {
            if (patient.SpawnedDead)
                continue;

            if (_mobState.IsDead(uid))
                continue;

            if (IsHealed(uid, patient))
                continue;

            remaining.Add((uid, patient));
        }

        remaining.Sort((a, b) => HelpIndex(a.Patient.Kind).CompareTo(HelpIndex(b.Patient.Kind)));

        session.HelpActive = true;

        if (remaining.Count == 0)
        {
            session.HelpKind = "done";
            session.HelpPatientName = string.Empty;
            session.NavigationAnchor = "bodybag";
            session.NavigationTarget = ResolveNavigationTarget(session, session.NavigationAnchor);
            Dirty(player, session);
            return;
        }

        var idx = 0;
        if (session.HelpKind is { } kind)
        {
            var current = remaining.FindIndex(p => p.Patient.Kind == kind);
            if (current >= 0)
                idx = (current + 1) % remaining.Count;
        }

        var pick = remaining[idx];
        session.HelpKind = pick.Patient.Kind;
        session.HelpPatientName = Name(pick.Uid);
        session.NavigationAnchor = pick.Patient.MedkitAnchor;
        session.NavigationTarget = ResolveNavigationTarget(session, session.NavigationAnchor);
        Dirty(player, session);
    }

    private IEnumerable<(EntityUid Uid, TutorialPatientComponent Patient)> PatientsOnGrid(EntityUid player)
    {
        if (!TryComp(player, out TransformComponent? xform) || xform.GridUid is not { } grid)
            yield break;

        var query = EntityQueryEnumerator<TutorialPatientComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var patient, out var px))
        {
            if (px.GridUid != grid)
                continue;

            yield return (uid, patient);
        }
    }

    private bool IsHealed(EntityUid uid, TutorialPatientComponent patient)
    {
        if (!TryComp<DamageableComponent>(uid, out var dmg))
            return true;

        var threshold = FixedPoint2.New(patient.HealBelow);

        if (patient.DamageType is { } type)
        {
            var pos = _damageable.GetPositiveDamage((uid, dmg));
            return !pos.DamageDict.TryGetValue(type, out var v) || v <= threshold;
        }

        if (patient.DamageGroup is { } group)
            return !_damageable.TryGetDamageGreaterThan((uid, dmg), threshold, out _, group);

        return true;
    }

    private static int HelpIndex(string kind)
    {
        var i = Array.IndexOf(HelpOrder, kind);
        return i < 0 ? int.MaxValue : i;
    }
}
