using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Midity {
    namespace Examples {
        /* Prints every note in each track of the Midi song */
        public class MidiParserTest : MonoBehaviour
        {
            [SerializeField, Tooltip("The MidiParser to test")]
            private Midi parser;

            /* Iterate through all tracks and print all of their notes */
            void Start()
            {
                for(int trackIdx = 0; parser.ContainsTrack(trackIdx); trackIdx++) {
                    MidiTrackReader reader = parser.GetTrack(trackIdx);
                    Debug.Log(string.Join("\n", reader.Advance(float.PositiveInfinity)));
                }
            }
        }
    }
}
