using BaseLib.Config;

namespace HoldYourTurn.HoldYourTurnCode;

[ConfigHoverTipsByDefault]
public sealed class HoldYourTurnConfig : SimpleModConfig
{
    private const float DefaultHoldTime = 1.5f;
    
    [ConfigSlider(0.0, 5.0, 0.1, Format = "{0:0.0}sec")]
    public static float TurnHoldTime { get; set; } = DefaultHoldTime;

    /// <summary>他のプレイヤーが使ったポーションの影響を受けたときも、追加リソース獲得と同様に扱う(既定は無効)。</summary>
    public static bool PotionInterruptsTurnEnd { get; set; } = false;
}