// =========================================================================
// VR Wind Simulation System - 3-Fan Dynamic Speed & Direction Controller
// Pins: Left Fan = Pin 10 (PWM), Middle Fan = Pin 9 (PWM), Right Fan = Pin 11 (PWM)
// Default Baud Rate: 115200 | Port: COM12
// =========================================================================

const int fanMiddlePin = 9;   // Middle Fan (Frontal Wind)
const int fanLeftPin   = 10;  // Left Fan (Yaw / Tilt Left Bias)
const int fanRightPin  = 11;  // Right Fan (Yaw / Tilt Right Bias)

// --- Minimum PWM Kick-Start Threshold ---
// Most 12V/5V DC fans stall if PWM < 30-40. When speed > 0, we scale from MIN_FAN_PWM to 255.
const int MIN_FAN_PWM = 35; 

// --- Safety Watchdog Configuration ---
unsigned long lastSerialTime = 0;
const unsigned long SERIAL_TIMEOUT_MS = 1500; // Turn off fans if Unity stops communicating for 1.5s
bool debugMode = false;

// --- Serial Buffer ---
const byte MAX_CHARS = 64;
char receivedChars[MAX_CHARS];
boolean newData = false;

// Current PWM values
int currentLeftPWM   = 0;
int currentMiddlePWM = 0;
int currentRightPWM  = 0;

void setup() {
  // Configure PWM Output Pins
  pinMode(fanMiddlePin, OUTPUT);
  pinMode(fanLeftPin, OUTPUT);
  pinMode(fanRightPin, OUTPUT);

  // Initialize fans OFF
  analogWrite(fanMiddlePin, 0);
  analogWrite(fanLeftPin, 0);
  analogWrite(fanRightPin, 0);

  Serial.begin(9600);
  Serial.println(F("--- VR Rig Fan Controller Initialized on COM12 (115200 Baud) ---"));
  Serial.println(F("Supported Commands:"));
  Serial.println(F("  'L,M,R'       -> Discrete PWM 0-255 (e.g. '120,255,120')"));
  Serial.println(F("  'SPD:<0-100>' -> Speed Percentage (e.g. 'SPD:80')"));
  Serial.println(F("  'W' / 'S'     -> Debug All ON / All OFF"));
  Serial.println(F("  '1','2','3'   -> Individual Fan Tests (Left, Middle, Right)"));
}

void loop() {
  recvWithEndMarker();

  if (newData) {
    parseAndSetFans();
    newData = false;
    lastSerialTime = millis();
  }

  // Safety Watchdog: Shut down fans if Unity communication halts (bypassed in debug mode)
  if (!debugMode && (millis() - lastSerialTime > SERIAL_TIMEOUT_MS)) {
    if (currentLeftPWM > 0 || currentMiddlePWM > 0 || currentRightPWM > 0) {
      applyFanPWM(0, 0, 0);
    }
  }
}

// Non-blocking serial packet receiver (terminated with '\n')
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

// Maps input 0-255 or 0-100 to compensated PWM including minimum start threshold
int mapToFanPWM(int value, bool isPercent) {
  if (value <= 0) return 0;

  int maxIn = isPercent ? 100 : 255;
  value = constrain(value, 0, maxIn);

  // Scale smoothly from MIN_FAN_PWM to 255
  return map(value, 1, maxIn, MIN_FAN_PWM, 255);
}

void applyFanPWM(int left, int middle, int right) {
  currentLeftPWM   = constrain(left, 0, 255);
  currentMiddlePWM = constrain(middle, 0, 255);
  currentRightPWM  = constrain(right, 0, 255);

  analogWrite(fanLeftPin,   currentLeftPWM);
  analogWrite(fanMiddlePin, currentMiddlePWM);
  analogWrite(fanRightPin,  currentRightPWM);
}

