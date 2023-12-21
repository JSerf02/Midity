using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Midity {
    /* A struct that stores all of the settings for playing a MIDI track */
    [System.Serializable]
    public struct MidiTrackSettings {
        [SerializeField, Tooltip("The AudioClip to play and its associated parameters")]
        public MidiPlayingClip clip;
        [SerializeField, Tooltip("An event that is triggered whenever a note is started")]
        public MidiPlayerEvent noteOnEvent;
        [SerializeField, Tooltip("An event that is triggered whenever a note stops")]
        public MidiPlayerEvent noteOffEvent;
    }
    public class MidiPlayer : MonoBehaviour {
        
        [Header("Settings")]
        [SerializeField, Tooltip("The Midi.cs component that manages the MIDI file this component will play")]
        private Midi midi;
        [SerializeField, Tooltip("The settings for each track in the song\n\n" + 
                                    "The i-th entry in this list will correspond to the i-th track that contains at least one note in the song\n\n" + 
                                    "The number of entries in this list MUST be at least the number of tracks with notes in the song")]
        private List<MidiTrackSettings> tracksSettings;
        [SerializeField, Tooltip("The transform of the parent object of all of the track player objects\n\n" + 
                                    "This parent object and all of its children will be managed by this component")]
        private Transform tracksParent;
        [SerializeField, Tooltip("When enabled, the Setup() function will get called whenever the Unity Start() function is called\n\n" + 
                                    "This parameter should be enabled when using the inspector to configure settings but disabled when using the Configure() function")]
        private bool setupOnStart = true;
        
        /* Removes any preexisting children of tracksParent and spawns the a child for each track player */
        public void Setup() {
            // Destroy all children with MidiTrackPlayer components
            MapTracks(player => Destroy(player.gameObject));

            // Spawn in new track players as child objects for the parent
            int settingsIdx = 0;
            for(int trackIdx = 0; midi.ContainsTrack(trackIdx); trackIdx++) {
                // Don't spawn tracks that have no notes
                if(!midi.GetTrack(trackIdx).ContainsNotes()) {
                    continue;
                }

                // Spawn tracks with the appropriate settings
                SpawnTrackPlayer(trackIdx, settingsIdx);
                settingsIdx++;
            }
        }

        /* Spawns a child object of tracksParent with a configured MidiTrackPlayer component for the inputted track and track settings */
        private void SpawnTrackPlayer(int trackIdx, int settingsIdx) {
            if(settingsIdx >= tracksSettings.Count) {
                throw new System.FormatException("No track settings found for index " + settingsIdx + "!");
            }

            // Create the new object as a child object of tracksParent
            GameObject newTrackPlayer = new GameObject("Track " + settingsIdx);
            newTrackPlayer.transform.parent = tracksParent;

            // Add and configure the MidiTrackPlayer component
            MidiTrackPlayer player = newTrackPlayer.AddComponent<MidiTrackPlayer>();
            MidiTrackSettings curSettings = tracksSettings[settingsIdx];
            player.Configure(midi, trackIdx, curSettings.clip, curSettings.noteOnEvent, curSettings.noteOffEvent);
        }

        /* Loads a new MIDI song file */
        public void Configure(Midi newMidi, List<MidiTrackSettings> newTracksSettings, Transform newTracksParent = null) {
            midi = newMidi;
            tracksSettings = newTracksSettings;
            if(newTracksParent != null) {
                tracksParent = newTracksParent;
            }
            Setup();
            setupOnStart = false;
        }

        /* Plays the song from the beginning */
        public void Play() {
            MapTracks(player => player.Play());
        }

        /* Stops the song and resets to the beginning */
        public void Stop() {
            MapTracks(player => player.Stop());
        }

        /* Stops the song but keeps the current timestamp */
        public void Pause() {
            MapTracks(player => player.Pause());
        }

        /* Continues playing the song at the current timestamp */
        public void Resume() {
            MapTracks(player => player.Resume());
        }

        /* Calls a function on every MidiTrackPlayer created during setup */
        private delegate void TrackPlayerAction(MidiTrackPlayer player);
        private void MapTracks(TrackPlayerAction action) {
            foreach(Transform child in tracksParent) {
                if(child.TryGetComponent(out MidiTrackPlayer player)) {
                    action(player);
                }
            }
        }

        /* Setup the MidiTrackPlayers if applicable */
        private void Start() {
            if(setupOnStart) {
                Setup();
            }
        }

        /* Destroy all MidiTrackPlayers spawned by this object whenever this object is destroyed */
        private void OnDestroy()
        {
            // Destroy all children with MidiTrackPlayer components
            MapTracks(player => Destroy(player.gameObject));
        }
    }

}

