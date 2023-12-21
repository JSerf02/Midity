using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif // ENABLE_INPUT_SYSTEM

namespace Midity {
    /* Types for all of the events that are called during hit detection */
    [System.Serializable]
    public class NoteSpawnEvent : UnityEvent<float, MidiEvent> {
        /* 
            * An event that is called whenever a note is added to the hit detection system's queue of notes
            * 
            * Params:
            * - float time     - The time that the note was spawned in
            * - MidiEvent note - The note that was spawned in
        */
    }
    [System.Serializable]
    public class HitEvent : UnityEvent<float, MidiEvent> {
        /* 
            * An event that is called whenever a note is successfully hit
            * 
            * Params:
            * - float time     - The time that the note was hit
            * - MidiEvent note - The note that was hit
        */
    }
    [System.Serializable]
    public class MissEvent : UnityEvent<MidiEvent> {
        /* 
            * An event that is called whenever a note is missed, meaning the user 
            * did not hit the note in time
            * 
            * Params:
            * - MidiEvent note - The note that was missed
        */
    }

    [System.Serializable]
    public class MisinputEvent : UnityEvent<float> {
            /* 
            * An event that is called whenever the player makes an input at the wrong time
            * meaning that their input did not hit any notes
            * 
            * Params:
            * - float time - The time that the player misinputted
        */
    }
    [System.Serializable]
    public class SongCompletionEvent : UnityEvent {
            /* 
            * An event that is called whenever the song completes and ever note 
            * was either hit or missed
        */
    }


    /* A class that manages user inputs to determine note hits and misses as a song plays */
    public class HitDetection : MonoBehaviour {
        /* The ways of representing time in various settings */
        public enum TimeMeasurementMode {
            Seconds, 
            Beats
        }
        /* The different methods of detecting inputs */
        public enum InputMode {
            PressOnly, // Only note presses events will register
            ReleaseOnly, // Only note release events will register
            PressAndRelease, // Note press and note release events will both register
            PressAndLongRelease // Note press events will register and note release events will register as long as the length of the note is sufficiently long
        }

        

        [Header("Song Settings")]
        [SerializeField, Tooltip("The Midi.cs file that manages the song that is performing hit detection")]
        private Midi midi;
        [SerializeField, Tooltip("The index of the track used for hit detection\n\n" + 
                                    "This index starts at 0 and only takes into account tracks that have at least one note\n" + 
                                    "Ex: A song with one track that stores only tempo data and another track with music data will only have one valid index: 0")]
        private int trackIdx;

        [Header("Time Measurement Setting")]
        [SerializeField, Tooltip("The method for measuring time used for various settings \n\n" + 
                                    "Note that when in Beats mode, times provided will scale as the tempo of the song changes")]
        private TimeMeasurementMode timeMeasurementMode;

        [Header("Detection Interval Settings")]
        [SerializeField, Tooltip("The size of the interval before and after the actual note time\n\n" +
                                    "This value is measured using the unit indicated by the Time Measurement Mode\n\n" +   
                                    "Ex: 0.5 in Seconds Mode means the player will have 0.5 seconds before the note and 0.5 seconds after the note to hit the note\n\n" + 
                                    "Ex: 0.5 in Beats Mode means the player will have half of a quarter note's length before the note and half of a quarter note's length after the note to hit the note\n"+ 
                                    "Then, if the tempo is 60BPM, a quarter note would take 1 second so the player would have 0.5 seconds before the note and 0.5 seconds after the note to hit it\n" + 
                                    "However, if the tempo changes to 120BPM, a quarter note would now take 0.5 seconds so the player would have 0.25 seconds before the note and 0.25 seconds after the note to hit the note")]
        private float intervalSize;
        /* Returns the interval size for any note at the inputted tempo */
        public float IntervalSize(float secondsPerBeat) {
            return timeMeasurementMode switch {
                TimeMeasurementMode.Seconds => intervalSize,
                TimeMeasurementMode.Beats => intervalSize * secondsPerBeat,
                _ => 0 // Impossible case
            };
        }
        /* Returns the interval size for the inputted note */
        public float IntervalSize(MidiEvent note) {
            return IntervalSize(midi.TempoAtTime(note.time));
        }
        /* Returns whether a given time is within the time interval for hitting a given note */
        public bool InNoteInterval(float time, MidiEvent note) {
            float size = IntervalSize(note);
            return note.time - size <= time && time <= note.time + size;
        }

