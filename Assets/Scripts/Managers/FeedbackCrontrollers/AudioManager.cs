using System;
using UnityEngine;
using UnityEngine.Audio;

public enum SoundType
{
    SFX,
    UI,
    Music
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Serializable]
    private class AudioEntry
    {
        public string soundName;
        public AudioClip clip;

        [Range(0f, 1f)]
        public float volume;
        public bool loop;
        public bool doPitchVariation;
        public float pitchVariationAmount;
    }

    //private AudioSource sourcePrefab;
    //private ObjectPool<AudioSource> sourcePool;

    [SerializeField] private AudioEntry[] sfxLibrary;
    [SerializeField] private AudioEntry[] musicLibrary;

    public AudioMixerGroup sfxMixer;
    public AudioMixerGroup musicMixer;

    private float globalVolume = 1;

    [Range(0f, 1f)]
    public float sfxVolume;

    [Range(0f, 1f)]
    public float musicVolume;

    [SerializeField] private PooledSFX sfxSourcePrefab;
    private ObjectPool<PooledSFX> sfxSourcePool;

    public AudioSource musicSourceA;
    public AudioSource musicSourceB;

    private AudioSource activeMusicSource;
    private AudioSource inactiveMusicSource;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);

        musicSourceA.outputAudioMixerGroup = musicMixer;
        musicSourceB.outputAudioMixerGroup = musicMixer;

        BuildPools();
    }

    private void BuildPools()
    {
        sfxSourcePool = new ObjectPool<PooledSFX>(
            sfxSourcePrefab, 
            10, 
            transform
            );
    }

    #region Music

    [Header("Music Playback")]
    [Tooltip("Crossfade time when switching tracks (unscaled, so it works while paused).")]
    [SerializeField] private float musicFadeTime = 1.5f;

    private Coroutine musicFade;

    public string CurrentTrack { get; private set; }

    // Plays a track from the music library, crossfading from whatever is playing.
    // Asking for the track that is already playing does nothing, so callers can request freely.
    public void PlayMusicTrack(string name, bool doFade = true)
    {
        AudioEntry track = Array.Find(musicLibrary, sound => sound.soundName == name);
        if (track == null || track.clip == null)
        {
            Debug.LogError($"[AudioManager] No track with name: {name}");
            return;
        }

        if (CurrentTrack == name && activeMusicSource != null && activeMusicSource.isPlaying) return;
        CurrentTrack = name;

        var outgoing = activeMusicSource;
        var incoming = activeMusicSource == musicSourceA ? musicSourceB : musicSourceA;

        incoming.clip = track.clip;
        incoming.loop = true;
        incoming.volume = doFade ? 0f : TrackVolume(track);
        incoming.Play();
        activeMusicSource = incoming;

        StartFade(incoming, TrackVolume(track), outgoing, doFade ? musicFadeTime : 0f);
    }

    public void StopMusic(float fadeTime = 0f)
    {
        CurrentTrack = null;
        var outgoing = activeMusicSource;
        activeMusicSource = null;
        StartFade(null, 0f, outgoing, fadeTime);
    }

    // Entries left at 0 volume play at full volume (overall level is set by the mixer / settings)
    private static float TrackVolume(AudioEntry track) => track.volume > 0f ? track.volume : 1f;

    private void StartFade(AudioSource fadeIn, float targetVolume, AudioSource fadeOut, float duration)
    {
        if (musicFade != null) StopCoroutine(musicFade);

        // Any source that is neither fading in nor out is stale from an interrupted fade
        foreach (var s in new[] { musicSourceA, musicSourceB })
            if (s != fadeIn && s != fadeOut) s.Stop();

        musicFade = StartCoroutine(FadeRoutine(fadeIn, targetVolume, fadeOut, duration));
    }

    private System.Collections.IEnumerator FadeRoutine(AudioSource fadeIn, float targetVolume, AudioSource fadeOut, float duration)
    {
        float inStart = fadeIn != null ? fadeIn.volume : 0f;
        float outStart = fadeOut != null ? fadeOut.volume : 0f;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = t / duration;
            if (fadeIn != null) fadeIn.volume = Mathf.Lerp(inStart, targetVolume, k);
            if (fadeOut != null) fadeOut.volume = Mathf.Lerp(outStart, 0f, k);
            yield return null;
        }

        if (fadeIn != null) fadeIn.volume = targetVolume;
        if (fadeOut != null) { fadeOut.Stop(); fadeOut.volume = 0f; }
        musicFade = null;
    }

    #endregion

    #region SFX

    public void PlaySFX(string name, Vector3 position)
    {
        AudioEntry sound = Array.Find(sfxLibrary, sound => sound.soundName == name);
        if (sound == null)
        {
            Debug.LogError($"[AudioManager] No SFX with name: {name}");
            return;
        }

        float adjustedVolume = globalVolume * sfxVolume * sound.volume;

        var avaliableSource = sfxSourcePool.Get();
        avaliableSource.Play(sound.clip, sfxSourcePool.ReturnToPool, position, adjustedVolume, 1f);
    }

    #endregion
}
