using Content.Shared._Polonium.Tutorial.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Polonium.Tutorial.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class TutorialSessionComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<TutorialFlowPrototype> Flow;

    [DataField, AutoNetworkedField]
    public int CurrentStepIndex = -1;

    [DataField, AutoNetworkedField]
    public ProtoId<TutorialStepPrototype>? CurrentStep;

    [DataField, AutoNetworkedField]
    public string? NavigationAnchor;

    [DataField, AutoNetworkedField]
    public NetEntity? NavigationTarget;

    [ViewVariables]
    public Dictionary<string, EntityUid> Anchors = new();

    [ViewVariables]
    public TimeSpan StepStartedAt;

    [DataField, AutoNetworkedField]
    public bool HelpActive;

    [DataField, AutoNetworkedField]
    public string? HelpKind;

    [DataField, AutoNetworkedField]
    public string? HelpPatientName;
}

[Serializable, NetSerializable]
public sealed class TutorialStepPresentationState
{
    public string? StepId;
    public string? InstructionLocId;
    public NetEntity? NavigationTarget;
}
