/* 
 * Implementation of this script is based on the simple MIDI parser SmfLite.cs
 * by Keijiro Takahashi.
 *
 * GitHub link for SmfLite.cs: https://github.com/keijiro/smflite
 * Copyright information for SmfLite.cs below 
 */
//
// SmfLite.cs - A minimal toolkit for handling standard MIDI files (SMF) on Unity
//
// Copyright (C) 2013 Keijiro Takahashi
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using UnityEngine;

namespace Midity {
    
    /*
    * Sets up and manages each of the tracks of a MIDI file
    */
    public class Midi : MonoBehaviour {
        [Header("Settings")]
        [Tooltip("The MIDI file that you want to parse\n\n" + 
                "To load this file, make sure to change the extension from .mid to .bytes\n" + 
                "Ex: MySong.mid => MySong.bytes or MySong.mid.bytes if you prefer")]
        public TextAsset midiFile;
        [SerializeField, Tooltip("Multiplies the tempo of the song by this amount\n\n" + 
                                 "Leave as 1 to keep the song at its default speed\n\n" + 
                                 "Note: This value only updates the internal time changes when the game starts, when a MidiTrackReader linked to this MIDI file is `Restart()`ed, or when the `SetTempoMultiplier()` or `ChangeTempoMultiplier(float newMultiplier)` functions are called")]
        private float tempoMultiplier = 1;
        private float setTempoMultiplier = -1;
        /* All of the tempo changes in the song as listed in the MIDI file */
        private readonly List<TempoMarker> baseTimeChanges = new List<TempoMarker>();
        /* All of the tempo changes in the song scaled by tempoMultiplier*/
        [System.NonSerialized]
        public readonly List<TempoMarker> timeChanges = new List<TempoMarker>();
        /* The number of MIDI event pulses that make a single quarter note beat */
        [System.NonSerialized]
        public int pulsesPerBeat = 200;
        /* A reader for each MIDI track in the MIDI file */
        [System.NonSerialized]
        private MidiTrackReader[] trackReaders;
        /* The slowest tempo in the song */
        [System.NonSerialized]
        private float slowestTempo;
        public float SlowestTempo {
            get {
                return slowestTempo;
            }
        }

        /* Returns whether the reader has a MIDI track of the specified index */
        public bool ContainsTrack(int trackIdx) {
            return trackIdx < trackReaders.Length;
        }

        /* Returns the reader for the specified MIDI track */
        public MidiTrackReader GetTrack(int trackIdx) {
            if(!ContainsTrack(trackIdx)) {
                throw new System.FormatException("Track index not accessible!");
            }
            return trackReaders[trackIdx];
        }

        /* Returns the reader for the trackIdx-th MIDI track that contains at least one note event */
        public MidiTrackReader GetTrackWithNotes(int trackIdx) {
            int curIdx = 0;
            for(int actualIdx = 0; curIdx <= trackIdx; actualIdx++) {
                MidiTrackReader curReader = GetTrack(actualIdx);
                if(!curReader.ContainsNotes()) {
                    continue;
                }
                if(curIdx == trackIdx) {
                    return curReader;
                }
                curIdx++;
            }
            throw new System.FormatException("Track index not accessible!");
        }

        /* Setup the parser on Awake */
        private void Awake() {
            Setup();
        }

        /* Parses the header of the MIDI file, sets up track readers, and parses the first track for tempo changes */
        // For more info, check out the Standard MIDI File specifications here: http://www.music.mcgill.ca/~ich/classes/mumt306/StandardMIDIfileformat.html
        public void Setup() {
            ByteStreamReader reader = new ByteStreamReader(midiFile.bytes);

            // Make sure the stream starts with the proper header
            char[] header = reader.ReadChars(4);
            if(new string(header) != "MThd") {
                throw new System.FormatException("Cannot find header chunk!");
            }

            // Make sure the header has the correct length
            int headerLength = reader.ReadInt32();
            if(headerLength != 6) {
                throw new System.FormatException("The length of the header chunk for supported MIDI file formats must be 6!");
            }

            // The next 2 bytes are unused in supported MIDI file formats
            reader.Advance(2);

            // Parse the number of tracks
            int numTracks = reader.ReadInt16();

            // Parse the number of pulses in each beat and ensure the time format is correct
            pulsesPerBeat = reader.ReadInt16();
            if((pulsesPerBeat & 0x8000) != 0) {
                throw new System.FormatException("SMPTE time format is not supported!"); // See MIDI file specification linked above for explanation on SMPTE time format
            }

            // Setup the track readers and the tempo changes
            SetupTrackReaders(reader, numTracks);
            SetupTempoChanges();
        }

