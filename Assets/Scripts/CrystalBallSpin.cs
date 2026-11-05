using UnityEngine;

namespace AiHackathon
{
    /// <summary>
    /// Spins the crystal sphere around its parent. The parent stays at the ball center, so aiming at it does not move.
    /// </summary>
    public class CrystalBallSpin : MonoBehaviour
    {
        public float degreesPerSecond = 140f;

        void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
