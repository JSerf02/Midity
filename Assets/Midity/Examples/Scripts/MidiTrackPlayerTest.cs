using System.Collections;
using System.Collections.Generic;
using Midity.MidiPlaying;
using UnityEngine;

namespace Midity {
    namespace MidiPlaying {
        public class MidiTrackPlayerTest : MonoBehaviour {
            [SerializeField, Tooltip("The MidiTrackPlayer to test")]
            private MidiTrackPlayer player;
            [SerializeField, Tooltip("When pressed, the MidiTrackPlayer will start playing the song")]
            private bool playSong;
            [SerializeField, Tooltip("When pressed, the MidiTrackPlayer will stop playing the song")]
            private bool stopSong;
            [SerializeField, Tooltip("When pressed, the MidiTrackPlayer will pause the song")]
            private bool pauseSong;
            [SerializeField, Tooltip("When pressed, the MidiTrackPlayer will resume playing the song")]
            private bool resumeSong;

            // Make sure the player is setup before playing any songs
            void Start()
            {
                player.Setup();
            }

            // Read inspector values as buttons and process them accordingly
            void Update()
            {
                if(playSong) {
                    playSong = false;
                    player.Play();
                }
                if(stopSong) {
                    stopSong = false;
                    player.Stop();
                }
                if(pauseSong) {
                    pauseSong = false;
                    player.Pause();
                }
                if(resumeSong) {
                    resumeSong = false;
                    player.Resume();
                }
            }
        }

    }
}
