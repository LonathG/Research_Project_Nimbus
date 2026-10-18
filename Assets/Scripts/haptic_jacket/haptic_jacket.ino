// =========================================================================
// PROJECT NIMBUS: DUAL HAPTIC VEST / JACKET CONTROLLER
// Dedicated Vibration Driver (No Pressure Sensor / Analog Inputs)
// Hardware:
//   - Haptic Motor 1 (PWM) -> Digital Pin 3
//   - Haptic Motor 2 (PWM) -> Digital Pin 5
// Controlled directly via Unity HapticJacketController.cs over Serial (9600 baud)
// =========================================================================

// --- Pin Definitions ---
const int hapticPin1 = 3;  // Haptic Motor 1 (PWM Pin 3)
const int hapticPin2 = 5;  // Haptic Motor 2 (PWM Pin 5)

// --- Haptic State ---
int currentHapticIntensity = 0;

// --- Safety Watchdog ---
// If no packet is received from Unity for 2.5 seconds, automatically silence motors
unsigned long lastPacketTime = 0;
const unsigned long WATCHDOG_TIMEOUT_MS = 2500;

// --- Non-blocking Serial Buffer ---
const byte MAX_CHARS = 64;
char receivedChars[MAX_CHARS];
boolean newData = false;

// --- Periodic Diagnostic Logging ---
unsigned long lastLogTime = 0;
const unsigned long LOG_INTERVAL_MS = 1000;

void setup() {
  // 1. Configure Haptic Output Pins (guaranteed 0 PWM on boot)
  pinMode(hapticPin1, OUTPUT);
  pinMode(hapticPin2, OUTPUT);
  analogWrite(hapticPin1, 0);
  analogWrite(hapticPin2, 0);
  currentHapticIntensity = 0;

  // 2. Initialize Serial Connection
  Serial.begin(9600);
  Serial.println(F("--- HAPTIC JACKET CONTROLLER INITIALIZED ---"));
  Serial.println(F("Hardware: Motor 1 (Pin 3), Motor 2 (Pin 5)"));
  Serial.println(F("Status  : Ready for Unity HapticJacketController.cs"));
  Serial.println(F("Commands: 'HAPTIC:<0-255>', 'w' (max), 's' (stop)"));
  
  lastPacketTime = millis();
}

void loop() {
  // Task 1: Non-blocking Serial Command Listener
  recvWithEndMarker();
  if (newData) {
    parseHapticCommand();
    newData = false;
    lastPacketTime = millis();
  }

  // Task 2: Safety Watchdog (turns off motors if Unity is stopped or serial cable disconnected)
  if (currentHapticIntensity > 0 && (millis() - lastPacketTime > WATCHDOG_TIMEOUT_MS)) {
    currentHapticIntensity = 0;
    analogWrite(hapticPin1, 0);
    analogWrite(hapticPin2, 0);
    Serial.println(F("[WATCHDOG] No signal from Unity. Motors auto-silenced."));
  }

  // Task 3: Periodic Status Heartbeat (Every 1s)
  if (millis() - lastLogTime >= LOG_INTERVAL_MS) {
    lastLogTime = millis();
    Serial.print(F("Haptic_PWM:"));
    Serial.println(currentHapticIntensity);
  }
}

// Non-blocking serial packet receiver (terminated by newline '\n')
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

// Parses incoming commands from Unity or Serial Monitor
void parseHapticCommand() {
  char *ptr = receivedChars;
  while (*ptr == ' ' || *ptr == '\t') ptr++;
  if (*ptr == '\0') return;

  // 1. Protocol from Unity: "HAPTIC:<0-255>" or "MOVE_FWD:<0-255>"
  if (strncasecmp(ptr, "HAPTIC:", 7) == 0) {
    currentHapticIntensity = constrain(atoi(ptr + 7), 0, 255);
  }
  else if (strncasecmp(ptr, "MOVE_FWD:", 9) == 0) {
    currentHapticIntensity = constrain(atoi(ptr + 9), 0, 255);
  }
  // 2. Single Key Debug Commands (from Arduino Serial Monitor)
  else if (ptr[1] == '\0') {
    char key = ptr[0];
    if (key == 'w' || key == 'W') {
      currentHapticIntensity = 255;
      Serial.println(F("[MANUAL] Haptics set to MAX (255 PWM)"));
    }
    else if (key == 's' || key == 'S') {
      currentHapticIntensity = 0;
      Serial.println(F("[MANUAL] Haptics STOPPED (0 PWM)"));
    }
    else if (key == '+') {
      currentHapticIntensity = constrain(currentHapticIntensity + 25, 0, 255);
    }
    else if (key == '-') {
      currentHapticIntensity = constrain(currentHapticIntensity - 25, 0, 255);
    }
    else if (key == '?' || key == 'p' || key == 'P') {
      Serial.print(F("Current PWM: "));
      Serial.println(currentHapticIntensity);
    }
  }
  // 3. Direct numeric value (e.g. "180" or "0")
  else {
    int val = atoi(ptr);
    if (val >= 0 && val <= 255) {
      currentHapticIntensity = val;
    }
  }

  // Apply PWM to both haptic motors
  analogWrite(hapticPin1, currentHapticIntensity);
  analogWrite(hapticPin2, currentHapticIntensity);
}