        [SerializeField, Tooltip("The amount of time before a note's actual play time where the note should be spawned in and considered by the hit detection algoithm\n\n" + 
                                    "This value is measured using the unit indicated by the Time Measurement Mode\n\n" +   
                                    "This value must be at least as large as intervalSize, so the program will correct it automatically if not\n\n" + 
                                    "The primary usecase for this value is controlling how long before a note's play time that the note spawn events should be called\n" + 
                                    "If you are not using these events, feel free to leave this as 0!")]
        private float preemptionTime = 0;
        /* The preemption time converted to seconds */
        public float PreemptionTime {
            get {
                // Convert the user's input to seconds
                float res =  timeMeasurementMode switch {
                    TimeMeasurementMode.Seconds => preemptionTime,
                    TimeMeasurementMode.Beats => preemptionTime * midi.timeChanges[0].secondsPerBeat,
                    _ => 0 // Impossible case
                };
                // Override the user's input if it is smaller than the interval size
                return Mathf.Max(res, IntervalSize(midi.SlowestTempo));
            }
            
        }

        [Header("Input Detection Settings")]
        [SerializeField, Tooltip("The types of inputs that are detected\n\n" + 
                                    "Press Only: Only note presses will register\n" + 
                                    "Release Only: Only note releases will register\n" + 
                                    "Press And Release: Note press and note release events will both register\n" + 
                                    "Press And Long Release: Note press events will register and note release events will register as long as the length of the note is sufficiently long\n\n" + 
                                    "Select \"Press And Long Release\" if you want to create a rhythm game with regular press hit detection and occasional held notes")]
        private InputMode inputMode;
        [SerializeField, Tooltip("The minimum time of a held note when using the \"Press And Long Release\" Input Mode\n\n" + 
                                    "This value is measured using the unit indicated by the Time Measurement Mode\n\n" +   
                                    "This value is ignored when using input modes other than \"Press And Long Release\"")]
        private float minReleaseTime;
        /* The minimum release time of any note when the song has the inputted tempo */
        private float MinReleaseTime(float secondsPerBeat) {
            return timeMeasurementMode switch {
                TimeMeasurementMode.Seconds => minReleaseTime,
                TimeMeasurementMode.Beats => minReleaseTime * secondsPerBeat,
                _ => 0 // Impossible case
            };
        }
         /* The minimum release time of the inputted note */
        private float MinReleaseTime(MidiEvent note) {
            return MinReleaseTime(midi.TempoAtTime(note.time - 0.001f)); // Subtract a small amount to ensure we get the tempo before the note is played in case the tempo changes during this note
        }
        [SerializeField, Tooltip("The amount of time that should pass before the player can missclick notes\n\n" + 
                                    "This value is measured using the unit indicated by the Time Measurement Mode")]
        private float safetyTime;
        public float SafetyTime {
            get {
                return timeMeasurementMode switch {
                    TimeMeasurementMode.Seconds => safetyTime,
                    TimeMeasurementMode.Beats => safetyTime * midi.timeChanges[0].secondsPerBeat,
                    _ => 0 // Impossible case
                };
            }
        }

        [Header("Events")]
        [Tooltip("The event that is called whenever a note that can be hit with a button press spawns in")]
        public NoteSpawnEvent onPressSpawn;
        [Tooltip("The event that is called whenever a note that can be hit with a button release spawns in")]
        public NoteSpawnEvent onReleaseSpawn;
        [Tooltip("The event that is called whenever the player successfully hits a note with a button press")]
        public HitEvent onPressHit;
        [Tooltip("The event that is called whenever the player successfully hits a note with a button release")]
        public HitEvent onReleaseHit;
        [Tooltip("The event that is called whenever the player misses their chance to hit a note that requires a button press")]
        public MissEvent onPressMiss;
        [Tooltip("The event that is called whenever the player misses their chance to hit a note that requires a button release")]
        public MissEvent onReleaseMiss;
        [Tooltip("The event that is called whenever the player accidentally presses an extra time")]
        public MisinputEvent onExtraPress;
        [Tooltip("The event that is called whenever the player accidentally releases an extra time")]
        public MisinputEvent onExtraRelease;
        [Tooltip("The event that is called whenever the song finishes")]
        public SongCompletionEvent onSongCompletion;

