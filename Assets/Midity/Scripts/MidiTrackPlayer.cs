using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Midity {
    [System.Serializable]
    public struct MidiPlayingClip {
        [SerializeField, Tooltip("The audio clip to play")]
        public AudioClip audioClip;
        [SerializeField, Tooltip("The base note of the audio clip\nIn the format [char - Note Letter][int - Octave][#/b - Sharp/Flat (optional)] \n\nEx: D5#")]
        public string baseNote;
        [SerializeField, Tooltip("The starting time, in seconds, of the clip\n\n" + 
                                    "Whenever the audio clip starts playing, it will begin at this timestamp\n" + 
                                    "This is useful since pitch shifting will speed up and slow down the track " + 
                                    "which can make the starts of certain audio clips sound incorrect")]
        public float startTime;
    }
    [System.Serializable]
    public class MidiPlayerEvent : UnityEvent<MidiEvent> {
        // Necessary declaration to create a Unity Event with parameters
    }

    /* Every track player must have exclusive control of an AudioSource*/
    [DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
    public class MidiTrackPlayer : MonoBehaviour {
        [Header("Track Selection Settings")]
        [SerializeField, Tooltip("The Midi.cs component that manages the MIDI file whose track this script will play")]
        private Midi midi;
        [SerializeField, Tooltip("The index of the track to play")]
        private int trackIdx;

        [Header("Audio Clip Settings")]
        [SerializeField, Tooltip("The AudioClip to play and its associated parameters")]
        private MidiPlayingClip clip;
        private int BaseNote { // Converts baseNote into its integer representation
            get {
                const int middleC = 60; // The note value of middle C
                int res = clip.baseNote[0] - 'A' - 2; // The offset from C
                res += middleC + (clip.baseNote[1] - '0' - 4) * 12; // The offset of the current octave from middle C's octave (4)

                // Adjust for sharps and flats
                if(clip.baseNote.Length > 2) {
                    if(clip.baseNote[2] == '#') {
                        res += 1;
                    }
                    else if(clip.baseNote[2] == 'b') {
                        res -= 1;
                    }
                }
                return res;
            }
        }
        
        [Header("Events")]
        [SerializeField, Tooltip("An event that is triggered whenever a note is started")]
        public MidiPlayerEvent noteOnEvent = new MidiPlayerEvent();
        [SerializeField, Tooltip("An event that is triggered whenever a note stops")]
        public MidiPlayerEvent noteOffEvent = new MidiPlayerEvent();
        [Header("Other Settings")]
        [SerializeField, Tooltip("When enabled, the Setup() function will get called whenever the Unity Start() function is called\n\n" + 
                                    "This parameter should be enabled when using the inspector to configure settings but disabled when using the Configure() function")]
        private bool setupOnStart = true;

        /* Internal Values */
        private AudioSource audioSource;
        private MidiTrackReader reader;
        private bool isPlaying = false;
        private bool notePlaying = false;
        
        /* Preventing audio popping */
        private const float volumeEaseTime = 1f / 60f; // The amount of time, in seconds, to spend changing volume
        private const float audioPlayDelay = 2f / 60f; // The amount of time, in seconds, to delay each note before playing
        private bool curEasing = false;

        /* Allows external scripts to setup internal settings values for this script */
        public void Configure(Midi midi, int trackIdx, MidiPlayingClip clip, MidiPlayerEvent noteOnEvent = null, MidiPlayerEvent noteOffEvent = null) {
            // Set settings
            this.midi = midi;
            this.trackIdx = trackIdx;
            this.clip = clip;

            // Add inputted events to the existing events
            if(noteOnEvent != null) {
                this.noteOnEvent.AddListener(noteOnEvent.Invoke);
            }
            if(noteOffEvent != null) {
                this.noteOffEvent.AddListener(noteOffEvent.Invoke);
            }

            // Setup the TrackPlayer
            Setup();

            // Make sure the Setup function is not called again in Start
            setupOnStart = false;
        }

        /* Sets up the AudioSource and the MidiTrackReader and stops the song from playing */
        public void Setup() {
            // Setup the audio source with the playing clip
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.clip = clip.audioClip;

            // Setup the track reader
            reader = midi.GetTrack(trackIdx).Clone();

            // Make sure the track has no polyphony, since polyphony is not supported
            if(reader.ContainsPolyphony()) {
                throw new System.FormatException("MIDI track " + trackIdx + " contains polyphony, which is not supported!");
            }

            // Setup internal parameters
            isPlaying = false;
            notePlaying = false;
        }

        /* Starts playing the song from the beginning */
        public void Play() {
            reader.Restart();
            isPlaying = true;
            notePlaying = false;
        }

        /* Stops playing the song and resets the song to the beginning */
        public void Stop() {
            reader.Restart();
            isPlaying = false;
            notePlaying = false;
        }

        /* Stops playing the song but keeps its timestamp */
        public void Pause() {
            isPlaying = false;
        }

        /* Resumes playing the song from the current time stamp */
        public void Resume() {
            isPlaying = true;
            if(notePlaying) {
                audioSource.Play();
            }
        }

        /* Calls the setup function whenever this Component is loaded in */
        private void Start() {
            if(setupOnStart) {
                Setup();
            }
        }

        /* Plays the next song events as long as the song is currently playing */
        private void Update() {
            if(reader.SongComplete) {
                isPlaying = false;
            }
            if(!isPlaying) {
                if(audioSource.isPlaying) {
                    audioSource.Stop();
                }
                return;
            }

            PlayNextEvents(Time.deltaTime);
        }

        /* Plays, pitch shifts, and stops the AudioSource according to the most recent MIDI events in the track */
        private void PlayNextEvents(float time) {
            // Get the MIDI events that occured during the most recent time step
            List<MidiEvent> midiEvents = reader.Advance(time);
            // Play each of the MIDI events
            foreach(MidiEvent midiEvent in midiEvents) {
                switch(midiEvent.eventType) {
                    case MidiEventType.NoteOn: // Start playing a note
                        float targetVolume = midiEvent.velocity / 100f;
                        if(notePlaying) {
                            EaseVolume(targetVolume);
                        }
                        else {
                            DelayedPlay(targetVolume, MidiNoteToPitch(midiEvent.note));
                        }
                        noteOnEvent.Invoke(midiEvent);
                        notePlaying = true;
                        break;
                    case MidiEventType.NoteOff: // Stop playing a note
                        EaseVolume(0); // Change volume while keeping note playing instead of calling Stop() to reduce audio pop
                        noteOffEvent.Invoke(midiEvent);
                        notePlaying = false;
                        break;
                }
                
            }
        }

        /* Gradually changes volume of the AudioSource */
        private void EaseVolume(float targetVolume) {
            StartCoroutine(EaseVolumeHelper(targetVolume));
        }
        private IEnumerator EaseVolumeHelper(float targetVolume) {
            // Wait for any previous easing actions to complete
            while(curEasing) {
                yield return new WaitForSeconds(1 / 60f);
            }

            // Make sure this is the only easing action currently taking place
            curEasing = true;

            // Linearly interpolate the volume from baseVolume to targetVolume over the course of volumeEaseTime
            float curTime = 0;
            float baseVolume = audioSource.volume;
            while(curTime < volumeEaseTime) {
                audioSource.volume = Mathf.Lerp(baseVolume, targetVolume, curTime / volumeEaseTime);
                float delay = Mathf.Min(1 / 60f, volumeEaseTime - curTime);
                yield return new WaitForSeconds(delay);
                curTime += delay;
            }
            audioSource.volume = targetVolume;

            // Return the ability for other easing actions to start
            curEasing = false;
        }


        /* Starts playing the AudioSource after a short delay */
        private void DelayedPlay(float targetVolume, float pitch) {
            StartCoroutine(DelayedPlayHelper(targetVolume, pitch));
        }
        private IEnumerator DelayedPlayHelper(float targetVolume, float pitch) {
            // Wait for any previous easing actions to complete
            while(curEasing) {
                yield return new WaitForSeconds(1 / 60f);
            }

            // Make sure this is the only easing action currently taking place
            curEasing = true;

            // Wait a short time and then start playing the audio source
            yield return new WaitForSeconds(audioPlayDelay);
            audioSource.volume = targetVolume;
            audioSource.pitch = pitch;
            audioSource.Play();

            // Return the ability for other easing actions to start
            curEasing = false;
        }

        /* Converts a MIDI note value into an AudioSource pitch */
        // Note that this method pf pitch shifting speeds up the clip! 
        private float MidiNoteToPitch(int note) {
            int noteDelta = note - BaseNote;

            // Math magic to shift the pitch
            // https://discussions.unity.com/t/pitch-in-unity/22657/3
            return Mathf.Pow(1.05946f, noteDelta);
        }
    }
}