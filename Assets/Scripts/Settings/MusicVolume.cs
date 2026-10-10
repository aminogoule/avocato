using UnityEngine;

/// Вешается на источник музыки: громкость из инспектора умножается на громкость музыки из настроек.
[RequireComponent(typeof(AudioSource))]
public class MusicVolume : MonoBehaviour
{
    private AudioSource _source;
    private float _baseVolume;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        _baseVolume = _source.volume;
    }

    private void OnEnable()
    {
        GameSettings.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        GameSettings.Changed -= Apply;
    }

    private void Apply()
    {
        if (_source != null) _source.volume = _baseVolume * Mathf.Clamp01(GameSettings.Current.musicVolume);
    }
}
