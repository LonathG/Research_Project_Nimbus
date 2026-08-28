// Component 01: Motion Rendering - Dual Pressure Sensor Handle (Thrust & Altitude)
const int thrustFsrPin = A0;   // FSR 1: Forward Thrust (Pin A0)
const int altitudeFsrPin = A1; // FSR 2: Altitude / Lift (Pin A1)

// Calibration thresholds (Adjust based on your squeeze intensity)
const int thrustDeadzone = 15;    // Below this value is considered resting/zero
const int thrustMax = 350;        // Max expected reading for FSR 1 (e.g. 100-500)

const int altitudeDeadzone = 25;  // Below this value is considered resting/zero
const int altitudeMax = 700;      // Max expected reading for FSR 2 (e.g. 700)

void setup() {
  Serial.begin(9600);
  Serial.println("--- DUAL FSR MODE: THRUST (A0) & ALTITUDE (A1) ---");
}

void loop() {
  // 1. Read FSR 1 (Thrust on A0)
  int rawThrust = analogRead(thrustFsrPin);

  // 2. Read FSR 2 (Altitude on A1) with ADC settling dummy read
  analogRead(altitudeFsrPin); // dummy read to settle ADC multiplexer
  int rawAltitude = analogRead(altitudeFsrPin);

  // 3. Map raw readings to 0-100% with deadzone and constrain to prevent runaway numbers
  int speedPercent = 0;
  if (rawThrust > thrustDeadzone) {
    speedPercent = constrain(map(rawThrust, thrustDeadzone, thrustMax, 0, 100), 0, 100);
  }

  int altPercent = 0;
  if (rawAltitude > altitudeDeadzone) {
    altPercent = constrain(map(rawAltitude, altitudeDeadzone, altitudeMax, 0, 100), 0, 100);
  }

  // 4. Print raw and speed/altitude key-value pairs for Serial Plotter and Unity
  Serial.print("Raw_Thrust:");
  Serial.print(rawThrust);
  Serial.print(",");
  Serial.print("Broom_Speed:");
  Serial.print(speedPercent);
  Serial.print(",");
  Serial.print("Raw_Altitude:");
  Serial.print(rawAltitude);
  Serial.print(",");
  Serial.print("Broom_Altitude:");
  Serial.println(altPercent);

  delay(30); // Refresh rate
}