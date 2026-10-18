// Component 01: Motion Rendering - Quad Pressure Sensor Handle (Thrust, Altitude, Left Turn, Right Turn)
// Pure FSR sensor controller (No haptic motor load on USB 5V rail)

const int thrustFsrPin    = A0; // FSR 1: Forward Thrust (Pin A0)
const int altitudeFsrPin  = A1; // FSR 2: Altitude / Lift (Pin A1)
const int leftTurnFsrPin  = A2; // FSR 3: Left Turn (Pin A2)
const int rightTurnFsrPin = A3; // FSR 4: Right Turn (Pin A3)

// Calibration thresholds (Adjust based on your squeeze intensity)
const int thrustDeadzone = 250;    // Below this value is considered resting/zero
const int thrustMax = 875;        // Max expected reading for FSR 1 (e.g. 100-500)

const int altitudeDeadzone = 270;  // Below this value is considered resting/zero
const int altitudeMax = 500;      // Max expected reading for FSR 2 (e.g. 700)

const int leftTurnDeadzone = 15;  // Below this value is considered resting/zero

const int leftTurnMax = 350;      // Max expected reading for FSR 3

const int rightTurnDeadzone = 650; // Below this value is considered resting/zero
const int rightTurnMax = 850;     // Max expected reading for FSR 4

// Exponential Moving Average filter values for hardware noise suppression
float smoothThrust = 0;
float smoothAltitude = 0;
float smoothLeftTurn = 0;
float smoothRightTurn = 0;

void setup() {
  Serial.begin(115200); // 115200 baud (matching BroomFlightController default)
  Serial.println(F("--- QUAD FSR MODE: THRUST (A0), ALTITUDE (A1), LEFT TURN (A2), RIGHT TURN (A3) ---"));
}

void loop() {
  // 1. Read FSRs with ADC settling dummy reads to prevent channel crosstalk
  analogRead(thrustFsrPin); // dummy read to settle ADC multiplexer
  int rawThrust = analogRead(thrustFsrPin);

  analogRead(altitudeFsrPin); // dummy read to settle ADC multiplexer
  int rawAltitude = analogRead(altitudeFsrPin);

  analogRead(leftTurnFsrPin); // dummy read to settle ADC multiplexer
  int rawLeftTurn = analogRead(leftTurnFsrPin);

  analogRead(rightTurnFsrPin); // dummy read to settle ADC multiplexer
  int rawRightTurn = analogRead(rightTurnFsrPin);

  // 2. Low-Pass Exponential Filter (Smooths out jitter/jumping effect)
  smoothThrust    = (smoothThrust * 0.75f) + (rawThrust * 0.25f);
  smoothAltitude  = (smoothAltitude * 0.75f) + (rawAltitude * 0.25f);
  smoothLeftTurn  = (smoothLeftTurn * 0.75f) + (rawLeftTurn * 0.25f);
  smoothRightTurn = (smoothRightTurn * 0.75f) + (rawRightTurn * 0.25f);

  // 3. Map smoothed readings to 0-100% with deadzone and constrain
  int speedPercent = 0;
  if (smoothThrust > thrustDeadzone) {
    speedPercent = constrain(map((int)smoothThrust, thrustDeadzone, thrustMax, 0, 100), 0, 100);
  }

  int altPercent = 0;
  if (smoothAltitude > altitudeDeadzone) {
    altPercent = constrain(map((int)smoothAltitude, altitudeDeadzone, altitudeMax, 0, 100), 0, 100);
  }

  int leftTurnPercent = 0;
  if (smoothLeftTurn > leftTurnDeadzone) {
    leftTurnPercent = constrain(map((int)smoothLeftTurn, leftTurnDeadzone, leftTurnMax, 0, 100), 0, 100);
  }

  int rightTurnPercent = 0;
  if (smoothRightTurn > rightTurnDeadzone) {
    rightTurnPercent = constrain(map((int)smoothRightTurn, rightTurnDeadzone, rightTurnMax, 0, 100), 0, 100);
  }

  // 4. Print raw and mapped key-value pairs for Serial Plotter and Unity
  Serial.print(F("Raw_Thrust:"));
  Serial.print(rawThrust);
  Serial.print(F(",Broom_Speed:"));
  Serial.print(speedPercent);
  Serial.print(F(",Raw_Altitude:"));
  Serial.print(rawAltitude);
  Serial.print(F(",Broom_Altitude:"));
  Serial.print(altPercent);
  Serial.print(F(",Raw_LeftTurn:"));
  Serial.print(rawLeftTurn);
  Serial.print(F(",Broom_LeftTurn:"));
  Serial.print(leftTurnPercent);
  Serial.print(F(",Raw_RightTurn:"));
  Serial.print(rawRightTurn);
  Serial.print(F(",Broom_RightTurn:"));
  Serial.println(rightTurnPercent);

  delay(25); // ~40Hz Refresh rate
}