        /* Creates a MidiTrackReader for each track in the MIDI file */
        // Note that the Reader parameter must be a reader whose index points to the start of the first track!
        private void SetupTrackReaders(ByteStreamReader reader, int numTracks) {
            trackReaders = new MidiTrackReader[numTracks];
            for(int i = 0; i < numTracks; i++) {
                // Create a new track reader for the current track
                trackReaders[i] = new MidiTrackReader(reader.Clone(), this);

                // Advance the reader until it goes past the End of Track Meta Event 
                bool advanceStatus = reader.AdvancePastByteSequence(new byte[]{0xFF, 0x2F, 0x00}); 
                if(!advanceStatus) {
                    throw new System.FormatException("Track parsed past end of file without reaching an End Track event!"); 
                }
            }
        }
        
        /* Iterates through the first track and stores tempo change data */
        // Note that this can only be called after the track readers are setup
        private void SetupTempoChanges() {
            if(trackReaders == null || trackReaders.Length == 0) {
                throw new System.FormatException("Cannot parse for tempo changes when there are no tracks configured!\n\nMake sure the MIDI file contains at least one track and make sure this function is called after SetupTrackReaders()."); 
            }
            ByteStreamReader reader = trackReaders[0].baseReader.Clone();

            int curPulse = 0;
            while(true) {
                // Get the delta time for the next note
                int deltaPulse = reader.ReadMultiByteValue();
                curPulse += deltaPulse;

                // Read the next event type, or use the last one if no event is specified
                byte rawEvent = reader.ReadByte();

                if (rawEvent == 0xFF) { // Process Meta Events
                    byte metaEventType = reader.ReadByte();
                    if(metaEventType == 0x51) { // Tempo Change Event
                        // Make sure the event has the correct length
                        int tempoLength = reader.ReadByte();
                        if(tempoLength != 3) {
                            throw new System.FormatException("Tempo Change Event has incorrect length!");
                        }

                        // Read the current tempo, which consists of 3 bytes of data
                        int microsPerBeat = 0 | reader.ReadByte();
                        microsPerBeat <<= 8;
                        microsPerBeat |= reader.ReadByte();
                        microsPerBeat <<= 8;
                        microsPerBeat |= reader.ReadByte();

                        // Convert microseconds to seconds
                        float secondsPerBeat = microsPerBeat / Mathf.Pow(10, 6);

                        // Add the current tempo to the list
                        TempoMarker curTempo;
                        if(baseTimeChanges.Count == 0) {
                            // Add a starting tempo of 120BPM (per MIDI standard) if no starting tempo is provided
                            if(curPulse != 0) {
                                TempoMarker startTempo = new TempoMarker(0, 0.5f, pulsesPerBeat);
                                curTempo = new TempoMarker(curPulse, secondsPerBeat, pulsesPerBeat, startTempo);
                                baseTimeChanges.Add(startTempo);
                            }
                            // Add current tempo as starting tempo
                            else {
                                curTempo = new TempoMarker(curPulse, secondsPerBeat, pulsesPerBeat);
                            }
                        }
                        // Add current tempo based on previous tempo
                        else {
                            TempoMarker prev = baseTimeChanges.Last();
                            curTempo = new TempoMarker(curPulse, secondsPerBeat, pulsesPerBeat, prev);
                        }
                        baseTimeChanges.Add(curTempo);
                    }
                    else if(metaEventType == 0x2F) { // End of Song Event
                        break;
                    }
                    else { // All other Meta Events not supported
                        int eventLength = reader.ReadMultiByteValue();
                        reader.Advance(eventLength);
                    }
                } 
                else if (rawEvent == 0xF0) { // Advance past SysEx Message (System Exclusive Message) 
                    while (reader.ReadByte() != 0xF7) { } // SysEx messages always end with byte 0xF7
                } 
                else { // Advance past MIDI Events
                    // MIDI Events that begin with 0b110 have only 2 bytes (including the event) while all others have 3
                    reader.Advance((rawEvent & 0xE0) == 0xC0 ? 1 : 2); 
                }
            }

            // Make sure the tempo is set correctly
            SetTempoMultiplier();
        }
        
