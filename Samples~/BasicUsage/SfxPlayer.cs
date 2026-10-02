using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Plays clips on its own source and clears the clip after it ends; the timer ends with this component.</summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        private AudioSource _source;
        private Lifetime _release;

        // The source arrives here because the demo builds its objects in code.
        public void Initialize(AudioSource source) => _source = source;

        private void Awake() => _release = this.GetLifetime().CreateChild("Release");

        public void PlayAndRelease(AudioClip clip)
        {
            _release.Cancel();                 // the earlier release would clear this clip, so it is dropped
            _source.clip = clip;
            _source.Play();

            // Audio runs in real time, so the release ignores the time scale.
            _release.After(clip.length, _source, static source => source.clip = null, ignoreTimeScale: true);
        }
    }
}
