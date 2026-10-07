using UnityEngine;

namespace AlloyFramework.Audio.Wwise
{
    internal sealed class WwiseAudioHost : MonoBehaviour
    {
        internal WwiseAudioBackend Backend;

        private void LateUpdate() { Backend?.Tick(); }
        private void OnApplicationFocus(bool focused) { Backend?.SetFocused(focused); }
        private void OnApplicationPause(bool paused) { Backend?.SetPausedBySystem(paused); }
        private void OnApplicationQuit() { AudioManager.Instance.ShutdownImmediately(); }
        private void OnDestroy() { Backend?.ShutdownImmediately(); }
    }
}