        /* Makes sure the tempo changes in `timeChanges` are scaled by `tempoMultiplier` */
        public void SetTempoMultiplier() {
            if(baseTimeChanges.Count == 0) {
                return;
            }
            if(tempoMultiplier <= 0) {
                throw new System.FormatException("Tempo multiplier must be positive!");
            }
            if(setTempoMultiplier == tempoMultiplier) {
                return;
            }
            timeChanges.Clear();
            float prevTimeOriginal = 0;
            float prevTimeNew = 0;
            foreach (TempoMarker tempoMarker in baseTimeChanges) {
                float newTime = prevTimeNew + (tempoMarker.time - prevTimeOriginal) / tempoMultiplier;
                prevTimeOriginal = tempoMarker.time;
                prevTimeNew = newTime;
                TempoMarker newMarker = new TempoMarker(tempoMarker);
                newMarker.secondsPerBeat /= tempoMultiplier;
                newMarker.time = newTime;
                timeChanges.Add(newMarker);
            }
            slowestTempo = SearchMaxSecondsPerBeat();
            setTempoMultiplier = tempoMultiplier;
        }

        /* Update the tempo multiplier */
        public void ChangeTempoMultiplier(float newMultiplier) {
            tempoMultiplier = newMultiplier;
            SetTempoMultiplier();
        }

        /* Returns the index of the tempo at the current time in the song */
        public int TempoIndexAtTime(float time) {
            int tempoIdx = 0;
            while(tempoIdx < timeChanges.Count && timeChanges[tempoIdx].time < time) {
                tempoIdx++;
            }
            if(tempoIdx >= timeChanges.Count || timeChanges[tempoIdx].time > time) {
                tempoIdx--;
            }
            return tempoIdx;
        }

        /* Returns the tempo at the current time in the song */
        public float TempoAtTime(float time) {
            return timeChanges[TempoIndexAtTime(time)].secondsPerBeat;
        }

        /* Performs a linear search on the tempos to find the smallest tempo */
        private float SearchMaxSecondsPerBeat() {
            if(timeChanges.Count <= 0) {
                throw new System.FormatException("Attempted to find the minimum tempo when no tempos exist!");
            }
            float max = timeChanges[0].time;
            foreach(TempoMarker cur in timeChanges) {
                if(cur.secondsPerBeat > max) {
                    max = cur.secondsPerBeat;
                }
            }
            return max;
        }
    }
    
    /* 
        * Provides an easy interface for reading notes of a MIDI track
    */
    public class MidiTrackReader {
        public ByteStreamReader baseReader;
        private readonly Midi midi;
        private readonly int length = 0;
        private int EndIdx {
            get {
                return length + baseReader.Index;
            }
        }
        private ByteStreamReader curReader;
        private int curPulse = 0; // The most recent pulse with an event
        private float curPulseTime = 0; // The time of the most recent pulse
        private float curTime = 0; // The current parsing time
        private int curTempoIdx = 0;
        private bool songComplete = false;
        public bool SongComplete {
            get {
                return songComplete;
            }
        }
        public float CurTempo {
            get {
                return midi.timeChanges[curTempoIdx].secondsPerBeat;
            }
        }
        private int NextTempoPulse {
            get {
                if(curTempoIdx + 1 >= midi.timeChanges.Count) {
                    return -1;
                }
                return midi.timeChanges[curTempoIdx + 1].pulse;
            }
        }

        public MidiTrackReader(ByteStreamReader trackStart, Midi midi) {
            // Make sure the inputted byte reader points to a track
            string header = new string(trackStart.ReadChars(4));
            if (header != "MTrk") {
                throw new System.FormatException("Cannot find track chunk!");
            }
            
            // Store the reader that points to the start of the track, the parser, and the length
            baseReader = trackStart;
            this.midi = midi;
            length = baseReader.ReadInt32();
            
            // Setup the current reader
            Restart();
        }

        /* A special constructor only for use by the Clone function */
        private MidiTrackReader(ByteStreamReader baseReader, Midi midi, int length) {
            // Store header values from the previous MidiTrackReader
            this.baseReader = baseReader.Clone();
            this.midi = midi;
            this.length = length;

            // Setup the current reader
            Restart();
        }

        /* Creates a new MidiTrackReader for the same track */
        public MidiTrackReader Clone() {
            return new MidiTrackReader(baseReader, midi, length);
        }

        /* Resets the current reader and associated parameters to the start of the track */
        public void Restart() {
            curReader = baseReader.Clone();
            curPulse = 0;
            curPulseTime = 0;
            curTime = 0;
            curTempoIdx = 0;
            songComplete = false;

            // Update the song's tempo if the multiplier was changed
            midi.SetTempoMultiplier();
        }
        

