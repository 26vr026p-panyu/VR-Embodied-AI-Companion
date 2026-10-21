/// <summary>Explicit per-turn language identity; Auto remains experimental.</summary>
public enum AsrLanguageMode { Ja=0, Zh=1, Auto=2 }
public static class AsrLanguagePolicy
{
    public static string Code(AsrLanguageMode mode) => mode==AsrLanguageMode.Ja?"ja":mode==AsrLanguageMode.Zh?"zh":"auto";
    public static string FinalLanguage(AsrLanguageMode mode,string recognized) => mode==AsrLanguageMode.Auto?(recognized??""):Code(mode);
}
