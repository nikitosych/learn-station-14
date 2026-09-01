using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Polonium.Tutorial.Components;

[RegisterComponent]
public sealed partial class TutorialPatientComponent : Component
{
    [DataField(required: true)]
    public string Kind = string.Empty;

    [DataField]
    public bool SpawnedDead;

    [DataField]
    public ProtoId<DamageTypePrototype>? DamageType;

    [DataField]
    public ProtoId<DamageGroupPrototype>? DamageGroup;

    [DataField]
    public string MedkitAnchor = string.Empty;

    [DataField]
    public float HealBelow = 8f;
}