        /* Returns all MIDI events that occur up until `time` seconds has passed */
        // For more info, check out the Standard MIDI File specifications here: http://www.music.mcgill.ca/~ich/classes/mumt306/StandardMIDIfileformat.html
        public List<MidiEvent> Advance(float time) {
            List<MidiEvent> results = new();
            float endTime = curTime + time; // The target time of the song
            byte rawEvent = 0; // The byte representing the MIDI Event of the current note
            while(!songComplete) {
                // Get the delta time for the next note
                int deltaPulse = curReader.PeekMultiByteValue();
                float deltaTime = DeltaPulseToDeltaTime(deltaPulse);

                // Make sure the next note doesn't start after the target time
                if(curPulseTime + deltaTime > endTime) {
                    break;
                }

                // Update the time
                curPulseTime += deltaTime;
                curPulse += deltaPulse;
                curTempoIdx = midi.TempoIndexAtTime(curPulseTime);

                // Skip past bytes already peaked
                curReader.ReadMultiByteValue();

                // Read the next event type, or use the last one if no event is specified
                if ((curReader.PeekByte() & 0x80) != 0) { // Every event in a Standard MIDI File begins with a 1 at the most significant bit
                    rawEvent = curReader.ReadByte();
                }

                if (rawEvent == 0xFF) { // Meta Event
                    byte metaEventType = curReader.ReadByte();
                    if(metaEventType == 0x2F) { // End of Song
                        songComplete = true;
                    }
                    else { // All other Meta Events not supported
                        int eventLength = curReader.ReadMultiByteValue();
                        curReader.Advance(eventLength);
                    }
                } 
                else if (rawEvent == 0xF0) { // SysEx Message (System Exclusive Message) - Not supported
                    while (curReader.ReadByte() != 0xF7) { } // SysEx messages always end with byte 0xF7
                } 
                else { // MIDI Event
                    // The first nibble of the first byte of a MIDI event is the status
                    // - Note that the second nibble of this byte is the MIDI channel, 
                    //   but this program does not suppose different MIDI channels
                    byte status = (byte) (rawEvent & 0xF0); 
                    MidiEventType eventType;
                    if(status == 0x90) { // 0b1001 is the Note On status
                        eventType = MidiEventType.NoteOn;
                    }
                    else if (status == 0x80) { // 0b1000 is the Note Off status
                        eventType = MidiEventType.NoteOff;
                    }
                    else { // Other statuses are not supported
                        curReader.Advance();
                        // All event statuses that do not begin with 0b110 have 
                        // an additional byte of data that we must skip
                        if((rawEvent & 0xE0) != 0xC0) { 
                            curReader.Advance();
                        }
                        continue;
                    }

                    int note = curReader.ReadByte();
                    int velocity = curReader.ReadByte();
                    
                    results.Add(new MidiEvent(eventType, curPulseTime, note, velocity));
                }
            }
            curTime = endTime;
            return results;
        }
        
        /* Returns whether there is at least one Note On or Note Off event in the song */
        public bool ContainsNotes() {
            byte rawEvent = 0; // The byte representing the MIDI Event of the current note
            bool res = false;
            while(!songComplete) {

                // Skip past delta time data
                curReader.ReadMultiByteValue();

                // Read the next event type if applicable
                if ((curReader.PeekByte() & 0x80) != 0) { // Every event in a Standard MIDI File begins with a 1 at the most significant bit
                    rawEvent = curReader.ReadByte();
                }

                // Check the event
                if (rawEvent == 0xFF) { // Meta Event
                    byte metaEventType = curReader.ReadByte();
                    if(metaEventType == 0x2F) { // End of Song
                        songComplete = true;
                    }
                    else { // Skip past other meta events
                        int eventLength = curReader.ReadMultiByteValue();
                        curReader.Advance(eventLength);
                    }
                } 
                else if (rawEvent == 0xF0) { // Skip past SysEx messages (System Exclusive messages)
                    while (curReader.ReadByte() != 0xF7) { } // SysEx messages always end with byte 0xF7
                } 
                else { // MIDI Event
                    // The first nibble of the first byte of a MIDI event is the status
                    // - Note that the second nibble of this byte is the MIDI channel, 
                    //   but this program does not suppose different MIDI channels
                    byte status = (byte) (rawEvent & 0xF0); 
                    if(status == 0x90 || status == 0x80) { // Note On and Note Off statuses
                        res = true;
                        break;
                    }
                    else { // Skip past other statuses
                        curReader.Advance();
                        // All event statuses that do not begin with 0b110 have 
                        // an additional byte of data that we must skip
                        if((rawEvent & 0xE0) != 0xC0) { 
                            curReader.Advance();
                        }
                    }
                }
            }
            Restart();
            return res;
        }

