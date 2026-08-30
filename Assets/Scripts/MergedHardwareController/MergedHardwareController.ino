// =========================================================================
// UNIFIED HARDWARE CONTROLLER (Arduino Uno / Nano / Mega)
// Merged: Dual Pressure Sensor Handle + Dual Haptic Jacket
// Speed increases -> Haptic vibration intensity automatically increases!
// -------------------------------------------------------------------------
// HARDWARE PIN ASSIGNMENTS:
//   Inputs  (Pressure Handle):
//     - FSR 1: Forward Thrust   -> Analog Pin A0
//     - FSR 2: Altitude / Lift  -> Analog Pin A1
//   Outputs (Haptic Jacket):
//     - Haptic Motor 1 (PWM)    -> Digital Pin 3
//     - Haptic Motor 2 (PWM)    -> Digital Pin 5
// -------------------------------------------------------------------------
// Baud Rate: 9600 (Synchronized with Unity BroomFlightController.cs)
// =========================================================================

// --- Pin Definitions ---
const int thrustFsrPin   = A0; // FSR 1: Forward Thrust (Analog A0)
const int altitudeFsrPin = A1; // FSR 2: Altitude / Lift (Analog A1)
const int hapticPin1     = 3;  // Haptic Motor 1 (PWM Pin 3)
const int hapticPin2     = 5;  // Haptic Motor 2 (PWM Pin 5)

// --- Calibration Thresholds (Pressure Handle) ---
const int thrustDeadzone   = 15;  // Below this value is considered resting/zero
const int thrustMax        = 350; // Max expected reading for FSR 1 (0-100% thrust)
const int altitudeDeadzone = 25;  // Below this value is considered resting/zero
const int altitudeMax      = 700; // Max expected reading for FSR 2 (0-100% altitude)

// --- Haptic Tuning ---
// Minimum PWM to overcome motor static friction when speed > 0
const int MIN_HAPTIC_PWM = 45; 
const int MAX_HAPTIC_PWM = 255;
bool autoHapticFromSpeed = true; // Auto-scale vibration with forward squeeze speed

// --- Low-Pass Filter State ---
float smoothThrust   = 0;
float smoothAltitude = 0;

// --- Haptic State ---
int currentHapticIntensity = 0;

// --- Non-blocking Timing & Serial Buffer ---
unsigned long lastSensorTime = 0;
const unsigned long SENSOR_INTERVAL_MS = 25; // 40Hz sensor refresh rate

const byte MAX_CHARS = 64;
char receivedChars[MAX_CHARS];
boolean newData = false;

void setup() {
  // 1. Configure Haptic Output Pins
  pinMode(hapticPin1, OUTPUT);
  pinMode(hapticPin2, OUTPUT);
  analogWrite(hapticPin1, 0);
  analogWrite(hapticPin2, 0);

  // 2. Initialize Serial Connection
  Serial.begin(9600);
  Serial.println(F("--- UNIFIED HARDWARE CONTROLLER INITIALIZED ---"));
  Serial.println(F("Speed -> Dynamic Haptic Scaling: ENABLED"));
  Serial.println(F("Pressure Handle: Thrust(A0), Altitude(A1)"));
  Serial.println(F("Haptic Jacket  : Motor 1(Pin 3), Motor 2(Pin 5)"));
  Serial.println(F("Commands: 'MOVE_FWD:<0-255>', 'AUTO', 'w' (max), 's' (stop), '+', '-'"));
}

void loop() {
  // -----------------------------------------------------------------------
  // Task 1: Haptic Jacket Command Listener (Non-blocking)
  // -----------------------------------------------------------------------
  recvWithEndMarker();
  if (newData) {
    parseHapticCommand();
    newData = false;
  }

  // -----------------------------------------------------------------------
  // Task 2: Pressure Sensor Polling & Telemetry Transmission (Every 25ms)
  // -----------------------------------------------------------------------
  if (millis() - lastSensorTime >= SENSOR_INTERVAL_MS) {
    lastSensorTime = millis();
    readSensorsAndTransmit();
  }
}

// Maps 0-100% speed to a proportional PWM intensity (MIN_HAPTIC_PWM to 255)
int mapSpeedToHapticPWM(int speedPct) {
  if (speedPct <= 0) return 0;
  speedPct = constrain(speedPct, 0, 100);
  return map(speedPct, 1, 100, MIN_HAPTIC_PWM, MAX_HAPTIC_PWM);
}

