// Component 01: Motion Rendering - Dual Pressure Sensor Handle (Thrust & Altitude)
const int thrustFsrPin = A0;   // FSR 1: Forward Thrust (Pin A0)
const int altitudeFsrPin = A1; // FSR 2: Altitude / Lift (Pin A1)

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

  // 3. Scale Altitude output (If FSR 2 registers 0-1, map 1 to 100% altitude thrust)
  int altPercent = (rawAltitude == 1) ? 100 : ((rawAltitude > 1) ? map(rawAltitude, 0, 7, 0, 100) : 0);

  // 4. Print raw and speed/altitude key-value pairs for Serial Plotter and Unity
  Serial.print("Raw_Thrust:");
  Serial.print(rawThrust);
  Serial.print(",");
  Serial.print("Broom_Speed:");
  Serial.print(rawThrust);
  Serial.print(",");
  Serial.print("Raw_Altitude:");
  Serial.print(rawAltitude);
  Serial.print(",");
  Serial.print("Broom_Altitude:");
  Serial.println(altPercent);

  delay(30); // Refresh rate
}