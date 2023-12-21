using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Midity {
    namespace Examples {
        public class HitDetectionDemo : MonoBehaviour
        {
            [Header("References to Midity scripts")]
            [SerializeField, Tooltip("The HitDetection component that manages the game")]
            private HitDetection hitDetection;
            [SerializeField, Tooltip("The MidiPlayer that will play this song")]
            private MidiPlayer player;
            [Header("Note spawning settings")]
            [SerializeField, Tooltip("The note prefab to spawn\n\nShould have a MoveNote.cs component attached")]
            private GameObject notePrefab;
            [SerializeField, Tooltip("The Transform that is at the starting position for spawned notes")]
            private Transform noteStart;
            [SerializeField, Tooltip("The Transform that is at the ending position for spawned notes")]
            private Transform noteEnd;
            [SerializeField, Tooltip("The color of a Press note")]
            private Color pressColor;
            [SerializeField, Tooltip("The color of a Release note")]
            private Color releaseColor;
            [Header("Text displays")]
            [SerializeField, Tooltip("The text component that shows the game's instructions")]
            private Text instructionsText;
            [SerializeField, Tooltip("The text component that shows the number of hits and misclicks")]
            private Text statusText;

            /* Internal variables */
            private readonly Queue<GameObject> notes = new Queue<GameObject>();
            private bool isPlaying = false;

            /* Display variables */
            private int noteHits = 0;
            private int displayedNoteHits = -1;
            private int totalNotes = 0;
            private int displayedTotalNotes = -1;
            private int misclicks = 0;
            private int displayedMisclicks = -1;
            
            /* Sets up hit detection, the MIDI player, and the notes queue */
            public void Setup() {
                notes.Clear();
                hitDetection.Setup();
                player.Setup();
                Restart();
            }

            /* Resets everything to starting states */
            public void Restart() {
                // Reset Midity components
                hitDetection.Restart();
                player.Stop();

                // Clear the notes queue
                foreach(GameObject oldNote in notes) {
                    Destroy(oldNote);
                }
                notes.Clear();

                // Stop playing the song
                isPlaying = false;

                // Reset the notes display
                noteHits = 0;
                displayedNoteHits = -1;
                totalNotes = 0;
                displayedTotalNotes = -1;
                misclicks = 0;
                displayedMisclicks = -1;

                // Reset the instructions display
                instructionsText.text = "Press [SPACEBAR] to start the song!";
            }

            /* Starts playing the song with hit detection and MIDI playback */
            public void Play() {
                if(!isPlaying) {
                    Restart();
                    hitDetection.Play();
                    StartCoroutine(PlayMidiSongAfterTime(hitDetection.PreemptionTime - 2f / 60f)); // MIDI player is delayed by 2 frames
                    isPlaying = true;
                    instructionsText.text = "Press [SPACEBAR] when a note reaches the bottom to hit the note!\n\n" + 
                                            "Release [SPACEBAR] when a different colored note releases the bottom to hit the special Release notes!";
                }
            }

            /* When called in a Coroutine, waits `time` seconds and then plays the MIDI player */
            private IEnumerator PlayMidiSongAfterTime(float time) {
                yield return new WaitForSeconds(time);
                player.Play();
            }

            /* A callback function that will take user inputs through the New Input System */
            public void ReadActionInputs(InputAction.CallbackContext context) {
                if(context.ReadValueAsButton()) {
                    Play();
                }
            }

            /* Setup this component when it spawns in */
            private void Start() {
                Setup();
            }
            
            /* Update the notes display each frame */
            private void Update() {
                if(noteHits != displayedNoteHits || totalNotes != displayedTotalNotes || misclicks != displayedMisclicks) {
                    displayedNoteHits = noteHits;
                    displayedTotalNotes = totalNotes;
                    displayedMisclicks = misclicks;
                    statusText.text = "Hits: " + noteHits + "/" + totalNotes + "\nMisclicks: " + misclicks;
                }
            }

            /**** Midity Hit Detection Event Functions *****/

            /* Spawns a note object that will move from noteStart to noteEnd */
            public void SpawnNote(float time, MidiEvent note) {
                GameObject newNote = Instantiate(notePrefab, transform);
                newNote.transform.position = noteStart.position;
                if(newNote.TryGetComponent(out SpriteRenderer renderer)) {
                    renderer.color = note.eventType == MidiEventType.NoteOn ? pressColor : releaseColor;
                }
                if(newNote.TryGetComponent(out MoveNote move)) {
                    move.Setup(hitDetection, noteEnd.position, hitDetection.PreemptionTime + hitDetection.AudioLatency);
                }
                notes.Enqueue(newNote);
            }

            /* Destroys a note object and increases the appropriate counters */
            public void HitNote(float time, MidiEvent note) {
                GameObject hitNote = notes.Dequeue();
                Destroy(hitNote);
                noteHits++;
                totalNotes++;
            }

            /* Destroys a note object and increases the appropriate counters */
            public void MissNote(MidiEvent note) {
                GameObject missedNote = notes.Dequeue();
                Destroy(missedNote);
                totalNotes++;
            }

            /* Increases the misclicks counter */
            public void Misinput(float time) {
                misclicks++;
            }

            /* Resets the program */
            public void CompleteSong() {
                Debug.Log(statusText.text);
                Restart();
            }
        }
    }
}