        /* Returns whether there are notes in the track that overlap */
        public bool ContainsPolyphony() {
            byte prevStatus = 0; // The status (on or off) of the most recent MIDI note that was parsed
            byte rawEvent = 0; // The byte representing the MIDI Event of the current note
            bool res = false;
            while(!songComplete) {
                // Skip past delta time data
                curReader.ReadMultiByteValue();

                // Read the next event type if applicable
                if ((curReader.PeekByte() & 0x80) != 0) { // Every event in a Standard MIDI File begins with a 1 at the most significant bit
                    rawEvent = curReader.ReadByte();
                }

                // Check the event
                if (rawEvent == 0xFF) { // Meta Event
                    byte metaEventType = curReader.ReadByte();
                    if(metaEventType == 0x2F) { // End of Song
                        songComplete = true;
                    }
                    else { // Skip past other meta events
                        int eventLength = curReader.ReadMultiByteValue();
                        curReader.Advance(eventLength);
                    }
                } 
                else if (rawEvent == 0xF0) { // Skip past SysEx messages (System Exclusive messages)
                    while (curReader.ReadByte() != 0xF7) { } // SysEx messages always end with byte 0xF7
                } 
                else { // MIDI Event
                    // The first nibble of the first byte of a MIDI event is the status
                    // - Note that the second nibble of this byte is the MIDI channel, 
                    //   but this program does not suppose different MIDI channels
                    byte status = (byte) (rawEvent & 0xF0); 
                    if(status == 0x90 || status == 0x80) { // Note On and Note Off statuses
                        if(prevStatus != 0 && prevStatus == status) {
                            return true;
                        }
                        prevStatus = status;
                        break;
                    }
                    else { // Skip past other statuses
                        curReader.Advance();
                        // All event statuses that do not begin with 0b110 have 
                        // an additional byte of data that we must skip
                        if((rawEvent & 0xE0) != 0xC0) { 
                            curReader.Advance();
                        }
                    }
                }
            }
            Restart();
            return res;
        }
        

        /* Uses tempo data to determine how long, in seconds, the inputted number of pulses will take */
        private float DeltaPulseToDeltaTime(int deltaPulse) {
            // Store the tempo index before this algorithm so it can be reset at the end
            int startTempoIdx = curTempoIdx;
            curTempoIdx = midi.TempoIndexAtTime(curPulseTime);
            // The time computed by the algorithm 
            float resultTime = 0; 

            // Iterate until times have accumulated for all pulses up to curPulse + deltaPulse
            int lastAccumedPulse = curPulse; // The largest pulse that has been accumulated so far
            int endPulse = curPulse + deltaPulse; // The desired largest pulse to accumulate
            while(lastAccumedPulse < endPulse) {
                // Set the current target to the desired ending pulse or the 
                // nearest tempo change if one is sooner
                int targetPulse = endPulse;
                if(NextTempoPulse >= 0) {
                    targetPulse = Mathf.Min(targetPulse, NextTempoPulse);
                }
                
                // new time = [pulses] * [beats/pulse * [seconds/beat]
                resultTime += (float) (targetPulse - lastAccumedPulse) / midi.pulsesPerBeat * CurTempo;
                
                // Store that we have accumulated the time for the target pulse
                lastAccumedPulse = targetPulse;
                if(lastAccumedPulse >= NextTempoPulse) {
                    curTempoIdx++;
                }
            }

            // Reset the tempo and return the resulting time
            curTempoIdx = startTempoIdx;
            return resultTime;
        }
    }

    public struct TempoMarker {
        public int pulse; // The MIDI pulse of hte song where this tempo begins
        public float time; // The time, in seconds, of the song where this tempo begins
        public float secondsPerBeat; // The new speed of the song, measured in [seconds/beat]
        public TempoMarker(int pulse, float secondsPerBeat, int pulsesPerBeat) {
            this.pulse = pulse;
            this.secondsPerBeat = secondsPerBeat;
            time = 0;
        }
        public TempoMarker(int pulse, float secondsPerBeat, int pulsesPerBeat, TempoMarker prev) {
            this.pulse = pulse;
            this.secondsPerBeat = secondsPerBeat;
            time = prev.time + (float) (pulse - prev.pulse) / pulsesPerBeat * prev.secondsPerBeat;
        }
        public TempoMarker(TempoMarker source) {
            pulse = source.pulse;
            time = source.time;
            secondsPerBeat = source.secondsPerBeat;
        }
        public override string ToString() {
            return pulse + ": " + secondsPerBeat + " s/beat";
        }
    }

