using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// The HUD fonts, kept in Resources (Assets/Game/Resources/GameGuiFonts.asset) so every scene's IMGUI picks them up
    /// without wiring: <see cref="GameGui"/> loads it the first time it needs a font.
    /// </summary>
    [CreateAssetMenu(menuName = "AiHackathon/Game GUI Fonts", fileName = "GameGuiFonts")]
    public class GameGuiFonts : ScriptableObject
    {
        public Font title;
        public Font body;
    }
}
