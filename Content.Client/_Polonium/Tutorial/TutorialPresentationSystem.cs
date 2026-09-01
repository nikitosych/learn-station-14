using Content.Client._Polonium.Tutorial.Lobby;
using Content.Client._Polonium.Tutorial.Lobby.UI;
using Content.Client._Polonium.Pathfinding;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared._Polonium.Tutorial;
using Content.Shared._Polonium.Tutorial.Components;
using Content.Shared._Polonium.Tutorial.Conditions;
using Content.Shared._Polonium.Tutorial.Prototypes;
using Robust.Client;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Polonium.Tutorial;

public sealed class TutorialPresentationSystem : SharedTutorialSystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _uiMan = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ILocalizationManager _loc = default!;
    [Dependency] private readonly IGameController _game = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly PlayerPathfindingSystem _pathfinding = default!;

    // lobby Reset() used to eat this if the ids matched. keep it distinct.
    public const string OverlayId = "tutorial-ingame";

    private (string? Step, bool Help, string? Kind, string? Name) _lastUi;
    private TutorialUIController _tutorialUi = default!;
    private GuidebookUIController _guidebook = default!;

    public override void Initialize()
    {
        base.Initialize();

        _tutorialUi = _uiMan.GetUIController<TutorialUIController>();
        _guidebook = _uiMan.GetUIController<GuidebookUIController>();

        SubscribeLocalEvent<TutorialSessionComponent, AfterAutoHandleStateEvent>(OnSessionState);
        SubscribeLocalEvent<TutorialSessionComponent, ComponentShutdown>(OnSessionShutdown);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalAttached);
        SubscribeNetworkEvent<TutorialRedialEvent>(OnRedial);
        _state.OnStateChanged += OnStateChanged;
    }

    public void RequestRestart()
    {
        RaiseNetworkEvent(new TutorialRestartRequestedEvent());
    }

    public void RequestPracticalJoin()
    {
        RaiseNetworkEvent(new TutorialStartPracticalEvent());
    }

    private void OnRedial(TutorialRedialEvent ev)
    {
        try
        {
            _game.Redial(ev.Address, ev.Message);
        }
        catch (Exception e)
        {
            // without the launcher this just throws, that's fine
            Log.Warning($"Tutorial redial failed: {e.Message}");
        }
    }

    private void OnSessionState(Entity<TutorialSessionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Owner != _player.LocalEntity)
            return;

        TryShow(ent);
    }

    private void OnLocalAttached(LocalPlayerAttachedEvent ev)
    {
        TryShowLocal(force: true);
    }

    private void OnStateChanged(StateChangedEventArgs args)
    {
        if (args.NewState is GameplayState)
            TryShowLocal(force: true);
    }

    private void OnSessionShutdown(Entity<TutorialSessionComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Owner != _player.LocalEntity)
            return;

        ClearPathfinding();
        ClearBubble();
        _lastUi = default;
    }

    private void TryShowLocal(bool force)
    {
        if (_player.LocalEntity is not { } uid || !TryComp<TutorialSessionComponent>(uid, out var session))
            return;

        if (force)
            _lastUi = default;

        TryShow((uid, session));
    }

    private void TryShow(Entity<TutorialSessionComponent> ent)
    {
        // lobby CancelTutorial/Reset runs on the same state change and used to delete this overlay
        if (_state.CurrentState is not GameplayState)
            return;

        ApplyState(ent);
    }

    private void ApplyState(Entity<TutorialSessionComponent> ent)
    {
        UpdatePathfinding(ent.Owner, ent.Comp);

        var ui = (ent.Comp.CurrentStep?.Id, ent.Comp.HelpActive, ent.Comp.HelpKind, ent.Comp.HelpPatientName);
        if (ui == _lastUi)
            return;

        ClearBubble();

        if (ent.Comp.CurrentStep is { } stepId && _proto.TryIndex(stepId, out var stepProto))
        {
            ShowInstructionBubble(ent.Comp, stepId, stepProto);
            _lastUi = ui;
        }
        else
        {
            _lastUi = default;
        }
    }

    private void UpdatePathfinding(EntityUid player, TutorialSessionComponent session)
    {
        if (session.NavigationAnchor is { } anchorId)
        {
            _pathfinding.SetDestinationAnchor(player, anchorId);
            return;
        }

        _pathfinding.SetDestinationAnchor(player, null);
    }

    private void ClearPathfinding()
    {
        if (_player.LocalEntity is not { } local)
            return;

        _pathfinding.SetDestinationAnchor(local, null);
    }

    private void ShowInstructionBubble(
        TutorialSessionComponent session,
        ProtoId<TutorialStepPrototype> stepId,
        TutorialStepPrototype stepProto)
    {
        if (_tutorialUi.ActiveOverlay is { Id: OverlayId })
            _tutorialUi.RequestClose(false);

        _tutorialUi.PlanOverlay(
            OverlayId,
            rootControl: _uiMan.RootControl,
            backgroundColor: stepProto.Blocking ? Color.Black.WithAlpha(0.75f) : Color.Transparent,
            isSelfClosingOnClick: false,
            ignoreBackgroundClicks: stepProto.Blocking);

        var instruction = _loc.GetString(stepProto.Instruction);
        TutorialBubble bubble;

        if (session.HelpActive && !string.IsNullOrEmpty(session.HelpKind))
        {
            var help = _loc.GetString(
                $"tutorial-help-{session.HelpKind}",
                ("name", session.HelpPatientName ?? string.Empty));
            bubble = new TutorialBubble(instruction, help);
        }
        else
        {
            bubble = new TutorialBubble(instruction);
        }

        bubble.ClickAction = TutorialBubble.ClickBehaviour.Ignore;
        bubble.TippyVariant = stepProto.Blocking
            ? TutorialBubble.Tippy.ClownRegular
            : TutorialBubble.Tippy.None;

        if (stepProto.Guidebook is { } guideId)
            AddGuidebookButton(bubble, guideId);

        if (stepProto.ShowHelp)
            AddHelpButton(bubble);

        if (stepProto.Completion is ManualAcknowledgeCondition)
            AddAcknowledgeButton(bubble, stepId, stepProto.Blocking);

        _tutorialUi.PlanBubble(
            bubble,
            stepProto.Blocking
                ? TutorialHighlightOverlay.OverlayControlPosition.Center
                : TutorialHighlightOverlay.OverlayControlPosition.BottomRight,
            overlayId: OverlayId,
            spacing: 40f);
    }

    private void AddGuidebookButton(TutorialBubble bubble, ProtoId<Content.Shared.Guidebook.GuideEntryPrototype> guideId)
    {
        var button = new Button
        {
            Text = _loc.GetString("tutorial-bubble-guidebook"),
            HorizontalAlignment = Control.HAlignment.Center,
        };

        button.OnPressed += _ => _guidebook.OpenGuidebook(selected: guideId);
        bubble.ButtonsContainer.AddChild(button);
    }

    private void AddHelpButton(TutorialBubble bubble)
    {
        var button = new Button
        {
            Text = _loc.GetString("tutorial-bubble-help"),
            HorizontalAlignment = Control.HAlignment.Center,
        };

        button.OnPressed += _ => RaiseNetworkEvent(new TutorialHelpRequestedEvent());
        bubble.ButtonsContainer.AddChild(button);
    }

    private void AddAcknowledgeButton(TutorialBubble bubble, ProtoId<TutorialStepPrototype> stepId, bool blocking)
    {
        var button = new Button
        {
            Text = _loc.GetString(blocking ? "tutorial-bubble-exit" : "tutorial-bubble-acknowledge"),
            HorizontalAlignment = Control.HAlignment.Center,
        };

        button.OnPressed += _ =>
        {
            RaiseNetworkEvent(new TutorialAcknowledgeStepEvent(stepId.Id));
            button.Disabled = true;
        };

        bubble.ButtonsContainer.AddChild(button);
    }

    private void ClearBubble()
    {
        if (_tutorialUi.ActiveOverlay is { Id: OverlayId })
            _tutorialUi.RequestClose(false);
    }
}