    public enum MidiEventType {
        NoteOn,
        NoteOff
    }
    public struct MidiEvent {
        public MidiEventType eventType; // 
        public float time; // The time of the note in the song, in seconds
        public int note; // MIDI note value (60 is middle c)
        public int velocity; // The MIDI velocity of the note
        public MidiEvent(MidiEventType eventType, float time, int note, int velocity) {
            this.eventType = eventType;
            this.time = time;
            this.note = note;
            this.velocity = velocity;
        }

        public override string ToString() {
            return time + ": " + note + " " + (eventType == MidiEventType.NoteOn ? "On" : "Off") + " at " + velocity + "%";
        }
    }

    
    /* 
    * Reads individual bytes of a byte array
    *
    * Note: This is a slightly modified version of the class "MidiDataStreamReader"
    *       from SmfLite.cs
    */
    public struct ByteStreamReader {
        private readonly byte[] data;
        private int index;
        public readonly int Index {
            get {
                return index;
            }
        }

        public ByteStreamReader(byte[] data, int startIdx = 0) {
            this.data = data;
            index = startIdx;
        }

        /* Creates a new ByteStreamReader with the same internal parameters as this one */
        public readonly ByteStreamReader Clone() {
            return new ByteStreamReader(data, index);
        }
        
        /* Moves the stream past `length` bytes without processing them */
        public void Advance(int length = 1) {
            index += length;
        }
        
        /* Looks at the current byte without advancing the stream */
        public readonly byte PeekByte() {
            return data[index];
        }

        /* Looks at a future byte without advancing the stream */
        public readonly byte PeekByte(int offset) {
            return data[index + offset];
        }

        /* Returns the current byte and advances the stream to the next byte */
        public byte ReadByte() {
            return data[index++];
        }

        /* Reads the next `length` bytes as chars */
        public char[] ReadChars(int length = 1) {
            var temp = new char[length];
            for (var i = 0; i < length; i++) {
                temp[i] = (char) ReadByte();
            }
            return temp;
        }

        /* Reads the next 4 bytes as an int32 */
        public int ReadInt32() {
            int b1 = ReadByte();
            int b2 = ReadByte();
            int b3 = ReadByte();
            int b4 = ReadByte();
            return b4 + (b3 << 8) + (b2 << 16) + (b1 << 24);
        }
        
        /* Reads the next 2 bytes as an int16 */
        public int ReadInt16() {
            int b1 = ReadByte ();
            int b2 = ReadByte ();
            return b2 + (b1 << 8);
        }

        /* Reads multiple bytes using the method described in the MIDI standard */
        public int ReadMultiByteValue() {
            int value = 0;
            while (true) {
                int b = ReadByte();
                value += b & 0x7F;
                if (b < 0x80) {
                    break;
                }
                value <<= 7;
            }
            return value;
        }
        
        /* Looks at multiple bytes usin ghte method described in the MIDI standard, but does not advance the stream */
        public readonly int PeekMultiByteValue() {
            int value = 0;
            int tempIdx = index;
            while (true) {
                int b = data[tempIdx++];
                value += b & 0x7F;
                if (b < 0x80) {
                    break;
                }
                value <<= 7;
            }
            return value;
        }

        /* Continues the stream until it goes past a byte that is equal to the `target` parameter */
        public bool AdvancePastByte(byte target) {
            while(index < data.Length) {
                if(ReadByte() == target) {
                    return true;
                }
            }
            return false;
        }

        /* Continues the stream until it goes past a sequence of bytes that are equal and in the same order as the bytes in the `bytes` input array */
        public bool AdvancePastByteSequence(byte[] bytes) {
            while(index + bytes.Length - 1 < data.Length) {
                bool bytesMatch = true;
                for(int i = 0; i < bytes.Length; i++) {
                    if(PeekByte(i) != bytes[i]) {
                        bytesMatch = false;
                        break;
                    }
                }
                if(bytesMatch) {
                    Advance(bytes.Length);
                    return true;
                }
                Advance();
            }
            return false;
        }
    }
}