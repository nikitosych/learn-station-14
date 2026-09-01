using Content.Shared._Polonium.Tutorial.Actions;
using Content.Shared._Polonium.Tutorial.Conditions;
using Content.Shared.Guidebook;
using Robust.Shared.Prototypes;

namespace Content.Shared._Polonium.Tutorial.Prototypes;

[Prototype("tutorialStep")]
public sealed partial class TutorialStepPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Instruction = string.Empty;

    [DataField]
    public string? NavigationAnchor;

    [DataField]
    public TutorialCondition? Completion;

    [DataField]
    public List<TutorialAction> OnEnter = new();

    [DataField]
    public List<TutorialAction> OnComplete = new();

    [DataField]
    public ProtoId<GuideEntryPrototype>? Guidebook;

    [DataField]
    public bool ShowHelp;

    [DataField]
    public bool Blocking;
}
