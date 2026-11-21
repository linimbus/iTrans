namespace Itrans.Audio;

public enum AudioSourceKind
{
    System,
    Application,
    Microphone,
}

public sealed record AudioApplication(uint ProcessId, string Name)
{
    public override string ToString() => Name;
}

public sealed record AudioDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record AudioSourceOption(string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed record AudioLanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}
