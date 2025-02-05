using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.Audio;

namespace Content.Shared.Sound.Components;

[RegisterComponent]
public sealed partial class EmitSoundRandomOnUseComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("collections", required: true)]
    public List<string> Collections = new();

    [DataField]
    public bool Positional;

    [DataField("handle")]
    public bool Handle = true;

    public SoundSpecifier? Sound;
}
