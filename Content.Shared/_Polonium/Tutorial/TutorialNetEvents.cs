using Robust.Shared.Serialization;

namespace Content.Shared._Polonium.Tutorial;

[Serializable, NetSerializable]
public sealed class TutorialHelpRequestedEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class TutorialRestartRequestedEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class TutorialStartPracticalEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class TutorialRedialEvent : EntityEventArgs
{
    public string Address { get; }
    public string Message { get; }

    public TutorialRedialEvent(string address, string message)
    {
        Address = address;
        Message = message;
    }
}
