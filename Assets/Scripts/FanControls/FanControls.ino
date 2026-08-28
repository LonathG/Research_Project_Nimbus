const int fanMiddlePin = 9;   // Middle Fan (PWM)
const int fanLeftPin   = 10;  // Left Fan (PWM)
const int fanRightPin  = 11;  // Right Fan (PWM)

// Failsafe configuration
unsigned long lastSerialTime = 0;
const unsigned long SERIAL_TIMEOUT_MS = 1000; // Shut down fans if serial drops for 1s
bool debugMode = false;

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

  // Safety Watchdog: Shut down fans if Unity stops sending packets (bypassed during manual debug mode)
  if (!debugMode && (millis() - lastSerialTime > SERIAL_TIMEOUT_MS)) {
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

// Parses "left,middle,right" integers or debug commands
void parseAndSetFans() {
  // Debug Command: 'W' or 'w' -> Turn ON all fans to full power (255)
  if ((receivedChars[0] == 'W' || receivedChars[0] == 'w') && receivedChars[1] == '\0') {
    debugMode = true;
    analogWrite(fanLeftPin, 255);
    analogWrite(fanMiddlePin, 255);
    analogWrite(fanRightPin, 255);
    Serial.println(F("[DEBUG] ALL fans ON (PWM: 255) [L:10, M:9, R:11]. Send 'S' to stop."));
    return;
  }

  // Debug Command: 'S' or 's' -> Turn OFF all fans
  if ((receivedChars[0] == 'S' || receivedChars[0] == 's') && receivedChars[1] == '\0') {
    debugMode = false;
    analogWrite(fanLeftPin, 0);
    analogWrite(fanMiddlePin, 0);
    analogWrite(fanRightPin, 0);
    Serial.println(F("[DEBUG] ALL fans STOPPED."));
    return;
  }

  // Individual Fan Test: '1' or 'L' -> Left fan only (Pin 10)
  if ((receivedChars[0] == '1' || receivedChars[0] == 'L' || receivedChars[0] == 'l') && receivedChars[1] == '\0') {
    debugMode = true;
    analogWrite(fanLeftPin, 255);
    analogWrite(fanMiddlePin, 0);
    analogWrite(fanRightPin, 0);
    Serial.println(F("[DEBUG] LEFT fan only ON (Pin 10 = 255)."));
    return;
  }

  // Individual Fan Test: '2' or 'M' -> Middle fan only (Pin 9)
  if ((receivedChars[0] == '2' || receivedChars[0] == 'M' || receivedChars[0] == 'm') && receivedChars[1] == '\0') {
    debugMode = true;
    analogWrite(fanLeftPin, 0);
    analogWrite(fanMiddlePin, 255);
    analogWrite(fanRightPin, 0);
    Serial.println(F("[DEBUG] MIDDLE fan only ON (Pin 9 = 255)."));
    return;
  }

  // Individual Fan Test: '3' or 'R' -> Right fan only (Pin 11)
  if ((receivedChars[0] == '3' || receivedChars[0] == 'R' || receivedChars[0] == 'r') && receivedChars[1] == '\0') {
    debugMode = true;
    analogWrite(fanLeftPin, 0);
    analogWrite(fanMiddlePin, 0);
    analogWrite(fanRightPin, 255);
    Serial.println(F("[DEBUG] RIGHT fan only ON (Pin 11 = 255)."));
    return;
  }

  // Normal Unity packet: "left,middle,right"
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

  debugMode = false;
  analogWrite(fanLeftPin,   constrain(leftPWM, 0, 255));
  analogWrite(fanMiddlePin, constrain(middlePWM, 0, 255));
  analogWrite(fanRightPin,  constrain(rightPWM, 0, 255));
}