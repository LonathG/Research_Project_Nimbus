const int fanMiddlePin = 9;   // Middle Fan (PWM)
const int fanLeftPin   = 10;  // Left Fan (PWM)
const int fanRightPin  = 11;  // Right Fan (PWM)

// Failsafe configuration
unsigned long lastSerialTime = 0;
const unsigned long SERIAL_TIMEOUT_MS = 1000; // Shut down fans if serial drops for 1s

// Serial buffer setup
const byte MAX_CHARS = 32;
char receivedChars[MAX_CHARS];
boolean newData = false;

void setup() {
  // Ensure pins are driven LOW immediately
  pinMode(fanMiddlePin, OUTPUT);
  pinMode(fanLeftPin, OUTPUT);
  pinMode(fanRightPin, OUTPUT);

  analogWrite(fanMiddlePin, 0);
  analogWrite(fanLeftPin, 0);
  analogWrite(fanRightPin, 0);

  Serial.begin(115200);
}

void loop() {
  recvWithEndMarker();
  
  if (newData) {
    parseAndSetFans();
    newData = false;
    lastSerialTime = millis();
  }

  // Safety Watchdog: Shut down fans if Unity stops sending packets
  if (millis() - lastSerialTime > SERIAL_TIMEOUT_MS) {
    analogWrite(fanMiddlePin, 0);
    analogWrite(fanLeftPin, 0);
    analogWrite(fanRightPin, 0);
  }
}

// Reads serial stream non-blockingly until '\n'
void recvWithEndMarker() {
  static byte ndx = 0;
  char endMarker = '\n';
  char rc;

  while (Serial.available() > 0 && !newData) {
    rc = Serial.read();

    if (rc != endMarker && rc != '\r') {
      receivedChars[ndx] = rc;
      ndx++;
      if (ndx >= MAX_CHARS) {
        ndx = MAX_CHARS - 1;
      }
    } else if (rc == endMarker) {
      receivedChars[ndx] = '\0';
      ndx = 0;
      newData = true;
    }
  }
}

// Parses "left,middle,right" integers
void parseAndSetFans() {
  char *strtokIndx;

  strtokIndx = strtok(receivedChars, ",");
  if (strtokIndx == NULL) return;
  int leftPWM = atoi(strtokIndx);

  strtokIndx = strtok(NULL, ",");
  if (strtokIndx == NULL) return;
  int middlePWM = atoi(strtokIndx);

  strtokIndx = strtok(NULL, ",");
  if (strtokIndx == NULL) return;
  int rightPWM = atoi(strtokIndx);

  analogWrite(fanLeftPin,   constrain(leftPWM, 0, 255));
  analogWrite(fanMiddlePin, constrain(middlePWM, 0, 255));
  analogWrite(fanRightPin,  constrain(rightPWM, 0, 255));
}