        [Header("Other Settings")]
        [SerializeField, Tooltip("The amount of time, in seconds, after playing a note before the detection period of the note should start")]
        private float audioLatency;
        public float AudioLatency {
            get {
                return audioLatency;
            }
        }
        [SerializeField, Tooltip("When enabled, the Setup() function will get called whenever the Unity Start() function is called\n\n" + 
                                    "This parameter should be enabled when using the inspector to configure settings but disabled when using the Configure() function")]
        private bool setupOnStart = true;
        // Add the name of the Old Input System's action when using the Old Input System
        #if ENABLE_LEGACY_INPUT_MANAGER
        [Header("Old Input System")]
        [SerializeField, TooltiP("Whether you are using the Old Input System")]
        private bool usingOldInputSystem = false;
        [SerializeField, Tooltip("The name of the input action that controls the hit detection script")]
        private string inputName;
        #endif
        /* Private Values */
        private MidiTrackReader reader; // The reader that gives each of the midi notes in the current track
        private readonly Queue<MidiEvent> upcomingNotes = new Queue<MidiEvent>(); // All of the notes that the user hasn't yet hit
        private float curTime = 0; // The current time in the song, offset by the preemption and the audio latency
        private float lastPressTime = -1; // The last time that a note with a press action was added to upcomingNotes
        private bool isPlaying = false; // Whether this component is currently playing through the song and detecting inputs
        /* The internal timestamp of the song when the first note is spawned */
        private float StartTime {
            get {
                return -PreemptionTime - audioLatency;
            }
        }
        public bool IsPlaying {
            get {
                return isPlaying;
            }
        }
        private bool buttonState = false; // The current state of the user's inputs
        /* Whether hit detection for the song is complete, meaning whether every note has either been hit or missed */
        public bool SongComplete {
            get {
                return reader.SongComplete && !upcomingNotes.Any();
            }
        }
        /* Whether a note that is yet to be added can be added as a Press note */
        private bool CanPressNote(MidiEvent note) {
            // Press notes must be notes that play sounds
            if(note.eventType != MidiEventType.NoteOn) {
                return false;
            }

            // Make sure the note's mode is one that allows presses
            InputMode[] validModes = {InputMode.PressOnly, InputMode.PressAndRelease, InputMode.PressAndLongRelease};
            return validModes.Contains(inputMode);
        }
        /* Whether a note that is yet to be added can be added as a Release note */
        private bool CanReleaseNote(MidiEvent note) {
            // Release notes must be notes that stop sounds
            if(note.eventType != MidiEventType.NoteOff) {
                return false;
            }

            // In PressAndLongRelease mode, the note can be added if enough time has passed since the last press note
            if(inputMode == InputMode.PressAndLongRelease) {
                return lastPressTime > 0 && note.time - lastPressTime >= MinReleaseTime(note);
            }

            // Make sure the note's mode is one that allows presses
            InputMode[] validModes = {InputMode.ReleaseOnly, InputMode.PressAndRelease};
            return validModes.Contains(inputMode);
        }
        /* Whether the player is allowed to miss Press notes */
        private bool CanMissPress {
            get {
                if(curTime < StartTime + SafetyTime) {
                    return false;
                }
                // Make sure the note's mode is one that allows presses
                InputMode[] validModes = {InputMode.PressOnly, InputMode.PressAndRelease, InputMode.PressAndLongRelease};
                return validModes.Contains(inputMode);
            }
        }
        /* Whether the player is allowed to miss Release notes */
        private bool CanMissRelease {
            get {
                if(curTime < StartTime + SafetyTime) {
                    return false;
                }
                // Make sure the note's mode is one that allows releases
                InputMode[] validModes = {InputMode.ReleaseOnly, InputMode.PressAndRelease};
                return validModes.Contains(inputMode);
            }
        }

        /* Setups up the track reader and all internal parameters */
        public void Setup() {
            setupOnStart = false;
            reader = midi.GetTrackWithNotes(trackIdx).Clone();
            Restart();
        }
        
        /* Sets up all internal parameters */

        public void Restart() {
            // Reset the track reader to the beginning of the song
            reader.Restart();

            // Reset internal parameters
            isPlaying = false;
            curTime = -PreemptionTime - audioLatency;
            lastPressTime = -1;

            // Clear any remaining notes from previous hit detections
            upcomingNotes.Clear();

            // Add starting notes
            AddNotes(0);
            UpdateNotesQueue();
        }

        /* Change the MIDI file and track that this script controls */
        public void LoadSong(Midi newMidi, int newTrackIdx) {
            midi = newMidi;
            trackIdx = newTrackIdx;
            Setup();
        }

        /* Starts playing the song from the beginning */
        public void Play() {
            Restart();
            isPlaying = true;
        }

        /* Stops playing the song and resets the song to the beginning */
        public void Stop() {
            isPlaying = false;
            Restart();
        }

        /* Stops playing the song but keeps its timestamp */
        public void Pause() {
            isPlaying = false;
        }

        /* Continues playing the song at the current timestamp */
        public void Resume() {
            isPlaying = true;
        }

        /* Progresses the song by deltaTime and adds all notes that appear to the upcomingNotes queue */
        private void AddNotes(float deltaTime) {
            List<MidiEvent> notes = reader.Advance(deltaTime);
            foreach(MidiEvent note in notes) {
                if(CanPressNote(note)) { // Add Press notes
                    upcomingNotes.Enqueue(note);
                    onPressSpawn.Invoke(curTime, note);
                    lastPressTime = note.time;
                }
                else if (CanReleaseNote(note)) { // Add Release notes
                    upcomingNotes.Enqueue(note);
                    onReleaseSpawn.Invoke(curTime, note);
                    lastPressTime = -1;
                }
            }
        }