// Main parser for Unity packets & debug commands
void parseAndSetFans() {
  // Trim leading whitespace
  char *ptr = receivedChars;
  while (*ptr == ' ' || *ptr == '\t') ptr++;

  if (*ptr == '\0') return;

  // --- 1. Manual Debug Commands ---
  if ((ptr[0] == 'W' || ptr[0] == 'w') && ptr[1] == '\0') {
    debugMode = true;
    applyFanPWM(255, 255, 255);
    Serial.println(F("[DEBUG] ALL fans ON @ 255 PWM. Send 'S' to stop."));
    return;
  }

  if ((ptr[0] == 'S' || ptr[0] == 's') && ptr[1] == '\0') {
    debugMode = false;
    applyFanPWM(0, 0, 0);
    Serial.println(F("[DEBUG] ALL fans STOPPED."));
    return;
  }

  if ((ptr[0] == '1' || ptr[0] == 'L' || ptr[0] == 'l') && ptr[1] == '\0') {
    debugMode = true;
    applyFanPWM(255, 0, 0);
    Serial.println(F("[DEBUG] LEFT fan (Pin 10) only ON."));
    return;
  }

  if ((ptr[0] == '2' || ptr[0] == 'M' || ptr[0] == 'm') && ptr[1] == '\0') {
    debugMode = true;
    applyFanPWM(0, 255, 0);
    Serial.println(F("[DEBUG] MIDDLE fan (Pin 9) only ON."));
    return;
  }

  if ((ptr[0] == '3' || ptr[0] == 'R' || ptr[0] == 'r') && ptr[1] == '\0') {
    debugMode = true;
    applyFanPWM(0, 0, 255);
    Serial.println(F("[DEBUG] RIGHT fan (Pin 11) only ON."));
    return;
  }

  // --- 2. Protocol: "SPD:<0-100>" or "SPD:<0-255>" ---
  if (strncmp(ptr, "SPD:", 4) == 0 || strncmp(ptr, "spd:", 4) == 0 ||
      strncmp(ptr, "FAN:", 4) == 0 || strncmp(ptr, "fan:", 4) == 0) {
    int speedVal = atoi(ptr + 4);
    // If value <= 100, treat as percent; if > 100, treat as direct PWM
    int pwm = (speedVal <= 100) ? mapToFanPWM(speedVal, true) : constrain(speedVal, 0, 255);
    
    debugMode = false;
    applyFanPWM(pwm, pwm, pwm);
    return;
  }

  // --- 3. Protocol: Key-Value Pairs "L:100,M:255,R:100" ---
  if (strstr(ptr, "L:") != NULL || strstr(ptr, "l:") != NULL ||
      strstr(ptr, "M:") != NULL || strstr(ptr, "m:") != NULL ||
      strstr(ptr, "R:") != NULL || strstr(ptr, "r:") != NULL) {
    
    int l = currentLeftPWM, m = currentMiddlePWM, r = currentRightPWM;
    char tempBuf[MAX_CHARS];
    strncpy(tempBuf, ptr, MAX_CHARS - 1);
    tempBuf[MAX_CHARS - 1] = '\0';

    char *token = strtok(tempBuf, ",;");
    while (token != NULL) {
      while (*token == ' ') token++;
      if (token[0] == 'L' || token[0] == 'l') {
        if (token[1] == ':') l = atoi(token + 2);
      } else if (token[0] == 'M' || token[0] == 'm') {
        if (token[1] == ':') m = atoi(token + 2);
      } else if (token[0] == 'R' || token[0] == 'r') {
        if (token[1] == ':') r = atoi(token + 2);
      }
      token = strtok(NULL, ",;");
    }

    debugMode = false;
    applyFanPWM(l, m, r);
    return;
  }

  // --- 4. Protocol: "left,middle,right" CSV format (e.g. "120,255,120") ---
  char *strtokIndx;
  strtokIndx = strtok(ptr, ",");
  if (strtokIndx != NULL) {
    int leftVal = atoi(strtokIndx);

    strtokIndx = strtok(NULL, ",");
    if (strtokIndx != NULL) {
      int middleVal = atoi(strtokIndx);

      strtokIndx = strtok(NULL, ",");
      if (strtokIndx != NULL) {
        int rightVal = atoi(strtokIndx);

        debugMode = false;
        applyFanPWM(leftVal, middleVal, rightVal);
        return;
      }
    } else {
      // Single numeric value sent (e.g. "180") -> apply to all fans
      debugMode = false;
      int singlePWM = constrain(leftVal, 0, 255);
      applyFanPWM(singlePWM, singlePWM, singlePWM);
      return;
    }
  }
}