// Reads FSRs, applies EMA smoothing, maps to 0-100%, updates haptics, and transmits telemetry
void readSensorsAndTransmit() {
  // 1. Read FSR 1 (Thrust on A0)
  int rawThrust = analogRead(thrustFsrPin);

  // 2. Read FSR 2 (Altitude on A1) with ADC settling dummy read
  analogRead(altitudeFsrPin); 
  int rawAltitude = analogRead(altitudeFsrPin);

  // 3. Low-Pass Exponential Moving Average Filter (eliminates noise/jitter)
  smoothThrust   = (smoothThrust * 0.75f) + (rawThrust * 0.25f);
  smoothAltitude = (smoothAltitude * 0.75f) + (rawAltitude * 0.25f);

  // 4. Map smoothed readings to 0-100% with deadzones
  int speedPercent = 0;
  if (smoothThrust > thrustDeadzone) {
    speedPercent = constrain(map((int)smoothThrust, thrustDeadzone, thrustMax, 0, 100), 0, 100);
  }

  int altPercent = 0;
  if (smoothAltitude > altitudeDeadzone) {
    altPercent = constrain(map((int)smoothAltitude, altitudeDeadzone, altitudeMax, 0, 100), 0, 100);
  }

  // 5. DYNAMIC HAPTIC RESPONSE: Scale vibration power as speed increases
  if (autoHapticFromSpeed) {
    currentHapticIntensity = mapSpeedToHapticPWM(speedPercent);
    analogWrite(hapticPin1, currentHapticIntensity);
    analogWrite(hapticPin2, currentHapticIntensity);
  }

  // 6. Print telemetry formatted for Unity BroomFlightController.cs + Serial Plotter
  Serial.print(F("Raw_Thrust:"));
  Serial.print(rawThrust);
  Serial.print(F(",Broom_Speed:"));
  Serial.print(speedPercent);
  Serial.print(F(",Raw_Altitude:"));
  Serial.print(rawAltitude);
  Serial.print(F(",Broom_Altitude:"));
  Serial.print(altPercent);
  Serial.print(F(",Haptic_PWM:"));
  Serial.println(currentHapticIntensity);
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

  // 1. Re-enable automatic speed-to-haptic coupling
  if (strcasecmp(ptr, "AUTO") == 0 || strcasecmp(ptr, "SPEED") == 0) {
    autoHapticFromSpeed = true;
    Serial.println(F("[MODE] Auto Haptic Scaling from Speed ENABLED"));
    return;
  }

  // 2. Protocol: "MOVE_FWD:<0-255>" or "HAPTIC:<0-255>" from Unity
  if (strncasecmp(ptr, "MOVE_FWD:", 9) == 0) {
    autoHapticFromSpeed = false; // Unity explicit override
    int val = atoi(ptr + 9);
    currentHapticIntensity = (val <= 100) ? mapSpeedToHapticPWM(val) : constrain(val, 0, 255);
  }
  else if (strncasecmp(ptr, "HAPTIC:", 7) == 0) {
    autoHapticFromSpeed = false;
    currentHapticIntensity = constrain(atoi(ptr + 7), 0, 255);
  }
  // 3. Single Key Debug Commands (from Arduino Serial Monitor)
  else if (ptr[1] == '\0') {
    char key = ptr[0];
    if (key == 'w' || key == 'W') {
      autoHapticFromSpeed = false;
      currentHapticIntensity = 255;
      Serial.println(F("[MANUAL] Haptics set to MAX (255)"));
    }
    else if (key == 's' || key == 'S') {
      autoHapticFromSpeed = false;
      currentHapticIntensity = 0;
      Serial.println(F("[MANUAL] Haptics STOPPED (0). Send 'AUTO' to resume speed sync."));
    }
    else if (key == '+') {
      autoHapticFromSpeed = false;
      currentHapticIntensity = constrain(currentHapticIntensity + 25, 0, 255);
    }
    else if (key == '-') {
      autoHapticFromSpeed = false;
      currentHapticIntensity = constrain(currentHapticIntensity - 25, 0, 255);
    }
  }
  // 4. Direct numeric value (e.g. "180")
  else {
    int val = atoi(ptr);
    if (val >= 0 && val <= 255) {
      autoHapticFromSpeed = false;
      currentHapticIntensity = val;
    }
  }

  // Apply PWM to both haptic motors
  analogWrite(hapticPin1, currentHapticIntensity);
  analogWrite(hapticPin2, currentHapticIntensity);
}