        /* Remove any notes whose times have already passed from the queue */
        private void UpdateNotesQueue() {
            while(upcomingNotes.Any()) {
                MidiEvent curNote = upcomingNotes.Peek();
                if(curNote.time + IntervalSize(curNote) >= curTime) {
                    break;
                }
                upcomingNotes.Dequeue();
                switch (curNote.eventType) {
                    case MidiEventType.NoteOn:
                        onPressMiss.Invoke(curNote);
                        break;
                    case MidiEventType.NoteOff:
                        onReleaseMiss.Invoke(curNote);
                        break;
                }
            }
        }

        /* Sets up hit detection if applicable */
        private void Start() {
            if(setupOnStart) {
                Setup();
            }
        }

        /* Updates hit detection parameters when hit detection is active */
        private void Update() {
            // Make sure hit detection is active
            if(!isPlaying) {
                return;
            }

            // Add new notes and remove missed notes
            curTime += Time.deltaTime;
            AddNotes(Time.deltaTime);
            UpdateNotesQueue();
            
            // Complete the song if applicable
            if(SongComplete) {
                onSongCompletion.Invoke();
                isPlaying = false;
            }

            // If using the Old Input System, read button inputs
            #if ENABLE_LEGACY_INPUT_MANAGER
            HandleButton();
            #endif // ENABLE_LEGACY_INPUT_MANAGER
        }
    
        /* Handles a button press */
        private void Press() {
            // Make sure hit detection is active
            if(!isPlaying) {
                return;
            }

            // Remove any missed notes
            UpdateNotesQueue();

            // Handle an extra press if there are no notes to hit
            if(!upcomingNotes.Any()) {
                if(CanMissPress) {
                    onExtraPress.Invoke(curTime);
                }
                return;
            }

            // Try to hit the next note
            MidiEvent curNote = upcomingNotes.Peek();
            if(curNote.eventType == MidiEventType.NoteOn && InNoteInterval(curTime, curNote)) {
                // The note could be hit
                upcomingNotes.Dequeue();
                onPressHit.Invoke(curTime, curNote);
            }
            else if(CanMissPress){
                // The note cannot be hit
                onExtraPress.Invoke(curTime);
            }
        }

        /* Handles a button release */
        private void Release() {
            // Make sure hit detection is active
            if(!isPlaying) {
                return;
            }

            // Remove any missed notes
            UpdateNotesQueue();

            // Handle an extra release if there are no notes to hit
            if(!upcomingNotes.Any()) {
                if(CanMissRelease) {
                    onExtraRelease.Invoke(curTime);
                }
                return;
            }

            // Try to hit the next note
            MidiEvent curNote = upcomingNotes.Peek();
            if(curNote.eventType == MidiEventType.NoteOff && InNoteInterval(curTime, curNote)) {
                // The note could be hit
                upcomingNotes.Dequeue();
                onReleaseHit.Invoke(curTime, curNote);
            }
            else if(CanMissRelease){
                // The note cannot be hit
                onExtraRelease.Invoke(curTime);
            }
            else if(inputMode == InputMode.PressAndLongRelease && upcomingNotes.Any() && upcomingNotes.Peek().eventType == MidiEventType.NoteOff) {
                // The note is a held note and the player released too early
                upcomingNotes.Dequeue();
                onReleaseMiss.Invoke(curNote);
            }
        }

        /* The input function for the Old Input System */
        #if ENABLE_LEGACY_INPUT_MANAGER
        private void HandleButton() {
            // Make sure the user actually intends to use the old input system
            if(!usingOldInputSystem) {
                return;
            }

            // Set the current input state
            bool prevButtonState = buttonState;
            buttonState = Input.GetButtonDown(inputName);

            // Do not call input functions if the button state did not change
            if(prevButtonState == buttonState) {
                return;
            }

            // Call the appropriate input function
            if(buttonState) {
                Press();
            }
            else {
                Release();
            }
        }
        #endif // ENABLE_LEGACY_INPUT_MANAGER

        /* The input function for the New Input System */
        #if ENABLE_INPUT_SYSTEM
        public void HandleButton(InputAction.CallbackContext context) {
            // Set the current input state
            bool prevButtonState = buttonState;
            buttonState = context.ReadValueAsButton();

            // Do note call input functions if the button state did not change
            if(prevButtonState == buttonState) {
                return;
            }

            // Call the appropriate input function
            if(buttonState) {
                Press();
            }
            else {
                Release();
            }
        }
        #endif // ENABLE_INPUT_SYSTEM
    }
}

