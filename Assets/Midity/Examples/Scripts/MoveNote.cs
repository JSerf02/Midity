using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Midity {
    namespace Examples {
        public class MoveNote : MonoBehaviour {
            private HitDetection detection;
            private Vector3 startPosition;
            private Vector3 targetPosition;
            private float moveTime;
            private float curTime = -1;
            public void Setup(HitDetection detection, Vector3 targetPosition, float moveTime) {
                this.detection = detection;
                startPosition = transform.position;
                this.targetPosition = targetPosition;
                this.moveTime = moveTime;
                curTime = 0;
            }

            // Move towards the targetPosition
            void Update() {
                // Do not move if Setup() has not been called yet
                if(curTime < 0) {
                    return;
                }
                if(!detection.IsPlaying) {
                    return;
                }

                /* 
                 * Move linearly from start position in the direction of targetPosition
                 * at a speed that ensures arrival after `moveTime` seconds has passed
                */
                transform.position = Vector3.LerpUnclamped(startPosition, targetPosition, curTime / moveTime);
                curTime += Time.deltaTime;
            }
        }
    }
}

